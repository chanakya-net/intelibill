using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Services;
using Intelibill.Application.Common.Operations;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;

public sealed class VoidSaleReturnCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IInventoryRepository inventoryRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IStockTransactionRepository stockTransactionRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    ICreditNoteRepository creditNoteRepository,
    ISaleReturnMutationCoordinator mutationCoordinator,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock, TillSourceSettlementService settlement, IFinancialOperationExecutor executor, Intelibill.Application.Features.Expiry.BatchDispositionService dispositions)
{
    public async Task<ErrorOr<Success>> HandleAsync(
        VoidSaleReturnCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Errors.Sale.ReturnVoidReasonRequired;

        var user = await userRepository.GetByIdAsync(command.ActorUserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.ActorUserId, command.ShopId, cancellationToken);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Sale.ReturnForbidden;

        // Only the sale id is read before the lock; it never changes. Everything the void decides
        // on is read after it.
        var saleId = await saleReturnRepository.GetSaleIdAsync(command.ShopId, command.SaleReturnId, cancellationToken);
        if (saleId is null)
            return Errors.Sale.ReturnNotFound(command.SaleReturnId);

        var key = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (key is not null)
        {
            var result = await executor.ExecuteAsync<SaleReturnOperationOutcome>(new(command.ShopId, FinancialOperationType.SaleReturnVoid, key),
                command.ActorUserId, command.SaleReturnId, FinancialOperationHasher.Compute(command with { OperationKey = null }), async token =>
                {
                    var posted = await ExecuteAsync(command, shop, saleId.Value, false, token);
                    if (posted.IsError) return posted.Errors;
                    return new SaleReturnOperationOutcome(command.SaleReturnId, saleId.Value);
                }, cancellationToken);
            return result.IsError ? result.Errors : Result.Success;
        }
        return await ExecuteAsync(command, shop, saleId.Value, true, cancellationToken);
    }

    private Task<ErrorOr<Success>> ExecuteAsync(VoidSaleReturnCommand command, Shop shop, Guid saleId, bool ownsTransaction, CancellationToken ct)
    {
        if (ownsTransaction && !settlement.IsEnabled(command.ShopId) && command.Till is null && command.SettlementEvidence is null)
            return mutationCoordinator.ExecuteAsync(command.ShopId, saleId, token => VoidAsync(command, shop, null, token), ct);
        TillSession? session = null;
        return mutationCoordinator.ExecuteAsync(command.ShopId, saleId, async token =>
        {
            if (command.Till is not null && command.SettlementEvidence?.Disposition == TillSettlementDisposition.NoPhysicalMovement)
                return Error.Validation("Till.SettlementEvidenceInvalid", "No physical movement must not select a drawer.");
            var acquired = await settlement.AcquireAsync(command.ShopId, command.ActorUserId, command.Till, command.OperationKey,
                command.SettlementEvidence, command.SettlementEvidence?.Tender?.AppliedAmount ?? 0m,
                businessClock.GetBusinessDate(mutationCoordinator.PostingInstant ?? businessClock.UtcNow, shop.BusinessTimeZoneId), token);
            if (acquired.IsError) return acquired.Errors;
            session = acquired.Value;
            return Result.Success;
        }, token => VoidAsync(command, shop, session, token), ownsTransaction, ct);
    }

    private async Task<ErrorOr<Success>> VoidAsync(
        VoidSaleReturnCommand command,
        Shop shop,
        TillSession? session,
        CancellationToken cancellationToken)
    {
        var saleReturn = await saleReturnRepository.GetByIdWithItemsAsync(
            command.ShopId,
            command.SaleReturnId,
            cancellationToken);
        if (saleReturn is null)
            return Errors.Sale.ReturnNotFound(command.SaleReturnId);

        if (saleReturn.IsVoided)
            return Errors.Sale.ReturnAlreadyVoided;

        // Validate the source's immutable payout evidence before stock, credit or accounting changes.
        if (command.SettlementEvidence is { } evidence)
        {
            var recorded = saleReturn.RecordVoidSettlementEvidence(evidence);
            if (recorded.IsError) return recorded.Errors;
        }

        // Locked so a redemption cannot land between this check and the void.
        var creditNote = await creditNoteRepository.GetBySaleReturnIdForUpdateWithRedemptionsAsync(
            command.ShopId, command.SaleReturnId, cancellationToken);

        if (creditNote is not null && creditNote.HasBeenRedeemed)
            return Errors.Sale.ReturnCreditNoteHasRedemptions;

        var sale = await saleRepository.GetByIdAsync(saleReturn.SaleId, command.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Sale.NotFound(saleReturn.SaleId);

        var saleItemsById = sale.Items.ToDictionary(item => item.Id);
        var processedAt = mutationCoordinator.PostingInstant ?? businessClock.UtcNow;
        // The void lands on the day it happened in the shop's current zone, the one the coordinator
        // fenced the period with.
        var voidDate = businessClock.GetBusinessDate(processedAt, shop.BusinessTimeZoneId);
        var stockReversals = new List<StockTransaction>();

        await dispositions.LockBatchesAsync(command.ShopId, saleReturn.Items.Where(x => x.QuarantineChangeId.HasValue && saleItemsById.ContainsKey(x.SaleItemId))
            .Select(x => saleItemsById[x.SaleItemId]).Where(x => x.InventoryBatchId.HasValue).Select(x => x.InventoryBatchId!.Value), cancellationToken);
        foreach (var returnItem in saleReturn.Items.Where(item => item.Condition == SaleReturnCondition.Restockable))
        {
            if (!saleItemsById.TryGetValue(returnItem.SaleItemId, out var saleItem))
                return Errors.Sale.ReturnSaleItemNotFound(returnItem.SaleItemId);

            if (!saleItem.ItemId.HasValue || !saleItem.InventoryBatchId.HasValue)
                return Errors.Sale.ReturnSaleItemNotFound(returnItem.SaleItemId);

            var itemId = saleItem.ItemId.Value;
            var batchId = saleItem.InventoryBatchId.Value;

            var batch = await inventoryBatchRepository.GetByIdAsync(batchId, cancellationToken);
            if (batch is null || batch.ShopId != command.ShopId)
                return Errors.Sale.ReturnBatchNotFound(batchId);

            var inventory = await inventoryRepository.GetByItemAsync(command.ShopId, itemId, cancellationToken);
            if (inventory is null)
                return Errors.Sale.ReturnInventoryAggregateNotFound(itemId);

            if (batch.Quantity < returnItem.Quantity || inventory.Quantity < returnItem.Quantity)
                return Errors.Sale.ReturnVoidInsufficientStock;

            var inventoryResult = inventory.SubtractQuantity(returnItem.Quantity, command.ActorUserId);
            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            var stockTransaction = StockTransaction.Create(
                command.ShopId,
                itemId,
                batchId,
                StockTransactionType.Out,
                -returnItem.Quantity,
                saleReturn.ReturnNumber,
                command.Reason,
                processedAt,
                command.ActorUserId,
                command.ActorUserId);
            if (stockTransaction.IsError)
                return stockTransaction.Errors;

            ErrorOr<Success> batchResult;
            if (returnItem.QuarantineChangeId.HasValue)
                batchResult = await dispositions.StageReversalAsync(command.ShopId, command.ActorUserId, batch, returnItem.QuarantineChangeId.Value,
                    returnItem.QuarantinedQuantity, stockTransaction.Value.Id, voidDate, saleReturn.Id, returnItem.Id, processedAt, cancellationToken);
            else
                batchResult = batch.SubtractQuantity(returnItem.Quantity, command.ActorUserId);
            if (batchResult.IsError) return batchResult.Errors;
            inventoryBatchRepository.Update(batch);
            inventoryRepository.Update(inventory);
            stockReversals.Add(stockTransaction.Value);
        }

        CustomerLedgerEntry? returnCreditReversal = null;
        if (saleReturn.DueReductionAmount > 0m && sale.CustomerId.HasValue)
        {
            var ledgerResult = CustomerLedgerEntry.Create(
                command.ShopId,
                sale.CustomerId.Value,
                sale.Id,
                CustomerLedgerEntryType.ReturnCreditReversal,
                saleReturn.DueReductionAmount,
                voidDate,
                $"Void return credit from {saleReturn.ReturnNumber} for sale {sale.InvoiceNumber}",
                command.ActorUserId);
            if (ledgerResult.IsError)
                return ledgerResult.Errors;

            returnCreditReversal = ledgerResult.Value;
        }

        var voidResult = saleReturn.Void(processedAt, voidDate, command.ActorUserId, command.Reason);
        if (voidResult.IsError)
            return voidResult.Errors;

        if (creditNote is not null && !creditNote.IsVoided)
        {
            var noteVoidResult = creditNote.Void(command.Reason);
            if (noteVoidResult.IsError)
                return noteVoidResult.Errors;
            creditNoteRepository.Update(creditNote);
        }

        foreach (var stockReversal in stockReversals)
            await stockTransactionRepository.AddAsync(stockReversal, cancellationToken);

        if (returnCreditReversal is not null)
            await customerLedgerEntryRepository.AddAsync(returnCreditReversal, cancellationToken);

        var appended = await settlement.AppendAsync(session, TillMovementSource.SaleReturnVoid, saleReturn.Id,
            command.ActorUserId, command.OperationKey, command.SettlementEvidence, cancellationToken);
        if (appended.IsError) return appended.Errors;
        saleReturnRepository.Update(saleReturn);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
