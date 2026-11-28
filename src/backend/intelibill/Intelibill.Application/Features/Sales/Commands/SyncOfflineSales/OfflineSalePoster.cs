using Intelibill.Application.Common.Licensing;
using System.Globalization;
using Intelibill.Application.Features.Expiry;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

[LicenceAction(LicenceAction.Recovery)]
internal sealed class OfflineSalePoster(
    ISaleLineValidator saleLineValidator,
    OfflineSaleCustomerReviewer customerReviewer,
    OfflineSaleStockAndDuePoster stockAndDuePoster,
    IShopRepository shopRepository,
    IBusinessClock businessClock,
    IPostingPeriodPolicy postingPeriodPolicy,
    OfflineSaleTillSettler tillSettler,
    IExpiryPolicyFence expiryFence,
    IOfflineExpiryAuthority expiryAuthority,
    IShopLicenceFence licenceFence,
    IOfflineLicenceHistoryAuthority licenceHistory) : IOfflineSalePoster
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
        if (sale.DueAmount > 0 && !sale.CustomerId.HasValue && string.IsNullOrWhiteSpace(sale.CustomerPhone))
            return Errors.Sale.CustomerIdentityRequiredForDue;
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
        var recordedBy = request.RecordedByUserId;

        // Read on the day the invoice was issued (the claimed time), never the day it synced.
        var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
        if (shop is null) return new OfflineSalePostingOutcome.Rejected(Errors.Shop.ShopNotFound);
        var businessDay = businessClock.GetBusinessDay(sale.SoldAt, shop.BusinessTimeZoneId);

        var licence = await EnterLicenceAsync(request, cancellationToken);
        if (licence.IsError) return new OfflineSalePostingOutcome.Rejected(licence.FirstError);
        var licenceLease = licence.Value;
        var initialDecision = licenceLease.CurrentDecision();
        if (await licenceHistory.EvaluateAsync(licenceLease, request, cancellationToken) is { } held) return held;

        // After the licence fence: the claimed business date's month must be open. A closed month
        // holds the invoice for review (it was issued offline and cannot simply be refused); a busy
        // fence is retryable, so the device resends.
        var period = await postingPeriodPolicy.AuthorizeForWriteAsync(shopId, businessDay.Date, cancellationToken);
        if (period.IsError) return ClassifyPeriodFailure(period.FirstError);

        var expiryLock = await expiryFence.AcquireAsync(shopId, false, cancellationToken);
        if (expiryLock.IsError) return new OfflineSalePostingOutcome.Rejected(expiryLock.FirstError);
        var expiry = await expiryAuthority.ValidateFrozenAsync(shopId, sale, request.Now, cancellationToken);
        if (expiry.IsError) return ClassifyExpiryFailure(expiry.FirstError);

        var till = await tillSettler.LockAndPrepareTillAsync(request, businessDay.Date, cancellationToken);
        if (till.IsError) return ClassifyTillFailure(till.FirstError);

        var invoiceNumber = (sale.InvoiceNumber ?? string.Empty).Trim();

        var warnings = new List<string>();
        var pendingIssues = new List<ReconciliationIssue>();
        var customerRes = await customerReviewer.ResolveCustomerAsync(request, warnings, pendingIssues, cancellationToken);
        if (customerRes.IsError) return new OfflineSalePostingOutcome.Rejected(customerRes.FirstError);
        var resolvedCust = customerRes.Value;

        // The bill was already issued offline, so it is never refused for credit here: a due the
        // customer's policy would refuse online posts with a review case instead. Offline devices
        // cannot see other devices' due, so the limit is not a guarantee across devices.
        if (sale.DueAmount > 0m && resolvedCust is { IsActive: true } && !request.CreditReviewed)
            await customerReviewer.ReviewCustomerCreditAsync(request, resolvedCust, warnings, pendingIssues, cancellationToken);

        var (lineCmds, lineByKey) = BuildLineCommands(sale.Items);

        // Frozen amounts must reconcile with their own inputs before anything is posted; catalog variance is reported separately below.
        if (OfflineSaleSyncHelpers.CheckArithmetic(sale) is { } arithmeticHold)
            return arithmeticHold;

        var validation = await ValidateLinesAsync(request, lineCmds, lineByKey, warnings, pendingIssues, cancellationToken);
        if (validation.IsError) return new OfflineSalePostingOutcome.Rejected(validation.FirstError);
        var vLines = validation.Value.Lines;

        var saleItems = BuildSaleItems(shopId, vLines, lineByKey);
        if (saleItems.IsError) return new OfflineSalePostingOutcome.Rejected(saleItems.FirstError);

        var consumption = PlanStockConsumption(request, vLines, warnings, pendingIssues);
        var saleEntity = CreateSale(request, invoiceNumber, businessDay, resolvedCust, saleItems.Value, warnings);

        saleEntity.AttachExpiryDecisions(expiry.Value);
        saleEntity.ApplyCreditTerms(resolvedCust?.CreditTermsDays);
        AttachDocument(request, shop, saleEntity, resolvedCust, vLines, pendingIssues);

        // Built before anything is staged so a refused entry leaves the unit of work untouched.
        CustomerLedgerEntry? dueEntry = null;
        if (saleEntity.DueAmount > 0 && saleEntity.CustomerId.HasValue)
        {
            var ledger = CreateDueEntry(saleEntity, saleEntity.CustomerId.Value, recordedBy);
            if (ledger.IsError) return new OfflineSalePostingOutcome.Rejected(ledger.FirstError);
            dueEntry = ledger.Value;
        }

        var settlement = await tillSettler.StageTillAsync(request, saleEntity, till.Value, cancellationToken);
        if (settlement.IsError) return new OfflineSalePostingOutcome.Rejected(settlement.FirstError);

        await stockAndDuePoster.StageSaleStockAndDueAsync(
            request, vLines, consumption, invoiceNumber, saleEntity, pendingIssues, dueEntry, cancellationToken);

        if (licenceLease.CurrentDecision() != initialDecision &&
            await licenceHistory.EvaluateAsync(licenceLease, request, cancellationToken) is { } changed) return changed;
        return new OfflineSalePostingOutcome.Staged(saleEntity, warnings);
    }

    private async Task<ErrorOr<ShopLicenceLease>> EnterLicenceAsync(
        OfflineSalePostingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExistingLicence is { } existingLicence)
            return existingLicence;

        return await licenceFence.EnterAsync(request.ShopId, LicenceAction.Recovery, cancellationToken);
    }

    private static OfflineSalePostingOutcome ClassifyPeriodFailure(Error periodError)
    {
        if (periodError.Code == Errors.PostingPeriod.ClosedCode)
        {
            return new OfflineSalePostingOutcome.Held(
                ReconciliationIssueType.ClosedPeriodReview,
                periodError,
                periodError.Description,
                []);
        }

        return new OfflineSalePostingOutcome.Rejected(periodError);
    }

    private static OfflineSalePostingOutcome ClassifyExpiryFailure(Error expiryError) =>
        expiryError.Code == OfflineExpiryAuthority.ReviewRequired.Code
            ? new OfflineSalePostingOutcome.Held(ReconciliationIssueType.ExpiryReview, expiryError, expiryError.Description, [])
            : new OfflineSalePostingOutcome.Rejected(expiryError);

    private static OfflineSalePostingOutcome ClassifyTillFailure(Error tillError) =>
        tillError.Code == Errors.Till.Busy.Code
            ? new OfflineSalePostingOutcome.Rejected(tillError)
            : new OfflineSalePostingOutcome.Held(ReconciliationIssueType.TillReview, tillError, tillError.Description, []);

    private static (List<RecordSaleItemCommand> LineCmds, Dictionary<string, OfflineSaleSyncLineCommand> LineByKey)
        BuildLineCommands(IReadOnlyList<OfflineSaleSyncLineCommand> items)
    {
        var lineCmds = new List<RecordSaleItemCommand>(items.Count);
        var lineByKey = new Dictionary<string, OfflineSaleSyncLineCommand>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var line = items[i];
            var key = i.ToString(CultureInfo.InvariantCulture);
            lineByKey.Add(key, line);
            var lineType = line.IsServiceLine ? SaleLineType.Service : SaleLineType.Goods;
            lineCmds.Add(new RecordSaleItemCommand(
                line.Barcode,
                line.BatchNumber,
                line.ItemName,
                line.Quantity,
                line.CostPrice,
                line.SalesPrice,
                line.Mrp,
                line.TaxRatePercent,
                line.IsPriceIncludingTax,
                line.InventoryBatchId,
                null,
                key,
                line.HsnCode,
                lineType,
                line.ServiceId));
        }

        return (lineCmds, lineByKey);
    }

    /// <summary>Validates the lines against the catalog and records every variance they show.</summary>
    private async Task<ErrorOr<SaleLineValidationResult>> ValidateLinesAsync(
        OfflineSalePostingRequest request,
        List<RecordSaleItemCommand> lineCmds,
        Dictionary<string, OfflineSaleSyncLineCommand> lineByKey,
        List<string> warnings,
        List<ReconciliationIssue> pendingIssues,
        CancellationToken cancellationToken)
    {
        var warnCountBefore = warnings.Count;
        var validation = await saleLineValidator.ValidateLinesAsync(request.ShopId, lineCmds, warnings, cancellationToken, true);
        if (validation.IsError) return validation;

        AddLineVarianceIssues(request, validation.Value.Lines, lineByKey, warnCountBefore, warnings, pendingIssues);
        return validation;
    }

    private static void AddLineVarianceIssues(
        OfflineSalePostingRequest request,
        IReadOnlyList<ValidatedSaleLine> vLines,
        Dictionary<string, OfflineSaleSyncLineCommand> lineByKey,
        int warnCountBefore,
        List<string> warnings,
        List<ReconciliationIssue> pendingIssues)
    {
        var shopId = request.ShopId;
        var recordedBy = request.RecordedByUserId;
        var clientSaleId = request.ClientSaleId;
        var deviceId = request.DeviceId;
        OfflineSaleSyncHelpers.AddValidationVarianceIssues(
            shopId,
            recordedBy,
            clientSaleId,
            deviceId,
            warnings.Skip(warnCountBefore),
            pendingIssues);
        OfflineSaleSyncHelpers.AddEconomicVarianceWarnings(
            shopId,
            recordedBy,
            clientSaleId,
            deviceId,
            vLines,
            lineByKey,
            warnings,
            pendingIssues);
        OfflineSaleSyncHelpers.AddPricingVarianceWarnings(
            shopId,
            recordedBy,
            clientSaleId,
            deviceId,
            vLines,
            lineByKey,
            warnings,
            pendingIssues);
        OfflineSaleSyncHelpers.AddDiscountVarianceWarnings(
            shopId,
            recordedBy,
            clientSaleId,
            deviceId,
            request.Sale,
            lineByKey.Values,
            request.ActiveDiscountRulesById,
            warnings,
            pendingIssues);
    }

    private static ErrorOr<List<SaleItem>> BuildSaleItems(
        Guid shopId,
        IReadOnlyList<ValidatedSaleLine> vLines,
        Dictionary<string, OfflineSaleSyncLineCommand> lineByKey)
    {
        var saleItems = new List<SaleItem>(vLines.Count);
        foreach (var v in vLines)
        {
            if (!lineByKey.TryGetValue(v.Command.ClientLineKey!, out var line)) return Errors.General.Unexpected("Line mapping failed.");
            if (v.LineType == SaleLineType.Service)
            {
                saleItems.Add(CreateServiceItem(shopId, v, line));
            }
            else
            {
                saleItems.Add(CreateGoodsItem(shopId, v, line));
            }
        }

        return saleItems;
    }

    private static SaleItem CreateServiceItem(Guid shopId, ValidatedSaleLine v, OfflineSaleSyncLineCommand line) =>
        SaleItem.CreateService(
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
            hsnCode: line.HsnCode);

    private static SaleItem CreateGoodsItem(Guid shopId, ValidatedSaleLine v, OfflineSaleSyncLineCommand line) =>
        SaleItem.CreateGoods(
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
            hsnCode: line.HsnCode);

    private static Sale CreateSale(
        OfflineSalePostingRequest request,
        string invoiceNumber,
        BusinessDay businessDay,
        Customer? resolvedCust,
        List<SaleItem> saleItems,
        List<string> warnings)
    {
        var sale = request.Sale;
        var deviceId = request.DeviceId;
        var clientSaleId = request.ClientSaleId;
        return Sale.Create(
            request.ShopId,
            request.AttributedUserId,
            OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, clientSaleId),
            request.PayloadHash,
            invoiceNumber,
            resolvedCust?.Id ?? sale.CustomerId,
            sale.CustomerName ?? resolvedCust?.Name,
            sale.CustomerPhone ?? resolvedCust?.PhoneNumber,
            sale.PaymentMethod,
            sale.SoldAt,
            businessDay,
            sale.PaidAmount,
            sale.DueAmount,
            sale.TotalAmount,
            sale.TotalTaxAmount,
            saleItems,
            sale.SubtotalBeforeDiscount,
            sale.TotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.ConfiguredSaleRuleId,
            sale.ConfiguredSaleRuleType,
            sale.ConfiguredSaleRulePercentage,
            sale.ConfiguredSaleRuleThresholdAmount,
            sale.SaleDiscountOverrideType,
            sale.SaleDiscountOverrideValue,
            SaleSource.Offline,
            clientSaleId,
            deviceId,
            request.Now,
            warnings);
    }

    private static List<OfflineStockConsumption> PlanStockConsumption(
        OfflineSalePostingRequest request,
        IReadOnlyList<ValidatedSaleLine> vLines,
        List<string> warnings,
        List<ReconciliationIssue> pendingIssues) =>
        OfflineSaleSyncHelpers.BuildStockConsumptionPlan(
            request.ShopId,
            request.RecordedByUserId,
            request.ClientSaleId,
            request.DeviceId,
            vLines,
            warnings,
            pendingIssues);

    private static ErrorOr<CustomerLedgerEntry> CreateDueEntry(Sale saleEntity, Guid customerId, Guid recordedBy) =>
        CustomerLedgerEntry.Create(
            saleEntity.ShopId,
            customerId,
            saleEntity.Id,
            CustomerLedgerEntryType.SaleDue,
            saleEntity.DueAmount,
            saleEntity.BusinessDate,
            $"Due recorded from sale {saleEntity.InvoiceNumber}",
            recordedBy);

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
            new InvoiceIssueContext(
                p,
                customer,
                PlaceOfSupplyStateCode: null,
                saleEntity.SoldAt,
                fiscalYear,
                ValidatedSaleLines.DocumentUnitsOf(validatedLines),
                saleEntity.BusinessDate),
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
