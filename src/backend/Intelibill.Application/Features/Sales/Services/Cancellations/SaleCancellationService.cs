using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Events;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Intelibill.Application.Features.Sales.Services.Cancellations;

public sealed class SaleCancellationService(IShopRepository shops, ISaleRepository sales, SaleCancellationPolicy policy,
    ISaleCancellationRollout rollout, IBusinessClock clock, IPostingPeriodPolicy periods, IFinancialOperationExecutor executor,
    IInventoryBatchRepository batches, IInventoryRepository inventory, IStockTransactionRepository movements,
    ICustomerLedgerEntryRepository ledger, ISaleCancellationRepository cancellations, IUnitOfWork uow, IMessageBus bus, ITillPostingService tills, Intelibill.Application.Features.Expiry.IExpiryPolicyFence expiryFence, Intelibill.Application.Features.Expiry.BatchDispositionService dispositions)
{
    private static readonly CancelSaleRequestValidator Validator = new();
    private async Task<ErrorOr<Shop>> AuthorizeAsync(Guid actor, Guid shopId, Guid saleId, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) is not (ShopRole.Owner or ShopRole.Manager)) return Errors.SaleCancellation.Forbidden;
        if (!await sales.ExistsAsync(shopId, saleId, ct)) return Errors.Sale.NotFound(saleId);
        var shop = await shops.GetByIdAsync(shopId, ct);
        return shop is null ? Errors.Shop.ShopNotFound : shop;
    }
    public async Task<ErrorOr<SaleCancellationPreviewDto>> PreviewAsync(Guid actorUserId, Guid shopId, Guid saleId, CancellationToken ct)
    {
        var access = await AuthorizeAsync(actorUserId, shopId, saleId, ct); if (access.IsError) return access.Errors;
        var sale = await sales.GetByIdAsync(saleId, shopId, ct); if (sale is null) return Errors.Sale.NotFound(saleId);
        var day = clock.GetBusinessDay(clock.UtcNow, access.Value.BusinessTimeZoneId);
        var e = await policy.EvaluateAsync(shopId, sale, day, ct);
        var previewLines = new List<SaleCancellationPreviewLineDto>();
        foreach (var line in e.Lines)
        {
            var batch = line.InventoryBatchId is { } id ? await batches.GetByIdAsync(id, ct) : null;
            if (batch is null || batch.ShopId != shopId) { previewLines.Add(line); continue; }
            var disposition = await dispositions.EvaluateAsync(batch, day.Date, line.Quantity, ct);
            if (disposition.IsError) return disposition.Errors;
            previewLines.Add(line with { PhysicalRestorationQuantity = disposition.Value.PhysicalQuantity,
                QuarantinedQuantity = disposition.Value.QuarantinedQuantity, DispositionReasonCode = disposition.Value.ReasonCode });
        }
        var cash = sale.PaymentMethod == PaymentMethod.Cash ? sale.PaidAmount : 0m;
        return new SaleCancellationPreviewDto(sale.Id, sale.InvoiceNumber, sale.InvoiceDocument, sale.Status, sale.Version, e.Eligible, rollout.IsPostingEnabled(shopId), day.Date, e.Reasons, previewLines, sale.TotalAmount, sale.DueAmount, cash, sale.PaidAmount - cash, e.CreditRestorationAmount, true, cash > 0);
    }
    public async Task<ErrorOr<SaleCancellationDto>> CancelAsync(Guid actorUserId, Guid shopId, Guid saleId, CancelSaleRequest request, CancellationToken ct)
    {
        var validation = Validator.Validate(request);
        if (!validation.IsValid) return validation.Errors.Select(x => Error.Validation("SaleCancellation.InvalidRequest", x.ErrorMessage)).ToList();
        var access = await AuthorizeAsync(actorUserId, shopId, saleId, ct); if (access.IsError) return access.Errors;
        var canonical = new { saleId, request.ExpectedVersion, Reason = request.Reason.Trim(), request.GoodsRecoveredConfirmed, request.CashRefundConfirmed };
        var hash = request.Till is null ? FinancialOperationHasher.Compute(canonical) : FinancialOperationHasher.Compute(new { legacyHash = FinancialOperationHasher.Compute(canonical), actorUserId, request.Till });
        var posted = false;
        ErrorOr<SaleCancellationDto> result;
        try
        {
            result = await executor.ExecuteAsync<SaleCancellationDto>(new(shopId, FinancialOperationType.SaleCancellation, request.OperationKey.Trim()), actorUserId, saleId, hash, async token =>
            {
                if (!rollout.IsPostingEnabled(shopId)) return Errors.SaleCancellation.PostingDisabled;
                var now = clock.UtcNow;
                var day = clock.GetBusinessDay(now, access.Value.BusinessTimeZoneId);
                var fence = await periods.AuthorizeForWriteAsync(shopId, day.Date, token); if (fence.IsError) return fence.Errors;
                var expiry = await expiryFence.AcquireAsync(shopId, false, token); if (expiry.IsError) return expiry.Errors;
                var acquired = await tills.AcquireAsync(shopId, actorUserId, request.Till, day.Date, token);
                if (acquired.IsError) return acquired.Errors;
                var locked = await sales.LockForReturnMutationAsync(shopId, saleId, token);
                if (locked == RowLockOutcome.NotFound) return Errors.Sale.NotFound(saleId);
                if (locked != RowLockOutcome.Acquired) return Errors.SaleCancellation.Busy;
                // AuthorizeAsync deliberately did not track a sale or its dependencies.
                var sale = await sales.GetByIdAsync(saleId, shopId, token); if (sale is null) return Errors.Sale.NotFound(saleId);
                if (sale.Status == SaleStatus.Cancelled) return Errors.SaleCancellation.AlreadyCancelled;
                if (sale.Version != request.ExpectedVersion) return Errors.SaleCancellation.VersionConflict;
                var e = await policy.EvaluateAsync(shopId, sale, day, token);
                if (!e.Eligible) return e.Reasons.Select(x => Error.Conflict(x.Code, x.Message)).ToList();
                if (request.CashRefundConfirmed != (sale.PaidAmount > 0)) return Error.Validation("SaleCancellation.CashConfirmationInvalid", sale.PaidAmount > 0 ? "Confirm the exact original cash amount was paid back." : "A sale with no paid amount must not confirm a cash refund.");
                List<SaleCancellationLineInput> lines = [];
                List<(InventoryBatch Batch, Guid SaleItemId, StockTransaction Movement)> restorations = [];
                var heldBatchIds = new List<Guid>();
                foreach (var line in sale.Items)
                {
                    var originalBatch = await batches.GetByIdAsync(line.InventoryBatchId!.Value, token);
                    var restored = await dispositions.EvaluateAsync(originalBatch!, day.Date, line.Quantity, token);
                    if (restored.IsError) return restored.Errors;
                    if (restored.Value.QuarantinedQuantity > 0) heldBatchIds.Add(originalBatch!.Id);
                }
                await dispositions.LockBatchesAsync(shopId, heldBatchIds, token);
                foreach (var line in sale.Items.OrderBy(x => x.ItemId).ThenBy(x => x.InventoryBatchId).ThenBy(x => x.Id))
                {
                    var batch = await batches.GetByIdAsync(line.InventoryBatchId!.Value, token);
                    var stock = await inventory.GetByItemAsync(shopId, line.ItemId!.Value, token);
                    var added = stock!.AddQuantity(line.Quantity, actorUserId); if (added.IsError) return added.Errors;
                    var movement = StockTransaction.Create(shopId, line.ItemId.Value, batch!.Id, StockTransactionType.SaleCancellation, line.Quantity, sale.InvoiceNumber, request.Reason.Trim(), now, actorUserId, actorUserId);
                    if (movement.IsError) return movement.Errors;
                    await movements.AddAsync(movement.Value, token);
                    restorations.Add((batch!, line.Id, movement.Value)); inventory.Update(stock);
                    var frozen = e.Lines.Single(x => x.SaleItemId == line.Id);
                    lines.Add(new(shopId, saleId, line.Id, line.ItemId.Value, batch.Id, movement.Value.Id, line.Quantity, frozen.Description, frozen.Uqc, line.CostPrice, line.TotalAmount, SaleEconomicsCalculator.RecordedCost(line.Quantity, line.CostPrice)));
                }
                Guid? ledgerId = null;
                if (sale.DueAmount > 0)
                {
                    var credit = CustomerLedgerEntry.Create(shopId, sale.CustomerId!.Value, saleId, CustomerLedgerEntryType.SaleCancellationCredit, sale.DueAmount, day.Date, request.Reason.Trim(), actorUserId);
                    if (credit.IsError) return credit.Errors;
                    ledgerId = credit.Value.Id; await ledger.AddAsync(credit.Value, token);
                }
                var document = sale.InvoiceDocument!;
                var fact = SaleCancellation.Record(new SaleCancellationInput(shopId, saleId, actorUserId, request.OperationKey, request.Reason, now, day.Date, day.TimeZoneId, sale.InvoiceNumber, sale.BusinessDate, sale.TotalAmount, sale.TotalAmount, 0m, sale.DueAmount, sale.PaidAmount, ledgerId, document.Seller, document.Buyer, true, request.CashRefundConfirmed, lines));
                if (fact.IsError) return fact.Errors;
                foreach (var restoration in restorations)
                {
                    var recoveredLine = fact.Value.Lines.Single(x => x.SaleItemId == restoration.SaleItemId);
                    var disposition = await dispositions.EvaluateAsync(restoration.Batch, day.Date, recoveredLine.Quantity, token);
                    if (disposition.IsError) return disposition.Errors;
                    var staged = await dispositions.StageRestorationAsync(shopId, actorUserId, restoration.Batch, "SaleCancellation",
                        fact.Value.Id, recoveredLine.Id, restoration.Movement.Id, disposition.Value, day.Date, now, token);
                    if (staged.IsError) return staged.Errors;
                    recoveredLine.FreezeDisposition(staged.Value, disposition.Value.QuarantinedQuantity, disposition.Value.ReasonCode);
                }
                var cancelled = sale.MarkCancelled(); if (cancelled.IsError) return cancelled.Errors;
                sales.Update(sale); await cancellations.AddAsync(fact.Value, token);
                if (acquired.Value is { } session)
                {
                    var appended = await tills.AppendAsync(session, new(TillMovementSource.SaleCancellation, fact.Value.Id, "cash-refund",
                        sale.PaidAmount > 0 ? [new TenderLine(ReceiptTenderMethod.Cash, sale.PaidAmount, null, null, null, null)] : [],
                        -1m, now, request.OperationKey.Trim(), request.Reason.Trim(), ReceivableChange: -sale.DueAmount), actorUserId, token);
                    if (appended.IsError) return appended.Errors;
                }
                await uow.SaveChangesAsync(token);
                posted = true;
                return SaleCancellationDto.From(fact.Value);
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { uow.ClearChanges(); return Errors.SaleCancellation.VersionConflict; }
        catch (Exception ex) when (SqlStateOf(ex) is "55P03" or "40P01" or "40001") { uow.ClearChanges(); return Errors.SaleCancellation.Busy; }
        if (!result.IsError && posted) await bus.PublishAsync(new InventoryStockChangedDomainEvent(shopId, result.Value.Lines.Select(x => x.ItemId).Distinct().ToArray()));
        return result;
    }
    private static string? SqlStateOf(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException) if (current is DbException { SqlState: { } state }) return state;
        return null;
    }
}
