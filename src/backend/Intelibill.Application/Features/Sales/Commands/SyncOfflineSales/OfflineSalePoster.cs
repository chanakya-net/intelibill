using System.Globalization;
using Intelibill.Application.Features.Expiry;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed partial class OfflineSalePoster(
    ISaleLineValidator saleLineValidator,
    ICustomerResolver customerResolver,
    ISaleRepository saleRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IStockTransactionRepository stockTransactionRepository,
    IShopRepository shopRepository,
    IOfflineSaleVarianceAnalyzer varianceAnalyzer,
    IBusinessClock businessClock,
    IPostingPeriodPolicy postingPeriodPolicy,
    ITillPostingService tillPosting,
    ITillSessionPolicy tillPolicy,
    ITillRepository tills,
    IBankAccountRepository banks,
    IExpiryPolicyFence expiryFence,
    IOfflineExpiryAuthority expiryAuthority) : IOfflineSalePoster
{
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

        // Read on the day the invoice was issued (the claimed time), never the day it synced.
        var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
        if (shop is null) return new OfflineSalePostingOutcome.Rejected(Errors.Shop.ShopNotFound);
        var businessDay = businessClock.GetBusinessDay(sale.SoldAt, shop.BusinessTimeZoneId);

        // The first fenced statement: the claimed business date's month must be open. A closed month
        // holds the invoice for review (it was issued offline and cannot simply be refused); a busy
        // fence is retryable, so the device resends.
        var period = await postingPeriodPolicy.AuthorizeForWriteAsync(shopId, businessDay.Date, cancellationToken);
        if (period.IsError)
        {
            var periodError = period.FirstError;
            if (periodError.Code == Errors.PostingPeriod.ClosedCode)
                return new OfflineSalePostingOutcome.Held(ReconciliationIssueType.ClosedPeriodReview, periodError, periodError.Description, []);

            return new OfflineSalePostingOutcome.Rejected(periodError);
        }

        var expiryLock = await expiryFence.AcquireAsync(shopId, false, cancellationToken);
        if (expiryLock.IsError) return new OfflineSalePostingOutcome.Rejected(expiryLock.FirstError);
        var expiry = await expiryAuthority.ValidateFrozenAsync(shopId, sale, request.Now, cancellationToken);
        if (expiry.IsError)
            return expiry.FirstError.Code == OfflineExpiryAuthority.ReviewRequired.Code
                ? new OfflineSalePostingOutcome.Held(ReconciliationIssueType.ExpiryReview, expiry.FirstError, expiry.FirstError.Description, [])
                : new OfflineSalePostingOutcome.Rejected(expiry.FirstError);

        var till = await PrepareTillAsync(request, businessDay.Date, cancellationToken);
        if (till.IsError)
            return till.FirstError.Code == Errors.Till.Busy.Code
                ? new OfflineSalePostingOutcome.Rejected(till.FirstError)
                : new OfflineSalePostingOutcome.Held(ReconciliationIssueType.TillReview, till.FirstError, till.FirstError.Description, []);

        var invoiceNumber = (sale.InvoiceNumber ?? string.Empty).Trim();

        var warnings = new List<string>();
        var pendingIssues = new List<ReconciliationIssue>();
        var customerRes = await customerResolver.ResolveAsync(shopId, sale.CustomerId, sale.CustomerPhone, sale.DueAmount > 0, sale.PaymentMethod, cancellationToken);
        if (customerRes.IsError) return new OfflineSalePostingOutcome.Rejected(customerRes.FirstError);

        var resolvedCust = customerRes.Value;
        OfflineSaleSyncHelpers.AddCustomerVarianceWarnings(shopId, recordedBy, clientSaleId, deviceId, sale, resolvedCust, warnings, pendingIssues);

        // The bill was already issued offline, so it is never refused for credit here: a due the
        // customer's policy would refuse online posts with a review case instead. Offline devices
        // cannot see other devices' due, so the limit is not a guarantee across devices.
        if (sale.DueAmount > 0m && resolvedCust is { IsActive: true } && !request.CreditReviewed)
        {
            var exposure = await customerLedgerEntryRepository.GetCustomerBalanceAsync(shopId, resolvedCust.Id, cancellationToken);
            var credit = CustomerCreditPolicy.Evaluate(CustomerCreditState.Of(resolvedCust), exposure, sale.DueAmount, null, null);
            if (credit.IsError)
                OfflineSaleSyncHelpers.AddCreditPolicyViolation(shopId, recordedBy, clientSaleId, deviceId, resolvedCust, exposure, sale.DueAmount, credit.FirstError, warnings, pendingIssues);
        }

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
        if (OfflineSaleSyncHelpers.CheckArithmetic(sale) is { } arithmeticHold)
            return arithmeticHold;

        var warnCountBefore = warnings.Count;
        var validation = await saleLineValidator.ValidateLinesAsync(shopId, lineCmds, warnings, cancellationToken, true);
        if (validation.IsError) return new OfflineSalePostingOutcome.Rejected(validation.FirstError);

        var vLines = validation.Value.Lines;
        OfflineSaleSyncHelpers.AddValidationVarianceIssues(shopId, recordedBy, clientSaleId, deviceId, warnings.Skip(warnCountBefore), pendingIssues);
        OfflineSaleSyncHelpers.AddEconomicVarianceWarnings(shopId, recordedBy, clientSaleId, deviceId, vLines, lineByKey, warnings, pendingIssues);
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
        var saleEntity = Sale.Create(shopId, request.AttributedUserId, OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, clientSaleId), request.PayloadHash, invoiceNumber, resolvedCust?.Id ?? sale.CustomerId, sale.CustomerName ?? resolvedCust?.Name, sale.CustomerPhone ?? resolvedCust?.PhoneNumber, sale.PaymentMethod, sale.SoldAt, businessDay, sale.PaidAmount, sale.DueAmount, sale.TotalAmount, sale.TotalTaxAmount, saleItems, sale.SubtotalBeforeDiscount, sale.TotalBeforeDiscount, sale.TotalDiscountAmount, sale.ConfiguredSaleRuleId, sale.ConfiguredSaleRuleType, sale.ConfiguredSaleRulePercentage, sale.ConfiguredSaleRuleThresholdAmount, sale.SaleDiscountOverrideType, sale.SaleDiscountOverrideValue, SaleSource.Offline, clientSaleId, deviceId, request.Now, warnings);

        saleEntity.AttachExpiryDecisions(expiry.Value);
        saleEntity.ApplyCreditTerms(resolvedCust?.CreditTermsDays);
        AttachDocument(request, shop, saleEntity, resolvedCust, vLines, pendingIssues);

        // Built before anything is staged so a refused entry leaves the unit of work untouched.
        CustomerLedgerEntry? dueEntry = null;
        if (saleEntity.DueAmount > 0 && saleEntity.CustomerId.HasValue)
        {
            var ledger = CustomerLedgerEntry.Create(saleEntity.ShopId, saleEntity.CustomerId.Value, saleEntity.Id, CustomerLedgerEntryType.SaleDue, saleEntity.DueAmount, saleEntity.BusinessDate, $"Due recorded from sale {saleEntity.InvoiceNumber}", recordedBy);
            if (ledger.IsError) return new OfflineSalePostingOutcome.Rejected(ledger.FirstError);
            dueEntry = ledger.Value;
        }

        var settlement = await StageTillAsync(request, saleEntity, till.Value, cancellationToken);
        if (settlement.IsError) return new OfflineSalePostingOutcome.Rejected(settlement.FirstError);

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

    /// <summary>
    /// Issues the sale's document under the profile its lease froze (the shop's current one for
    /// ranges reserved before profiles were frozen). The device has already handed the invoice over,
    /// so a profile that cannot issue it does not stop the sale: it posts without a document and a
    /// review case records why.
    /// </summary>
    private static void AttachDocument(
        OfflineSalePostingRequest request,
        Shop shop,
        Sale saleEntity,
        Customer? customer,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        List<ReconciliationIssue> pendingIssues)
    {
        var lease = request.Lease;
        ErrorOr<InvoiceSellerProfile> profile;
        if (lease?.SellerProfile is { } frozen)
        {
            profile = frozen;
        }
        else
        {
            profile = InvoiceDocumentBuilder.CaptureSellerProfile(shop).Then(current => current with { SeriesCode = string.Empty });
        }

        var fiscalYear = lease is null ? FiscalYear.ForBusinessDate(saleEntity.BusinessDate) : new FiscalYear(lease.FiscalYearStart);
        var document = profile.Then(p => InvoiceDocumentBuilder.Build(
            new InvoiceIssueContext(p, customer, PlaceOfSupplyStateCode: null, saleEntity.SoldAt, fiscalYear, ValidatedSaleLines.DocumentUnitsOf(validatedLines), saleEntity.BusinessDate),
            saleEntity));

        if (!document.IsError)
        {
            saleEntity.AttachInvoiceDocument(document.Value);
            return;
        }

        pendingIssues.Add(ReconciliationIssue.Create(
            request.ShopId,
            null,
            request.ClientSaleId,
            request.DeviceId,
            ReconciliationIssueType.DocumentReview,
            document.FirstError.Code,
            $"Posted without a GST document: {document.FirstError.Description}",
            request.RecordedByUserId));
    }

    private static bool AmountsMatch(decimal paid, decimal due, decimal total) =>
        decimal.Round(paid + due, 2, MidpointRounding.AwayFromZero) == decimal.Round(total, 2, MidpointRounding.AwayFromZero);
}
