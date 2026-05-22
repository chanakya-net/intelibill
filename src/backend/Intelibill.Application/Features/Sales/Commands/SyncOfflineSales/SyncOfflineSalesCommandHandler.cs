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
    IReconciliationIssueRepository reconciliationIssueRepository,
    IDiscountRuleRepository discountRuleRepository,
    IUnitOfWork unitOfWork)
{
    private const string StatusCreated = "created";
    private const string StatusDuplicate = "duplicate";
    private const string StatusFailed = "failed";
    private const string StatusSyncedWithWarnings = "SyncedWithWarnings";
    private const string StatusNeedsReview = "NeedsReview";

    public async Task<ErrorOr<OfflineSalesSyncResponseDto>> HandleAsync(
        SyncOfflineSalesCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var isMember = actor.ShopMemberships.Any(sm => sm.ShopId == command.ShopId);
        if (!isMember)
            return Errors.Shop.MembershipNotFound;

        var deviceId = command.DeviceId.Trim();
        var now = DateTimeOffset.UtcNow;
        var activeLeases = await invoiceLeaseRepository.GetActiveByDeviceAsync(
            command.ShopId,
            deviceId,
            now,
            cancellationToken);
        var activeDiscountRules = await discountRuleRepository.GetActiveByShopAsync(
            command.ShopId,
            now,
            cancellationToken);
        var activeDiscountRulesById = activeDiscountRules.ToDictionary(rule => rule.Id);

        var results = new List<OfflineSaleSyncResultDto>(command.Sales.Count);

        foreach (var sale in command.Sales)
        {
            var normalizedClientSaleId = string.IsNullOrWhiteSpace(sale.ClientSaleId)
                ? string.Empty
                : sale.ClientSaleId.Trim();

            if (string.IsNullOrWhiteSpace(normalizedClientSaleId))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ClientSaleIdRequired));
                continue;
            }

            if (normalizedClientSaleId.Length > 120)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ClientSaleIdTooLong));
                continue;
            }

            var requestHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, deviceId, sale);

            var existingSale = await saleRepository.GetByClientSaleIdAsync(
                command.ShopId,
                deviceId,
                normalizedClientSaleId,
                cancellationToken);

            if (existingSale is not null)
            {
                if (!string.Equals(existingSale.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    await AddReviewIssueAsync(
                        command.ShopId,
                        existingSale.Id,
                        normalizedClientSaleId,
                        deviceId,
                        ReconciliationIssueType.ValidationConflict,
                        Errors.Sale.IdempotencyConflict.Code,
                        Errors.Sale.IdempotencyConflict.Description,
                        command.ActorUserId,
                        cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    results.Add(BuildNeedsReviewResult(normalizedClientSaleId, Errors.Sale.IdempotencyConflict));
                    continue;
                }

                results.Add(new OfflineSaleSyncResultDto(
                    normalizedClientSaleId,
                    StatusDuplicate,
                    existingSale.Id,
                    existingSale.InvoiceNumber,
                    []));
                continue;
            }

            var invoiceNumber = string.IsNullOrWhiteSpace(sale.InvoiceNumber)
                ? string.Empty
                : sale.InvoiceNumber.Trim();

            if (string.IsNullOrWhiteSpace(invoiceNumber))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberRequired));
                continue;
            }

            if (invoiceNumber.Length > 40)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberTooLong));
                continue;
            }

            if (sale.Items.Count == 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ItemsRequired));
                continue;
            }

            if (sale.PaidAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.PaidAmountInvalid));
                continue;
            }

            if (sale.DueAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.DueAmountInvalid));
                continue;
            }

            if (sale.PaymentMethod == PaymentMethod.Credit && sale.DueAmount <= 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.CreditRequiresDueAmount));
                continue;
            }

            if (sale.DueAmount > 0 && !sale.CustomerId.HasValue && string.IsNullOrWhiteSpace(sale.CustomerPhone))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.CustomerIdentityRequiredForDue));
                continue;
            }

            if (sale.TotalAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalAmountInvalid));
                continue;
            }

            if (sale.TotalTaxAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalTaxAmountInvalid));
                continue;
            }

            if (sale.SubtotalBeforeDiscount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.SubtotalBeforeDiscountInvalid));
                continue;
            }

            if (sale.TotalBeforeDiscount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalBeforeDiscountInvalid));
                continue;
            }

            if (sale.TotalDiscountAmount < 0)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.TotalDiscountAmountInvalid));
                continue;
            }

            if (!AmountsMatch(sale.PaidAmount, sale.DueAmount, sale.TotalAmount))
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.PaidAndDueAmountMismatch));
                continue;
            }

            var warnings = new List<string>();
            var pendingIssues = new List<ReconciliationIssue>();

            var customerResolution = await customerResolver.ResolveAsync(
                command.ShopId,
                sale.CustomerId,
                sale.CustomerPhone,
                hasDueAmount: sale.DueAmount > 0,
                sale.PaymentMethod,
                cancellationToken);

            if (customerResolution.IsError)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, customerResolution.FirstError));
                continue;
            }

            var resolvedCustomer = customerResolution.Value;
            AddCustomerVarianceWarnings(
                command.ShopId,
                command.ActorUserId,
                normalizedClientSaleId,
                deviceId,
                sale,
                resolvedCustomer,
                warnings,
                pendingIssues);

            if (!TryMatchLease(activeLeases, invoiceNumber, out _))
            {
                await AddReviewIssueAsync(
                    command.ShopId,
                    null,
                    normalizedClientSaleId,
                    deviceId,
                    ReconciliationIssueType.InvoiceConflict,
                    Errors.Sale.InvoiceLeaseNotFound.Code,
                    Errors.Sale.InvoiceLeaseNotFound.Description,
                    command.ActorUserId,
                    cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                results.Add(BuildNeedsReviewResult(normalizedClientSaleId, Errors.Sale.InvoiceLeaseNotFound));
                continue;
            }

            var existingInvoiceSale = await saleRepository.GetByInvoiceNumberAsync(
                command.ShopId,
                invoiceNumber,
                cancellationToken);

            if (existingInvoiceSale is not null)
            {
                await AddReviewIssueAsync(
                    command.ShopId,
                    null,
                    normalizedClientSaleId,
                    deviceId,
                    ReconciliationIssueType.InvoiceConflict,
                    Errors.Sale.InvoiceNumberAlreadyUsed.Code,
                    Errors.Sale.InvoiceNumberAlreadyUsed.Description,
                    command.ActorUserId,
                    cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                results.Add(BuildNeedsReviewResult(normalizedClientSaleId, Errors.Sale.InvoiceNumberAlreadyUsed));
                continue;
            }

            var lineCommands = new List<RecordSaleItemCommand>(sale.Items.Count);
            var lineByKey = new Dictionary<string, OfflineSaleSyncLineCommand>(sale.Items.Count);

            for (var i = 0; i < sale.Items.Count; i++)
            {
                var line = sale.Items[i];

                if (string.IsNullOrWhiteSpace(line.Barcode))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.BarcodeRequired));
                    goto NextSale;
                }

                if (string.IsNullOrWhiteSpace(line.BatchNumber))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.BatchNumberRequired));
                    goto NextSale;
                }

                if (string.IsNullOrWhiteSpace(line.ItemName))
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.ItemNameRequired));
                    goto NextSale;
                }

                if (line.InventoryBatchId == Guid.Empty)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.InventoryBatchIdRequired));
                    goto NextSale;
                }

                if (line.Quantity <= 0)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.OfflineLineQuantityMustBePositive));
                    goto NextSale;
                }

                if (line.PreTaxAmountBeforeDiscount < 0
                    || line.ItemDiscountAmount < 0
                    || line.SaleDiscountAmount < 0
                    || line.TaxableAmount < 0
                    || line.TaxAmount < 0
                    || line.TotalAmount < 0)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, Errors.Sale.OfflineLineAmountsInvalid));
                    goto NextSale;
                }

                var lineKey = i.ToString(CultureInfo.InvariantCulture);
                lineByKey.Add(lineKey, line);
                lineCommands.Add(new RecordSaleItemCommand(
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
                    ItemDiscount: null,
                    ClientLineKey: lineKey,
                    HsnCode: line.HsnCode));
            }

            var validation = await saleLineValidator.ValidateLinesAsync(
                command.ShopId,
                lineCommands,
                warnings,
                cancellationToken,
                allowInsufficientStock: true);

            if (validation.IsError)
            {
                results.Add(BuildErrorResult(normalizedClientSaleId, validation.FirstError));
                continue;
            }

            var validatedLines = validation.Value.Lines;
            var saleItems = new List<SaleItem>(validatedLines.Count);
            AddPricingVarianceWarnings(
                command.ShopId,
                command.ActorUserId,
                normalizedClientSaleId,
                deviceId,
                validatedLines,
                lineByKey,
                warnings,
                pendingIssues);
            AddDiscountVarianceWarnings(
                command.ShopId,
                command.ActorUserId,
                normalizedClientSaleId,
                deviceId,
                sale,
                lineByKey.Values,
                activeDiscountRulesById,
                warnings,
                pendingIssues);

            foreach (var validated in validatedLines)
            {
                if (string.IsNullOrWhiteSpace(validated.Command.ClientLineKey)
                    || !lineByKey.TryGetValue(validated.Command.ClientLineKey, out var line))
                {
                    results.Add(BuildErrorResult(
                        normalizedClientSaleId,
                        Errors.General.Unexpected("Offline sale line mapping failed.")));
                    goto NextSale;
                }

                var saleItem = SaleItem.Create(
                    command.ShopId,
                    validated.Item.Id,
                    validated.Batch.Id,
                    line.Quantity,
                    line.CostPrice,
                    line.SalesPrice,
                    line.Mrp,
                    line.TaxRatePercent,
                    line.IsPriceIncludingTax,
                    validated.HasPriceMismatch,
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
                saleItems.Add(saleItem);
            }

            var stockConsumptions = BuildStockConsumptionPlan(
                command.ShopId,
                command.ActorUserId,
                normalizedClientSaleId,
                deviceId,
                validatedLines,
                warnings,
                pendingIssues);

            var offlineIdempotencyKey = OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, normalizedClientSaleId);
            var saleEntity = Sale.Create(
                command.ShopId,
                command.ActorUserId,
                offlineIdempotencyKey,
                requestHash,
                invoiceNumber,
                resolvedCustomer?.Id ?? sale.CustomerId,
                sale.CustomerName ?? resolvedCustomer?.Name,
                sale.CustomerPhone ?? resolvedCustomer?.PhoneNumber,
                sale.PaymentMethod,
                sale.SoldAt,
                sale.PaidAmount,
                sale.DueAmount,
                sale.TotalAmount,
                sale.TotalTaxAmount,
                saleItems,
                subtotalBeforeDiscount: sale.SubtotalBeforeDiscount,
                totalBeforeDiscount: sale.TotalBeforeDiscount,
                totalDiscountAmount: sale.TotalDiscountAmount,
                configuredSaleRuleId: sale.ConfiguredSaleRuleId,
                configuredSaleRuleType: sale.ConfiguredSaleRuleType,
                configuredSaleRulePercentage: sale.ConfiguredSaleRulePercentage,
                configuredSaleRuleThresholdAmount: sale.ConfiguredSaleRuleThresholdAmount,
                saleDiscountOverrideType: sale.SaleDiscountOverrideType,
                saleDiscountOverrideValue: sale.SaleDiscountOverrideValue,
                source: SaleSource.Offline,
                clientSaleId: normalizedClientSaleId,
                deviceId: deviceId,
                syncedAt: now,
                warnings: warnings);

            for (var i = 0; i < validatedLines.Count; i++)
            {
                var validated = validatedLines[i];
                var consumedQuantity = stockConsumptions[i].ConsumedQuantity;

                if (consumedQuantity <= 0)
                    continue;

                var batchResult = validated.Batch.SubtractQuantity(consumedQuantity, command.ActorUserId);
                if (batchResult.IsError)
                    throw new InvalidOperationException(batchResult.FirstError.Description);

                var inventoryResult = validated.Inventory.SubtractQuantity(consumedQuantity, command.ActorUserId);
                if (inventoryResult.IsError)
                    throw new InvalidOperationException(inventoryResult.FirstError.Description);

                var txResult = StockTransaction.Create(
                    command.ShopId,
                    validated.Item.Id,
                    validated.Batch.Id,
                    StockTransactionType.Out,
                    -consumedQuantity,
                    invoiceNumber,
                    null,
                    sale.SoldAt,
                    command.ActorUserId,
                    command.ActorUserId);

                if (txResult.IsError)
                    throw new InvalidOperationException(txResult.FirstError.Description);

                await stockTransactionRepository.AddAsync(txResult.Value, cancellationToken);
            }

            await saleRepository.AddAsync(saleEntity, cancellationToken);

            foreach (var issue in pendingIssues)
            {
                issue.LinkSale(saleEntity.Id);
                await reconciliationIssueRepository.AddAsync(issue, cancellationToken);
            }

            if (saleEntity.DueAmount > 0 && saleEntity.CustomerId.HasValue)
            {
                var ledgerResult = CustomerLedgerEntry.Create(
                    saleEntity.ShopId,
                    saleEntity.CustomerId.Value,
                    saleEntity.Id,
                    CustomerLedgerEntryType.SaleDue,
                    saleEntity.DueAmount,
                    DateOnly.FromDateTime(saleEntity.SoldAt.UtcDateTime),
                    $"Due recorded from sale {saleEntity.InvoiceNumber}",
                    command.ActorUserId);

                if (ledgerResult.IsError)
                {
                    results.Add(BuildErrorResult(normalizedClientSaleId, ledgerResult.FirstError));
                    continue;
                }

                await customerLedgerEntryRepository.AddAsync(ledgerResult.Value, cancellationToken);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                unitOfWork.ClearChanges();

                var concurrentSale = await saleRepository.GetByClientSaleIdAsync(
                    command.ShopId,
                    deviceId,
                    normalizedClientSaleId,
                    cancellationToken);

                if (concurrentSale is null)
                {
                    var saveError = GetSaveFailureError(ex);
                    if (saveError.Code == Errors.Sale.InvoiceNumberAlreadyUsed.Code)
                    {
                        await AddReviewIssueAsync(
                            command.ShopId,
                            null,
                            normalizedClientSaleId,
                            deviceId,
                            ReconciliationIssueType.InvoiceConflict,
                            saveError.Code,
                            saveError.Description,
                            command.ActorUserId,
                            cancellationToken);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        results.Add(BuildNeedsReviewResult(normalizedClientSaleId, saveError));
                        continue;
                    }

                    results.Add(BuildErrorResult(normalizedClientSaleId, saveError));
                    continue;
                }

                if (!string.Equals(concurrentSale.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    await AddReviewIssueAsync(
                        command.ShopId,
                        concurrentSale.Id,
                        normalizedClientSaleId,
                        deviceId,
                        ReconciliationIssueType.ValidationConflict,
                        Errors.Sale.IdempotencyConflict.Code,
                        Errors.Sale.IdempotencyConflict.Description,
                        command.ActorUserId,
                        cancellationToken);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    results.Add(BuildNeedsReviewResult(normalizedClientSaleId, Errors.Sale.IdempotencyConflict));
                    continue;
                }

                results.Add(new OfflineSaleSyncResultDto(
                    normalizedClientSaleId,
                    StatusDuplicate,
                    concurrentSale.Id,
                    concurrentSale.InvoiceNumber,
                    []));
                continue;
            }

            results.Add(new OfflineSaleSyncResultDto(
                normalizedClientSaleId,
                warnings.Count > 0 ? StatusSyncedWithWarnings : StatusCreated,
                saleEntity.Id,
                saleEntity.InvoiceNumber,
                [])
            {
                Warnings = warnings,
            });

        NextSale:
            continue;
        }

        return new OfflineSalesSyncResponseDto(results);
    }

    private static bool AmountsMatch(decimal paidAmount, decimal dueAmount, decimal totalAmount) =>
        decimal.Round(paidAmount + dueAmount, 2, MidpointRounding.AwayFromZero) ==
        decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);

    private static bool TryMatchLease(
        IReadOnlyList<InvoiceLease> leases,
        string invoiceNumber,
        out InvoiceLease? matched)
    {
        foreach (var lease in leases)
        {
            if (!invoiceNumber.StartsWith(lease.Prefix, StringComparison.Ordinal))
                continue;

            var numberPart = invoiceNumber[lease.Prefix.Length..];
            if (!int.TryParse(numberPart, out var number))
                continue;

            if (number < lease.RangeStart || number > lease.RangeEnd)
                continue;

            matched = lease;
            return true;
        }

        matched = null;
        return false;
    }

    private async Task AddReviewIssueAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var issue = ReconciliationIssue.Create(
            shopId,
            saleId,
            clientSaleId,
            deviceId,
            issueType,
            code,
            message,
            actorUserId);
        await reconciliationIssueRepository.AddAsync(issue, cancellationToken);
    }

    private static void AddCustomerVarianceWarnings(
        Guid shopId,
        Guid actorUserId,
        string clientSaleId,
        string deviceId,
        OfflineSaleSyncCommand sale,
        Customer? resolvedCustomer,
        List<string> warnings,
        List<ReconciliationIssue> issues)
    {
        if (resolvedCustomer is null)
            return;

        if (!resolvedCustomer.IsActive)
        {
            var message = $"Offline customer '{resolvedCustomer.Name}' is inactive at sync time.";
            warnings.Add(message);
            issues.Add(ReconciliationIssue.Create(
                shopId,
                null,
                clientSaleId,
                deviceId,
                ReconciliationIssueType.CustomerVariance,
                "offline_sync.customer_inactive",
                message,
                actorUserId));
        }

        var printedName = NormalizeOptional(sale.CustomerName);
        var printedPhone = NormalizeOptional(sale.CustomerPhone);
        var nameChanged = printedName is not null
            && !string.Equals(printedName, resolvedCustomer.Name, StringComparison.Ordinal);
        var phoneChanged = printedPhone is not null
            && !string.Equals(printedPhone, resolvedCustomer.PhoneNumber, StringComparison.Ordinal);

        if (!nameChanged && !phoneChanged)
            return;

        var changedMessage = "Offline customer details changed at sync time; printed customer details were preserved.";
        warnings.Add(changedMessage);
        issues.Add(ReconciliationIssue.Create(
            shopId,
            null,
            clientSaleId,
            deviceId,
            ReconciliationIssueType.CustomerVariance,
            "offline_sync.customer_changed",
            changedMessage,
            actorUserId));
    }

    private static void AddPricingVarianceWarnings(
        Guid shopId,
        Guid actorUserId,
        string clientSaleId,
        string deviceId,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        Dictionary<string, OfflineSaleSyncLineCommand> lineByKey,
        List<string> warnings,
        List<ReconciliationIssue> issues)
    {
        foreach (var validated in validatedLines)
        {
            if (!validated.HasPriceMismatch)
                continue;

            var printedLine = !string.IsNullOrWhiteSpace(validated.Command.ClientLineKey)
                && lineByKey.TryGetValue(validated.Command.ClientLineKey, out var line)
                    ? line
                    : null;
            var printedPrice = printedLine?.SalesPrice ?? validated.Command.SalesPrice;
            var message = $"Offline price variance for item '{validated.Item.Name}' batch '{validated.Batch.BatchNumber}': printed {printedPrice}, current {validated.Batch.SalesPrice}.";
            warnings.Add(message);
            issues.Add(ReconciliationIssue.Create(
                shopId,
                null,
                clientSaleId,
                deviceId,
                ReconciliationIssueType.PricingVariance,
                "offline_sync.pricing_variance",
                message,
                actorUserId,
                validated.Item.Id,
                validated.Batch.Id));
        }
    }

    private static void AddDiscountVarianceWarnings(
        Guid shopId,
        Guid actorUserId,
        string clientSaleId,
        string deviceId,
        OfflineSaleSyncCommand sale,
        IEnumerable<OfflineSaleSyncLineCommand> lines,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById,
        List<string> warnings,
        List<ReconciliationIssue> issues)
    {
        if (sale.ConfiguredSaleRuleId.HasValue
            && IsSaleDiscountRuleVariance(sale, activeDiscountRulesById))
        {
            var message = "Offline sale discount rule changed at sync time; printed sale discount was preserved.";
            warnings.Add(message);
            issues.Add(ReconciliationIssue.Create(
                shopId,
                null,
                clientSaleId,
                deviceId,
                ReconciliationIssueType.DiscountVariance,
                "offline_sync.discount_variance",
                message,
                actorUserId));
        }

        foreach (var line in lines)
        {
            if (!line.ConfiguredBatchRuleId.HasValue
                || !IsBatchDiscountRuleVariance(line, activeDiscountRulesById))
            {
                continue;
            }

            var message = $"Offline item discount rule changed for batch '{line.BatchNumber}'; printed item discount was preserved.";
            warnings.Add(message);
            issues.Add(ReconciliationIssue.Create(
                shopId,
                null,
                clientSaleId,
                deviceId,
                ReconciliationIssueType.DiscountVariance,
                "offline_sync.discount_variance",
                message,
                actorUserId,
                inventoryBatchId: line.InventoryBatchId));
        }
    }

    private static bool IsSaleDiscountRuleVariance(
        OfflineSaleSyncCommand sale,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById)
    {
        if (!sale.ConfiguredSaleRuleId.HasValue
            || !activeDiscountRulesById.TryGetValue(sale.ConfiguredSaleRuleId.Value, out var rule))
        {
            return true;
        }

        return rule.RuleType != sale.ConfiguredSaleRuleType
            || rule.Percentage != sale.ConfiguredSaleRulePercentage
            || rule.ThresholdAmount != sale.ConfiguredSaleRuleThresholdAmount;
    }

    private static bool IsBatchDiscountRuleVariance(
        OfflineSaleSyncLineCommand line,
        IReadOnlyDictionary<Guid, DiscountRule> activeDiscountRulesById)
    {
        if (!line.ConfiguredBatchRuleId.HasValue
            || !activeDiscountRulesById.TryGetValue(line.ConfiguredBatchRuleId.Value, out var rule))
        {
            return true;
        }

        return rule.InventoryBatchId != line.InventoryBatchId
            || rule.RuleType != DiscountRuleType.BatchPercentage
            || rule.Percentage != line.ConfiguredBatchRulePercentage;
    }

    private static List<OfflineStockConsumption> BuildStockConsumptionPlan(
        Guid shopId,
        Guid actorUserId,
        string clientSaleId,
        string deviceId,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        List<string> warnings,
        List<ReconciliationIssue> issues)
    {
        var remainingByBatchId = validatedLines
            .Select(line => line.Batch)
            .DistinctBy(batch => batch.Id)
            .ToDictionary(batch => batch.Id, batch => Math.Max(0m, batch.Quantity));
        var remainingByItemId = validatedLines
            .Select(line => line.Inventory)
            .DistinctBy(inventory => inventory.ItemId)
            .ToDictionary(inventory => inventory.ItemId, inventory => Math.Max(0m, inventory.Quantity));
        var plan = new List<OfflineStockConsumption>(validatedLines.Count);

        foreach (var validated in validatedLines)
        {
            var availableQuantity = Math.Min(
                remainingByBatchId[validated.Batch.Id],
                remainingByItemId[validated.Item.Id]);
            var consumedQuantity = Math.Min(validated.Command.Quantity, availableQuantity);
            remainingByBatchId[validated.Batch.Id] -= consumedQuantity;
            remainingByItemId[validated.Item.Id] -= consumedQuantity;

            if (consumedQuantity < validated.Command.Quantity)
            {
                var message = $"Offline stock shortage for item '{validated.Item.Name}' batch '{validated.Batch.BatchNumber}': printed {validated.Command.Quantity}, consumed {consumedQuantity}.";
                warnings.Add(message);
                issues.Add(ReconciliationIssue.Create(
                    shopId,
                    null,
                    clientSaleId,
                    deviceId,
                    ReconciliationIssueType.StockVariance,
                    "offline_sync.stock_shortage",
                    message,
                    actorUserId,
                    validated.Item.Id,
                    validated.Batch.Id,
                    validated.Command.Quantity,
                    availableQuantity,
                    consumedQuantity));
            }

            plan.Add(new OfflineStockConsumption(consumedQuantity));
        }

        return plan;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OfflineSaleSyncResultDto BuildNeedsReviewResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusNeedsReview, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    private static OfflineSaleSyncResultDto BuildErrorResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusFailed, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    private static Error GetSaveFailureError(DbUpdateException exception) =>
        ContainsExceptionText(exception, "ix_sales_shop_id_invoice_number")
            ? Errors.Sale.InvoiceNumberAlreadyUsed
            : Errors.General.Unexpected("Offline sale could not be synced. Please retry.");

    private static bool ContainsExceptionText(Exception exception, string text)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed record OfflineStockConsumption(decimal ConsumedQuantity);
}
