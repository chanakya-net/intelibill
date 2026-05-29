using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal static class OfflineSaleSyncHelpers
{
    private const string StatusDuplicate = "duplicate";
    private const string StatusFailed = "failed";
    private const string StatusNeedsReview = "NeedsReview";

    internal static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static OfflineSaleSyncResultDto BuildDuplicateResult(string clientSaleId, Sale sale) =>
        new(clientSaleId, StatusDuplicate, sale.Id, sale.InvoiceNumber, [])
        {
            Warnings = sale.Warnings,
        };

    internal static OfflineSaleSyncResultDto BuildNeedsReviewResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusNeedsReview, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    internal static OfflineSaleSyncResultDto BuildErrorResult(string clientSaleId, Error error) =>
        new(clientSaleId, StatusFailed, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);

    internal static Error GetSaveFailureError(DbUpdateException exception) =>
        ContainsExceptionText(exception, "ix_sales_shop_id_invoice_number")
            ? Errors.Sale.InvoiceNumberAlreadyUsed
            : Errors.General.Unexpected("Offline sale could not be synced. Please retry.");

    internal static bool HasNegativeOfflineLineAmounts(OfflineSaleSyncLineCommand line) =>
        line.PreTaxAmountBeforeDiscount < 0
        || line.ItemDiscountAmount < 0
        || line.SaleDiscountAmount < 0
        || line.TaxableAmount < 0
        || line.TaxAmount < 0
        || line.TotalAmount < 0;

    internal static void AddCustomerVarianceWarnings(
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

    internal static void AddPricingVarianceWarnings(
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
            if (validated.LineType == SaleLineType.Service)
                continue;

            if (!validated.HasPriceMismatch)
                continue;

            var printedLine = !string.IsNullOrWhiteSpace(validated.Command.ClientLineKey)
                && lineByKey.TryGetValue(validated.Command.ClientLineKey, out var line)
                    ? line
                    : null;
            var printedPrice = printedLine?.SalesPrice ?? validated.Command.SalesPrice;
            var message = $"Offline price variance for item '{validated.Item!.Name}' batch '{validated.Batch!.BatchNumber}': printed {printedPrice}, current {validated.Batch!.SalesPrice}.";
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
                validated.Item!.Id,
                validated.Batch!.Id));
        }
    }

    internal static void AddDiscountVarianceWarnings(
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
            if (line.IsServiceLine)
                continue;

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

    internal static void AddValidationVarianceIssues(
        Guid shopId,
        Guid actorUserId,
        string clientSaleId,
        string deviceId,
        IEnumerable<string> validationWarnings,
        List<ReconciliationIssue> issues)
    {
        foreach (var warning in validationWarnings)
        {
            if (warning.StartsWith("Price mismatch", StringComparison.Ordinal))
                continue;

            issues.Add(ReconciliationIssue.Create(
                shopId,
                null,
                clientSaleId,
                deviceId,
                ReconciliationIssueType.ValidationConflict,
                "offline_sync.validation_variance",
                warning,
                actorUserId));
        }
    }

    internal static List<OfflineStockConsumption> BuildStockConsumptionPlan(
        Guid shopId,
        Guid actorUserId,
        string clientSaleId,
        string deviceId,
        IReadOnlyList<ValidatedSaleLine> validatedLines,
        List<string> warnings,
        List<ReconciliationIssue> issues)
    {
        var remainingByBatchId = validatedLines
            .Where(line => line.LineType == SaleLineType.Goods)
            .Select(line => line.Batch!)
            .DistinctBy(batch => batch.Id)
            .ToDictionary(batch => batch.Id, batch => Math.Max(0m, batch.Quantity));
        var remainingByItemId = validatedLines
            .Where(line => line.LineType == SaleLineType.Goods)
            .Select(line => line.Inventory!)
            .DistinctBy(inventory => inventory.ItemId)
            .ToDictionary(inventory => inventory.ItemId, inventory => Math.Max(0m, inventory.Quantity));
        var plan = new List<OfflineStockConsumption>(validatedLines.Count);

        foreach (var validated in validatedLines)
        {
            if (validated.LineType == SaleLineType.Service)
            {
                plan.Add(new OfflineStockConsumption(0m));
                continue;
            }

            var availableQuantity = Math.Min(
                remainingByBatchId[validated.Batch!.Id],
                remainingByItemId[validated.Item!.Id]);
            var consumedQuantity = Math.Min(validated.Command.Quantity, availableQuantity);
            remainingByBatchId[validated.Batch!.Id] -= consumedQuantity;
            remainingByItemId[validated.Item!.Id] -= consumedQuantity;

            if (consumedQuantity < validated.Command.Quantity)
            {
                var message = $"Offline stock shortage for item '{validated.Item!.Name}' batch '{validated.Batch!.BatchNumber}': printed {validated.Command.Quantity}, consumed {consumedQuantity}.";
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
                    validated.Item!.Id,
                    validated.Batch!.Id,
                    validated.Command.Quantity,
                    availableQuantity,
                    consumedQuantity));
            }

            plan.Add(new OfflineStockConsumption(consumedQuantity));
        }

        return plan;
    }

    internal static bool ContainsExceptionText(Exception exception, string text)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
}
