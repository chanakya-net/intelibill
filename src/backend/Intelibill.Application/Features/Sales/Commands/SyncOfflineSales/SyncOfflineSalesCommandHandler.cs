using System.Globalization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed class SyncOfflineSalesCommandHandler(
    IUserRepository userRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    ISaleLineValidator saleLineValidator,
    ICustomerResolver customerResolver,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IDiscountRuleRepository discountRuleRepository,
    IOfflineSaleVarianceAnalyzer varianceAnalyzer,
    IUnitOfWork unitOfWork)
{
    private const string StatusCreated = "created";
    private const string StatusSyncedWithWarnings = "SyncedWithWarnings";

    public async Task<ErrorOr<OfflineSalesSyncResponseDto>> HandleAsync(
        SyncOfflineSalesCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null) return Errors.Auth.UserNotFound;

        var isMember = actor.ShopMemberships.Any(sm => sm.ShopId == command.ShopId);
        if (!isMember) return Errors.Shop.MembershipNotFound;

        var deviceId = command.DeviceId.Trim();
        var now = DateTimeOffset.UtcNow;
        var activeLeases = await invoiceLeaseRepository.GetActiveByDeviceAsync(command.ShopId, deviceId, now, cancellationToken);
        var activeDiscountRules = await discountRuleRepository.GetActiveByShopAsync(command.ShopId, now, cancellationToken);
        var activeDiscountRulesById = activeDiscountRules.ToDictionary(rule => rule.Id);

        var results = new List<OfflineSaleSyncResultDto>(command.Sales.Count);
        foreach (var sale in command.Sales)
        {
            results.Add(await SyncSaleAsync(command, sale, deviceId, now, activeLeases, activeDiscountRulesById, cancellationToken));
        }

        return new OfflineSalesSyncResponseDto(results);
    }

    private async Task<OfflineSaleSyncResultDto> SyncSaleAsync(
        SyncOfflineSalesCommand command,
        OfflineSaleSyncCommand sale,
        string deviceId,
        DateTimeOffset now,
        IReadOnlyList<InvoiceLease> activeLeases,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById,
        CancellationToken cancellationToken)
    {
        var normalizedId = string.IsNullOrWhiteSpace(sale.ClientSaleId) ? string.Empty : sale.ClientSaleId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedId)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.ClientSaleIdRequired);
        if (normalizedId.Length > 120) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.ClientSaleIdTooLong);

        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, deviceId, sale);
        var existing = await saleRepository.GetByClientSaleIdAsync(command.ShopId, deviceId, normalizedId, cancellationToken);

        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                await varianceAnalyzer.AddReviewIssueAsync(command.ShopId, existing.Id, normalizedId, deviceId, ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict.Code, Errors.Sale.IdempotencyConflict.Description, command.ActorUserId, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return OfflineSaleSyncHelpers.BuildNeedsReviewResult(normalizedId, Errors.Sale.IdempotencyConflict);
            }
            return OfflineSaleSyncHelpers.BuildDuplicateResult(normalizedId, existing);
        }

        var invNum = (sale.InvoiceNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(invNum)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.InvoiceNumberRequired);
        if (invNum.Length > 40) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.InvoiceNumberTooLong);
        if (sale.Items.Count == 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.ItemsRequired);
        if (sale.PaidAmount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.PaidAmountInvalid);
        if (sale.DueAmount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.DueAmountInvalid);
        if (sale.TotalAmount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.TotalAmountInvalid);
        if (sale.TotalTaxAmount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.TotalTaxAmountInvalid);
        if (sale.SubtotalBeforeDiscount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.SubtotalBeforeDiscountInvalid);
        if (sale.TotalBeforeDiscount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.TotalBeforeDiscountInvalid);
        if (sale.TotalDiscountAmount < 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.TotalDiscountAmountInvalid);

        if (sale.PaymentMethod == PaymentMethod.Credit && sale.DueAmount <= 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.CreditRequiresDueAmount);
        if (sale.DueAmount > 0 && !sale.CustomerId.HasValue && string.IsNullOrWhiteSpace(sale.CustomerPhone)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.CustomerIdentityRequiredForDue);
        if (!AmountsMatch(sale.PaidAmount, sale.DueAmount, sale.TotalAmount)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.PaidAndDueAmountMismatch);

        var warnings = new List<string>();
        var pendingIssues = new List<ReconciliationIssue>();
        var customerRes = await customerResolver.ResolveAsync(command.ShopId, sale.CustomerId, sale.CustomerPhone, sale.DueAmount > 0, sale.PaymentMethod, cancellationToken);
        if (customerRes.IsError) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, customerRes.FirstError);

        var resolvedCust = customerRes.Value;
        OfflineSaleSyncHelpers.AddCustomerVarianceWarnings(command.ShopId, command.ActorUserId, normalizedId, deviceId, sale, resolvedCust, warnings, pendingIssues);

        if (!TryMatchLease(activeLeases, invNum, out _))
        {
            await varianceAnalyzer.AddReviewIssueAsync(command.ShopId, null, normalizedId, deviceId, ReconciliationIssueType.InvoiceConflict, Errors.Sale.InvoiceLeaseNotFound.Code, Errors.Sale.InvoiceLeaseNotFound.Description, command.ActorUserId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return OfflineSaleSyncHelpers.BuildNeedsReviewResult(normalizedId, Errors.Sale.InvoiceLeaseNotFound);
        }

        if (await saleRepository.GetByInvoiceNumberAsync(command.ShopId, invNum, cancellationToken) is not null)
        {
            await varianceAnalyzer.AddReviewIssueAsync(command.ShopId, null, normalizedId, deviceId, ReconciliationIssueType.InvoiceConflict, Errors.Sale.InvoiceNumberAlreadyUsed.Code, Errors.Sale.InvoiceNumberAlreadyUsed.Description, command.ActorUserId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return OfflineSaleSyncHelpers.BuildNeedsReviewResult(normalizedId, Errors.Sale.InvoiceNumberAlreadyUsed);
        }

        var lineCmds = new List<RecordSaleItemCommand>(sale.Items.Count);
        var lineByKey = new Dictionary<string, OfflineSaleSyncLineCommand>(sale.Items.Count);
        for (var i = 0; i < sale.Items.Count; i++)
        {
            var line = sale.Items[i];
            if (string.IsNullOrWhiteSpace(line.Barcode)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.BarcodeRequired);
            if (string.IsNullOrWhiteSpace(line.BatchNumber)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.BatchNumberRequired);
            if (string.IsNullOrWhiteSpace(line.ItemName)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.ItemNameRequired);
            if (line.InventoryBatchId == Guid.Empty) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.InventoryBatchIdRequired);
            if (line.Quantity <= 0) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.OfflineLineQuantityMustBePositive);
            if (OfflineSaleSyncHelpers.HasNegativeOfflineLineAmounts(line)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.Sale.OfflineLineAmountsInvalid);

            var key = i.ToString(CultureInfo.InvariantCulture);
            lineByKey.Add(key, line);
            lineCmds.Add(new RecordSaleItemCommand(line.Barcode, line.BatchNumber, line.ItemName, line.Quantity, line.CostPrice, line.SalesPrice, line.Mrp, line.TaxRatePercent, line.IsPriceIncludingTax, line.InventoryBatchId, null, key, line.HsnCode));
        }

        var warnCountBefore = warnings.Count;
        var validation = await saleLineValidator.ValidateLinesAsync(command.ShopId, lineCmds, warnings, cancellationToken, true);
        if (validation.IsError) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, validation.FirstError);

        var vLines = validation.Value.Lines;
        OfflineSaleSyncHelpers.AddValidationVarianceIssues(command.ShopId, command.ActorUserId, normalizedId, deviceId, warnings.Skip(warnCountBefore), pendingIssues);
        OfflineSaleSyncHelpers.AddPricingVarianceWarnings(command.ShopId, command.ActorUserId, normalizedId, deviceId, vLines, lineByKey, warnings, pendingIssues);
        OfflineSaleSyncHelpers.AddDiscountVarianceWarnings(command.ShopId, command.ActorUserId, normalizedId, deviceId, sale, lineByKey.Values, activeDiscountRulesById, warnings, pendingIssues);

        var saleItems = new List<SaleItem>(vLines.Count);
        foreach (var v in vLines)
        {
            if (!lineByKey.TryGetValue(v.Command.ClientLineKey!, out var line)) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, Errors.General.Unexpected("Line mapping failed."));
            saleItems.Add(SaleItem.CreateGoods(
                command.ShopId,
                v.Item!.Id,
                v.Batch!.Id,
                lineName: line.ItemName,
                lineCode: line.Barcode,
                line.Quantity,
                line.CostPrice,
                line.SalesPrice,
                line.Mrp,
                line.TaxRatePercent,
                line.IsPriceIncludingTax,
                v.HasPriceMismatch,
                preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
                itemDiscountAmount: line.ItemDiscountAmount,
                saleDiscountAmount: line.SaleDiscountAmount,
                taxableAmount: line.TaxableAmount,
                taxAmount: line.TaxAmount,
                totalAmount: line.TotalAmount,
                configuredBatchRuleId: line.ConfiguredBatchRuleId,
                configuredBatchRulePercentage: line.ConfiguredBatchRulePercentage,
                itemDiscountOverrideType: line.ItemDiscountOverrideType,
                itemDiscountOverrideValue: line.ItemDiscountOverrideValue,
                hsnCode: line.HsnCode));
        }

        var consumption = OfflineSaleSyncHelpers.BuildStockConsumptionPlan(command.ShopId, command.ActorUserId, normalizedId, deviceId, vLines, warnings, pendingIssues);
        var saleEntity = Sale.Create(command.ShopId, command.ActorUserId, OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, normalizedId), hash, invNum, resolvedCust?.Id ?? sale.CustomerId, sale.CustomerName ?? resolvedCust?.Name, sale.CustomerPhone ?? resolvedCust?.PhoneNumber, sale.PaymentMethod, sale.SoldAt, sale.PaidAmount, sale.DueAmount, sale.TotalAmount, sale.TotalTaxAmount, saleItems, sale.SubtotalBeforeDiscount, sale.TotalBeforeDiscount, sale.TotalDiscountAmount, sale.ConfiguredSaleRuleId, sale.ConfiguredSaleRuleType, sale.ConfiguredSaleRulePercentage, sale.ConfiguredSaleRuleThresholdAmount, sale.SaleDiscountOverrideType, sale.SaleDiscountOverrideValue, SaleSource.Offline, normalizedId, deviceId, now, warnings);

        for (var i = 0; i < vLines.Count; i++)
        {
            var consumed = consumption[i].ConsumedQuantity;
            if (consumed <= 0) continue;
            var batchResult = vLines[i].Batch!.SubtractQuantity(consumed, command.ActorUserId);
            if (batchResult.IsError) throw new InvalidOperationException(batchResult.FirstError.Description);
            var inventoryResult = vLines[i].Inventory!.SubtractQuantity(consumed, command.ActorUserId);
            if (inventoryResult.IsError) throw new InvalidOperationException(inventoryResult.FirstError.Description);
            var tx = StockTransaction.Create(command.ShopId, vLines[i].Item!.Id, vLines[i].Batch!.Id, StockTransactionType.Out, -consumed, invNum, null, sale.SoldAt, command.ActorUserId, command.ActorUserId);
            if (tx.IsError) throw new InvalidOperationException(tx.FirstError.Description);
            await stockTransactionRepository.AddAsync(tx.Value, cancellationToken);
        }

        await saleRepository.AddAsync(saleEntity, cancellationToken);
        await varianceAnalyzer.PersistReviewIssuesAsync(saleEntity.Id, pendingIssues, cancellationToken);

        if (saleEntity.DueAmount > 0 && saleEntity.CustomerId.HasValue)
        {
            var ledger = CustomerLedgerEntry.Create(saleEntity.ShopId, saleEntity.CustomerId.Value, saleEntity.Id, CustomerLedgerEntryType.SaleDue, saleEntity.DueAmount, DateOnly.FromDateTime(saleEntity.SoldAt.UtcDateTime), $"Due recorded from sale {saleEntity.InvoiceNumber}", command.ActorUserId);
            if (ledger.IsError) return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, ledger.FirstError);
            await customerLedgerEntryRepository.AddAsync(ledger.Value, cancellationToken);
        }

        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex)
        {
            unitOfWork.ClearChanges();
            var concurrent = await saleRepository.GetByClientSaleIdAsync(command.ShopId, deviceId, normalizedId, cancellationToken);
            if (concurrent is null)
            {
                var saveErr = OfflineSaleSyncHelpers.GetSaveFailureError(ex);
                if (saveErr.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code)
                {
                    await varianceAnalyzer.AddReviewIssueAsync(command.ShopId, null, normalizedId, deviceId, ReconciliationIssueType.InvoiceConflict, saveErr.Code, saveErr.Description, command.ActorUserId, cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return OfflineSaleSyncHelpers.BuildNeedsReviewResult(normalizedId, saveErr);
                }
                return OfflineSaleSyncHelpers.BuildErrorResult(normalizedId, saveErr);
            }
            if (!string.Equals(concurrent.RequestHash, hash, StringComparison.Ordinal))
            {
                await varianceAnalyzer.AddReviewIssueAsync(command.ShopId, concurrent.Id, normalizedId, deviceId, ReconciliationIssueType.ValidationConflict, Errors.Sale.IdempotencyConflict.Code, Errors.Sale.IdempotencyConflict.Description, command.ActorUserId, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return OfflineSaleSyncHelpers.BuildNeedsReviewResult(normalizedId, Errors.Sale.IdempotencyConflict);
            }
            return OfflineSaleSyncHelpers.BuildDuplicateResult(normalizedId, concurrent);
        }

        return new OfflineSaleSyncResultDto(normalizedId, warnings.Count > 0 ? StatusSyncedWithWarnings : StatusCreated, saleEntity.Id, saleEntity.InvoiceNumber, []) { Warnings = warnings };
    }

    private static bool AmountsMatch(decimal paid, decimal due, decimal total) => decimal.Round(paid + due, 2, MidpointRounding.AwayFromZero) == decimal.Round(total, 2, MidpointRounding.AwayFromZero);

    private static bool TryMatchLease(IReadOnlyList<InvoiceLease> leases, string num, out InvoiceLease? matched)
    {
        foreach (var l in leases)
        {
            if (num.StartsWith(l.Prefix, StringComparison.Ordinal) && int.TryParse(num[l.Prefix.Length..], out var n) && n >= l.RangeStart && n <= l.RangeEnd)
            {
                matched = l; return true;
            }
        }
        matched = null; return false;
    }
}
