using System.Globalization;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed class OfflineSalePoster(
    ISaleLineValidator saleLineValidator,
    ICustomerResolver customerResolver,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IOfflineSaleVarianceAnalyzer varianceAnalyzer) : IOfflineSalePoster
{
    private const int MaxReviewMessageLength = 1000;
    private const int MaxReportedMismatches = 100;

    public ErrorOr<Success> CheckStructure(OfflineSaleSyncCommand sale)
    {
        if (sale.Items is null || sale.Items.Count == 0) return Errors.Sale.ItemsRequired;
        if (sale.PaidAmount < 0) return Errors.Sale.PaidAmountInvalid;
        if (sale.DueAmount < 0) return Errors.Sale.DueAmountInvalid;
        if (sale.TotalAmount < 0) return Errors.Sale.TotalAmountInvalid;
        if (sale.TotalTaxAmount < 0) return Errors.Sale.TotalTaxAmountInvalid;
        if (sale.SubtotalBeforeDiscount < 0) return Errors.Sale.SubtotalBeforeDiscountInvalid;
        if (sale.TotalBeforeDiscount < 0) return Errors.Sale.TotalBeforeDiscountInvalid;
        if (sale.TotalDiscountAmount < 0) return Errors.Sale.TotalDiscountAmountInvalid;

        if (sale.PaymentMethod == PaymentMethod.Credit && sale.DueAmount <= 0) return Errors.Sale.CreditRequiresDueAmount;
        if (sale.DueAmount > 0 && !sale.CustomerId.HasValue && string.IsNullOrWhiteSpace(sale.CustomerPhone)) return Errors.Sale.CustomerIdentityRequiredForDue;
        if (!AmountsMatch(sale.PaidAmount, sale.DueAmount, sale.TotalAmount)) return Errors.Sale.PaidAndDueAmountMismatch;

        foreach (var line in sale.Items)
        {
            if (string.IsNullOrWhiteSpace(line.Barcode)) return Errors.Sale.BarcodeRequired;
            if (string.IsNullOrWhiteSpace(line.ItemName)) return Errors.Sale.ItemNameRequired;
            if (!line.IsServiceLine && string.IsNullOrWhiteSpace(line.BatchNumber)) return Errors.Sale.BatchNumberRequired;
            if (!line.IsServiceLine && line.InventoryBatchId == Guid.Empty) return Errors.Sale.InventoryBatchIdRequired;
            if (line.IsServiceLine && !line.ServiceId.HasValue) return Errors.Sale.ServiceIdRequired;
            if (line.Quantity <= 0) return Errors.Sale.OfflineLineQuantityMustBePositive;
            if (OfflineSaleSyncHelpers.HasNegativeOfflineLineAmounts(line)) return Errors.Sale.OfflineLineAmountsInvalid;
        }

        return Result.Success;
    }

    public async Task<OfflineSalePostingOutcome> StageAsync(OfflineSalePostingRequest request, CancellationToken cancellationToken)
    {
        var sale = request.Sale;
        var structure = CheckStructure(sale);
        if (structure.IsError) return new OfflineSalePostingOutcome.Rejected(structure.FirstError);

        var shopId = request.ShopId;
        var deviceId = request.DeviceId;
        var clientSaleId = request.ClientSaleId;
        var recordedBy = request.RecordedByUserId;
        var invoiceNumber = (sale.InvoiceNumber ?? string.Empty).Trim();

        var warnings = new List<string>();
        var pendingIssues = new List<ReconciliationIssue>();
        var customerRes = await customerResolver.ResolveAsync(shopId, sale.CustomerId, sale.CustomerPhone, sale.DueAmount > 0, sale.PaymentMethod, cancellationToken);
        if (customerRes.IsError) return new OfflineSalePostingOutcome.Rejected(customerRes.FirstError);

        var resolvedCust = customerRes.Value;
        OfflineSaleSyncHelpers.AddCustomerVarianceWarnings(shopId, recordedBy, clientSaleId, deviceId, sale, resolvedCust, warnings, pendingIssues);

        var lineCmds = new List<RecordSaleItemCommand>(sale.Items.Count);
        var lineByKey = new Dictionary<string, OfflineSaleSyncLineCommand>(sale.Items.Count);
        for (var i = 0; i < sale.Items.Count; i++)
        {
            var line = sale.Items[i];
            var key = i.ToString(CultureInfo.InvariantCulture);
            lineByKey.Add(key, line);
            var lineType = line.IsServiceLine ? SaleLineType.Service : SaleLineType.Goods;
            lineCmds.Add(new RecordSaleItemCommand(line.Barcode, line.BatchNumber, line.ItemName, line.Quantity, line.CostPrice, line.SalesPrice, line.Mrp, line.TaxRatePercent, line.IsPriceIncludingTax, line.InventoryBatchId, null, key, line.HsnCode, lineType, line.ServiceId));
        }

        // Frozen amounts must reconcile with their own inputs before anything is posted; catalog variance is reported separately below.
        var arithmetic = OfflineInvoiceArithmeticValidator.Validate(sale);
        if (!arithmetic.IsConsistent)
        {
            var arithmeticError = arithmetic.IsUnsupportedVersion ? Errors.Sale.OfflineCalculationVersionUnsupported : Errors.Sale.OfflineArithmeticMismatch;
            return new OfflineSalePostingOutcome.Held(
                ReconciliationIssueType.ArithmeticConflict,
                arithmeticError,
                arithmetic.Describe(MaxReviewMessageLength),
                [.. arithmetic.Mismatches.Take(MaxReportedMismatches)]);
        }

        var warnCountBefore = warnings.Count;
        var validation = await saleLineValidator.ValidateLinesAsync(shopId, lineCmds, warnings, cancellationToken, true);
        if (validation.IsError) return new OfflineSalePostingOutcome.Rejected(validation.FirstError);

        var vLines = validation.Value.Lines;
        OfflineSaleSyncHelpers.AddValidationVarianceIssues(shopId, recordedBy, clientSaleId, deviceId, warnings.Skip(warnCountBefore), pendingIssues);
        OfflineSaleSyncHelpers.AddPricingVarianceWarnings(shopId, recordedBy, clientSaleId, deviceId, vLines, lineByKey, warnings, pendingIssues);
        OfflineSaleSyncHelpers.AddDiscountVarianceWarnings(shopId, recordedBy, clientSaleId, deviceId, sale, lineByKey.Values, request.ActiveDiscountRulesById, warnings, pendingIssues);

        var saleItems = new List<SaleItem>(vLines.Count);
        foreach (var v in vLines)
        {
            if (!lineByKey.TryGetValue(v.Command.ClientLineKey!, out var line)) return new OfflineSalePostingOutcome.Rejected(Errors.General.Unexpected("Line mapping failed."));
            if (v.LineType == SaleLineType.Service)
            {
                saleItems.Add(SaleItem.CreateService(
                    shopId,
                    v.Service!.Id,
                    lineName: line.ItemName,
                    lineCode: line.Barcode,
                    line.Quantity,
                    costPrice: 0m,
                    line.SalesPrice,
                    mrp: 0m,
                    line.TaxRatePercent,
                    line.IsPriceIncludingTax,
                    hasPriceMismatch: false,
                    preTaxAmountBeforeDiscount: line.PreTaxAmountBeforeDiscount,
                    itemDiscountAmount: 0m,
                    saleDiscountAmount: line.SaleDiscountAmount,
                    taxableAmount: line.TaxableAmount,
                    taxAmount: line.TaxAmount,
                    totalAmount: line.TotalAmount,
                    hsnCode: line.HsnCode));
            }
            else
            {
                saleItems.Add(SaleItem.CreateGoods(
                    shopId,
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
        }

        var consumption = OfflineSaleSyncHelpers.BuildStockConsumptionPlan(shopId, recordedBy, clientSaleId, deviceId, vLines, warnings, pendingIssues);
        var saleEntity = Sale.Create(shopId, request.AttributedUserId, OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, clientSaleId), request.PayloadHash, invoiceNumber, resolvedCust?.Id ?? sale.CustomerId, sale.CustomerName ?? resolvedCust?.Name, sale.CustomerPhone ?? resolvedCust?.PhoneNumber, sale.PaymentMethod, sale.SoldAt, sale.PaidAmount, sale.DueAmount, sale.TotalAmount, sale.TotalTaxAmount, saleItems, sale.SubtotalBeforeDiscount, sale.TotalBeforeDiscount, sale.TotalDiscountAmount, sale.ConfiguredSaleRuleId, sale.ConfiguredSaleRuleType, sale.ConfiguredSaleRulePercentage, sale.ConfiguredSaleRuleThresholdAmount, sale.SaleDiscountOverrideType, sale.SaleDiscountOverrideValue, SaleSource.Offline, clientSaleId, deviceId, request.Now, warnings);

        // Built before anything is staged so a refused entry leaves the unit of work untouched.
        CustomerLedgerEntry? dueEntry = null;
        if (saleEntity.DueAmount > 0 && saleEntity.CustomerId.HasValue)
        {
            var ledger = CustomerLedgerEntry.Create(saleEntity.ShopId, saleEntity.CustomerId.Value, saleEntity.Id, CustomerLedgerEntryType.SaleDue, saleEntity.DueAmount, DateOnly.FromDateTime(saleEntity.SoldAt.UtcDateTime), $"Due recorded from sale {saleEntity.InvoiceNumber}", recordedBy);
            if (ledger.IsError) return new OfflineSalePostingOutcome.Rejected(ledger.FirstError);
            dueEntry = ledger.Value;
        }

        for (var i = 0; i < vLines.Count; i++)
        {
            var consumed = consumption[i].ConsumedQuantity;
            if (consumed <= 0) continue;
            if (vLines[i].LineType == SaleLineType.Service) continue;
            var batchResult = vLines[i].Batch!.SubtractQuantity(consumed, recordedBy);
            if (batchResult.IsError) throw new InvalidOperationException(batchResult.FirstError.Description);
            var inventoryResult = vLines[i].Inventory!.SubtractQuantity(consumed, recordedBy);
            if (inventoryResult.IsError) throw new InvalidOperationException(inventoryResult.FirstError.Description);
            var tx = StockTransaction.Create(shopId, vLines[i].Item!.Id, vLines[i].Batch!.Id, StockTransactionType.Out, -consumed, invoiceNumber, null, sale.SoldAt, request.AttributedUserId, recordedBy);
            if (tx.IsError) throw new InvalidOperationException(tx.FirstError.Description);
            await stockTransactionRepository.AddAsync(tx.Value, cancellationToken);
        }

        await saleRepository.AddAsync(saleEntity, cancellationToken);

        foreach (var issue in pendingIssues)
            issue.AttachEvidence(invoiceNumber, request.OfflineSubmissionId);
        await varianceAnalyzer.PersistReviewIssuesAsync(saleEntity.Id, pendingIssues, cancellationToken);

        if (dueEntry is not null)
            await customerLedgerEntryRepository.AddAsync(dueEntry, cancellationToken);

        return new OfflineSalePostingOutcome.Staged(saleEntity, warnings);
    }

    private static bool AmountsMatch(decimal paid, decimal due, decimal total) =>
        decimal.Round(paid + due, 2, MidpointRounding.AwayFromZero) == decimal.Round(total, 2, MidpointRounding.AwayFromZero);
}
