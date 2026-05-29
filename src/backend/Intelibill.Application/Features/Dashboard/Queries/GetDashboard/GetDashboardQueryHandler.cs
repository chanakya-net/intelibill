using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    ISaleReturnRepository saleReturnRepository,
    IExpenseRepository expenseRepository,
    IInventoryRepository inventoryRepository,
    IInventoryAdjustmentRepository inventoryAdjustmentRepository,
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    private const int MaxRangeDays = 90;

    public async Task<ErrorOr<DashboardDto>> Handle(
        GetDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        if (query.StartDate > query.EndDate)
            return Errors.Dashboard.InvalidDateRange;

        if (query.EndDate > today)
            return Errors.Dashboard.FutureDateNotAllowed;

        var dayDifference = (int)(query.EndDate.ToDateTime(TimeOnly.MinValue) - query.StartDate.ToDateTime(TimeOnly.MinValue)).TotalDays;
        if (dayDifference >= MaxRangeDays)
            return Errors.Dashboard.RangeExceeds90Days;

        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var isStaff = membership.Role == ShopRole.Staff;

        var sales = await saleRepository.GetByShopAndDateRangeAsync(query.ShopId, query.StartDate, query.EndDate, cancellationToken);
        var saleReturns = await saleReturnRepository.GetByShopAndDateRangeAsync(query.ShopId, query.StartDate, query.EndDate, cancellationToken);
        var expenses = await expenseRepository.GetByShopAndDateRangeAsync(query.ShopId, query.StartDate, query.EndDate, cancellationToken);
        var inventories = await inventoryRepository.GetAllByShopWithItemAsync(query.ShopId, cancellationToken);
        var adjustmentLosses = await inventoryAdjustmentRepository.GetDashboardLossesByShopAndDateRangeAsync(
            query.ShopId, query.StartDate, query.EndDate, cancellationToken);
        var customers = await customerRepository.GetByShopIdAsync(query.ShopId, cancellationToken);

        var spanDays = (int)(query.EndDate.ToDateTime(TimeOnly.MinValue) - query.StartDate.ToDateTime(TimeOnly.MinValue)).TotalDays;
        var prevEndDate = query.StartDate.AddDays(-1);
        var prevStartDate = prevEndDate.AddDays(-spanDays);
        var prevSales = await saleRepository.GetByShopAndDateRangeAsync(query.ShopId, prevStartDate, prevEndDate, cancellationToken);
        var prevSaleReturns = await saleReturnRepository.GetByShopAndDateRangeAsync(query.ShopId, prevStartDate, prevEndDate, cancellationToken);
        var prevExpenses = await expenseRepository.GetByShopAndDateRangeAsync(query.ShopId, prevStartDate, prevEndDate, cancellationToken);
        var prevAdjustmentLosses = await inventoryAdjustmentRepository.GetDashboardLossesByShopAndDateRangeAsync(
            query.ShopId, prevStartDate, prevEndDate, cancellationToken);

        var customerIds = customers.Select(c => c.Id).ToList();
        var customerBalances = await customerLedgerEntryRepository.GetCustomerBalancesAsync(
            query.ShopId, customerIds, cancellationToken);
        var saleItemTypes = await BuildSaleItemTypeLookupAsync(
            query.ShopId,
            sales,
            saleReturns,
            saleRepository,
            cancellationToken);

        // Compute all KPIs using pure functions
        var salesKpis = SalesKpiCalculator.CalculateSalesKpis(sales, saleReturns, adjustmentLosses, saleItemTypes);
        var expenseKpis = ExpenseKpiCalculator.CalculateExpenseKpis(expenses);
        var paymentMix = SalesKpiCalculator.CalculatePaymentMix(sales);
        var stockRisk = StockRiskKpiCalculator.CalculateStockRisk(inventories);

        var creditSalesPercentage = salesKpis.SalesBooked > 0
            ? paymentMix.Credit / salesKpis.SalesBooked
            : 0m;

        var (highestDueCustomer, topFiveDueCustomers) = CustomerDueCalculator.CalculateCustomerDues(
            customerBalances, customers);

        var alerts = DashboardAlertBuilder.BuildAlerts(
            isStaff, stockRisk.CriticalStockCount, stockRisk.RunningLowStockCount,
            highestDueCustomer, creditSalesPercentage);

        // Trend series and previous-period (only for Owner/Manager)
        List<SalesTrendPointDto>? salesTrendSeries = null;
        List<ProfitTrendPointDto>? profitTrendSeries = null;
        List<PaymentMixTrendPointDto>? paymentMixTrendSeries = null;
        PreviousPeriodSummaryDto? previousPeriodSummary = null;

        if (!isStaff)
        {
            var trends = DashboardTrendBuilder.BuildTrendSeries(
                sales,
                saleReturns,
                query.StartDate,
                query.EndDate,
                adjustmentLosses,
                saleItemTypes);
            salesTrendSeries = trends.SalesTrend;
            profitTrendSeries = trends.ProfitTrend;
            paymentMixTrendSeries = trends.PaymentMixTrend;

            previousPeriodSummary = DashboardTrendBuilder.BuildPreviousPeriodSummary(
                prevSales, prevSaleReturns, prevExpenses, prevStartDate, prevEndDate, prevAdjustmentLosses);
        }

        return new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: query.StartDate,
            EndDate: query.EndDate,
            SalesCount: salesKpis.SalesCount,
            HasNoSalesActivity: salesKpis.SalesCount == 0
                && !saleReturns.Any(r => !r.IsVoided)
                && adjustmentLosses.Count == 0,
            SalesBooked: isStaff ? null : salesKpis.SalesBooked,
            NetSalesBooked: isStaff ? null : salesKpis.NetSalesBooked,
            WastageCost: isStaff ? null : salesKpis.WastageCost,
            CashCollected: isStaff ? null : salesKpis.CashCollected,
            ProfitBeforeTax: isStaff ? null : salesKpis.ProfitBeforeTax,
            ProfitAfterTax: isStaff ? null : salesKpis.ProfitAfterTax,
            ExpenseRecorded: isStaff ? null : expenseKpis.ExpenseRecorded,
            ExpenseCorrection: isStaff ? null : expenseKpis.ExpenseCorrection,
            NetExpense: isStaff ? null : expenseKpis.NetExpense,
            CreditSalesAmount: isStaff ? null : paymentMix.Credit,
            CreditSalesPercentage: isStaff ? null : creditSalesPercentage,
            PaymentMix: isStaff ? null : paymentMix,
            CreditShareWarning: isStaff ? null : creditSalesPercentage >= SalesKpiCalculator.CreditShareWarningThreshold,
            RunningLowStockCount: stockRisk.RunningLowStockCount,
            CriticalStockCount: stockRisk.CriticalStockCount,
            RankedShortageList: stockRisk.RankedShortageList,
            HighestDueCustomer: isStaff ? null : highestDueCustomer,
            TopFiveDueCustomers: isStaff ? null : topFiveDueCustomers,
            Alerts: alerts,
            SalesTrendSeries: salesTrendSeries,
            ProfitTrendSeries: profitTrendSeries,
            PaymentMixTrendSeries: paymentMixTrendSeries,
            PreviousPeriodSummary: previousPeriodSummary);
    }

    private static async Task<IReadOnlyDictionary<Guid, SaleLineType>> BuildSaleItemTypeLookupAsync(
        Guid shopId,
        IReadOnlyCollection<Sale> sales,
        IReadOnlyCollection<SaleReturn> saleReturns,
        ISaleRepository saleRepository,
        CancellationToken cancellationToken)
    {
        var saleItemTypes = sales
            .SelectMany(sale => sale.Items)
            .ToDictionary(item => item.Id, item => item.LineType);

        var missingSaleIds = saleReturns
            .Where(saleReturn => saleReturn.Items.Any(item => !saleItemTypes.ContainsKey(item.SaleItemId)))
            .Select(saleReturn => saleReturn.SaleId)
            .Distinct()
            .ToList();

        foreach (var saleId in missingSaleIds)
        {
            var sale = await saleRepository.GetByIdAsync(saleId, shopId, cancellationToken);
            if (sale is null)
            {
                continue;
            }

            foreach (var item in sale.Items)
            {
                saleItemTypes[item.Id] = item.LineType;
            }
        }

        return saleItemTypes;
    }
}
