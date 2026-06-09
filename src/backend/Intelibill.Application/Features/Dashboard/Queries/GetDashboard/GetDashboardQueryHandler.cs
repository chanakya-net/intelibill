using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using System.Globalization;

namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(
    ISaleRepository saleRepository,
    IExpenseRepository expenseRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    ISupplierLedgerEntryRepository supplierLedgerEntryRepository,
    IInventoryRepository inventoryRepository,
    ProfitLossReportBuilder profitLossReportBuilder)
{
    private const int MaxRangeDays = 90;
    private const int DefaultRangeDays = 29;

    public async Task<ErrorOr<DashboardDto>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        if (!IsAllowedRole(query.Role))
            return Errors.Dashboard.UserIsNotOwnerOrManager;

        var validationResult = ValidateDateRange(query.From, query.To);
        if (validationResult.IsError)
            return validationResult.Errors;

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var appliedTo = query.To ?? today;
        var appliedFrom = query.From ?? appliedTo.AddDays(-DefaultRangeDays);
        var previousPeriod = GetPreviousPeriodRange(appliedFrom, appliedTo);

        var summary = await saleRepository.GetHistorySummaryAsync(query.ShopId, appliedFrom, appliedTo, cancellationToken);
        var salesTrendBuckets = await saleRepository.GetDailySalesTrendAsync(query.ShopId, appliedFrom, appliedTo, cancellationToken);
        var latestSales = await saleRepository.GetLatestDashboardSalesAsync(query.ShopId, cancellationToken);
        var expenseTotal = await expenseRepository.GetSumByShopAndDateRangeAsync(query.ShopId, appliedFrom, appliedTo, cancellationToken);
        var stockValue = await inventoryBatchRepository.GetCurrentStockValueByShopAsync(query.ShopId, cancellationToken);
        var customerCreditDue = await customerLedgerEntryRepository.GetCustomerCreditDueAsync(query.ShopId, cancellationToken);
        var pendingPurchaseOrderAlerts = await purchaseOrderRepository.GetPendingPurchaseOrderAlertsAsync(
            query.ShopId,
            cancellationToken);
        var supplierPayables = await supplierLedgerEntryRepository.GetSupplierPayablesAsync(query.ShopId, cancellationToken);
        var lowStockItemCount = await inventoryRepository.CountLowStockItemsByShopAsync(query.ShopId, cancellationToken);
        var profitLossReport = await profitLossReportBuilder.BuildAsync(
            query.ShopId,
            appliedFrom,
            appliedTo,
            type: null,
            search: null,
            cancellationToken);
        var lowStockAlerts = await inventoryRepository.GetTopLowStockAlertsAsync(query.ShopId, cancellationToken);
        var previousProfitLossReport = await profitLossReportBuilder.BuildAsync(
            query.ShopId,
            previousPeriod.From,
            previousPeriod.To,
            type: null,
            search: null,
            cancellationToken);
        var netProfitChangePercent = CalculatePercentChange(
            profitLossReport.Summary.NetProfitAfterTax,
            previousProfitLossReport.Summary.NetProfitAfterTax);
        var expiringBatchAlerts = await inventoryBatchRepository.GetExpiringBatchAlertsAsync(
            query.ShopId,
            today,
            cancellationToken);

        var result = new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: appliedFrom,
            EndDate: appliedTo,
            SalesCount: summary.InvoiceCount,
            SalesRevenue: summary.PeriodSales,
            HasNoSalesActivity: summary.InvoiceCount == 0,
            CustomerCreditDue: customerCreditDue,
            SalesBooked: 0m,
            NetSalesBooked: summary.PeriodSales,
            WastageCost: 0m,
            CashCollected: 0m,
            ProfitBeforeTax: 0m,
            ProfitAfterTax: 0m,
            NetProfit: profitLossReport.Summary.NetProfitAfterTax,
            NetProfitChangePercent: netProfitChangePercent,
            ExpenseRecorded: 0m,
            ExpenseCorrection: 0m,
            NetExpense: expenseTotal,
            SupplierPayables: supplierPayables,
            CreditSalesAmount: 0m,
            CreditSalesPercentage: 0m,
            PaymentMix: new PaymentMixDto(0m, 0m, 0m, 0m),
            CreditShareWarning: false,
            RunningLowStockCount: 0,
            LowStockItemCount: lowStockItemCount,
            CriticalStockCount: 0,
            RankedShortageList: Array.Empty<StockShortageItemDto>(),
            HighestDueCustomer: new CustomerDueDto(Guid.Empty, string.Empty, 0m),
            TopFiveDueCustomers: Array.Empty<CustomerDueDto>(),
            Alerts: pendingPurchaseOrderAlerts.Select(MapPendingPurchaseOrderAlert)
                .Concat(lowStockAlerts.Select(MapLowStockAlert))
                .Concat(expiringBatchAlerts.Select(alert => MapExpiringBatchAlert(alert, today)))
                .ToList(),
            SalesTrendSeries: BuildSalesTrendSeries(appliedFrom, appliedTo, salesTrendBuckets),
            ProfitTrendSeries: Array.Empty<ProfitTrendPointDto>(),
            PaymentMixTrendSeries: Array.Empty<PaymentMixTrendPointDto>(),
            PreviousPeriodSummary: new PreviousPeriodSummaryDto(
                StartDate: previousPeriod.From,
                EndDate: previousPeriod.To,
                SalesCount: 0,
                SalesBooked: 0m,
                NetSalesBooked: 0m,
                ProfitAfterTax: 0m,
                NetExpense: 0m,
                CreditSalesPercentage: 0m),
            LatestSales: latestSales.Select(s => new DashboardLatestSaleDto(
                s.SaleId,
                s.InvoiceNumber,
                s.CustomerDisplayName,
                s.SoldAt,
                s.TotalAmount)).ToList(),
            StockValue: stockValue);

        return result;
    }

    private static List<SalesTrendPointDto> BuildSalesTrendSeries(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<SalesTrendBucketReadModel> buckets)
    {
        var bucketByDate = buckets.ToDictionary(bucket => bucket.Date);
        var days = endDate.DayNumber - startDate.DayNumber + 1;
        var series = new List<SalesTrendPointDto>(days);

        for (var index = 0; index < days; index++)
        {
            var date = startDate.AddDays(index);
            bucketByDate.TryGetValue(date, out var bucket);

            var grossSales = bucket?.GrossSales ?? 0m;
            var netSales = grossSales - (bucket?.ReturnAmount ?? 0m);
            series.Add(new SalesTrendPointDto(date, grossSales, netSales));
        }

        return series;
    }

    private static (DateOnly From, DateOnly To) GetPreviousPeriodRange(DateOnly appliedFrom, DateOnly appliedTo)
    {
        var rangeLength = appliedTo.DayNumber - appliedFrom.DayNumber + 1;
        var previousTo = appliedFrom.AddDays(-1);
        var previousFrom = previousTo.AddDays(-(rangeLength - 1));
        return (previousFrom, previousTo);
    }

    private static decimal? CalculatePercentChange(decimal currentValue, decimal previousValue)
    {
        if (previousValue == 0m)
            return null;

        return decimal.Round((currentValue - previousValue) * 100m / Math.Abs(previousValue), 2, MidpointRounding.AwayFromZero);
    }

    private static bool IsAllowedRole(string role) =>
        string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static ErrorOr<Success> ValidateDateRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue != to.HasValue)
            return Errors.Dashboard.PartialDateRange;

        if (from.HasValue && to.HasValue)
        {
            if (from.Value > to.Value)
                return Errors.Dashboard.InvalidDateRange;

            if (to.Value.DayNumber - from.Value.DayNumber >= MaxRangeDays)
                return Errors.Dashboard.DateRangeTooLarge;
        }

        return Result.Success;
    }

    private static DashboardAlertDto MapLowStockAlert(LowStockAlertReadModel alert) =>
        new(
            AlertType: "LowStock",
            Priority: 2,
            Title: "Low stock",
            Message: $"{alert.ItemName} is running low ({alert.Quantity} remaining, reorder at {alert.ReorderLevel}).",
            ActionLabel: "Manage inventory",
            ActionRoute: "/inventory");

    private static DashboardAlertDto MapPendingPurchaseOrderAlert(PendingPurchaseOrderAlertReadModel alert)
    {
        var reference = string.IsNullOrWhiteSpace(alert.SupplierName)
            ? alert.PurchaseOrderNumber
            : $"{alert.PurchaseOrderNumber} from {alert.SupplierName}";

        var message = alert.Status == PurchaseOrderStatus.PartiallyReceived
            ? $"{reference} has been partially received."
            : $"{reference} is waiting for goods receipt.";

        return new DashboardAlertDto(
            AlertType: "PendingPurchaseOrder",
            Priority: alert.Status == PurchaseOrderStatus.PartiallyReceived ? 0 : 1,
            Title: "Pending purchase order",
            Message: message,
            ActionLabel: "Review purchase orders",
            ActionRoute: "/inventory/purchase-orders");
    }

    private static DashboardAlertDto MapExpiringBatchAlert(ExpiringBatchAlertReadModel alert, DateOnly today)
    {
        var expiryText = alert.ExpiryDate == today
            ? "today"
            : $"on {alert.ExpiryDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

        return new DashboardAlertDto(
            AlertType: "ExpiringBatch",
            Priority: alert.ExpiryDate == today ? 0 : 1,
            Title: "Expiring batch",
            Message: $"{alert.ItemName} batch {alert.BatchNumber} expires {expiryText}.",
            ActionLabel: "Review batches",
            ActionRoute: "/inventory/batches");
    }
}
