using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository,
    IExpenseRepository expenseRepository,
    IInventoryRepository inventoryRepository,
    ICustomerRepository customerRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    private const decimal CreditShareWarningThreshold = 0.40m;
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

        if (query.EndDate.DayNumber - query.StartDate.DayNumber >= MaxRangeDays)
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
        var expenses = await expenseRepository.GetByShopAndDateRangeAsync(query.ShopId, query.StartDate, query.EndDate, cancellationToken);
        var inventories = await inventoryRepository.GetAllByShopWithItemAsync(query.ShopId, cancellationToken);
        var customers = await customerRepository.GetByShopIdAsync(query.ShopId, cancellationToken);

        // Previous period — same span, immediately before startDate
        var spanDays = query.EndDate.DayNumber - query.StartDate.DayNumber;
        var prevEndDate = query.StartDate.AddDays(-1);
        var prevStartDate = prevEndDate.AddDays(-spanDays);
        var prevSales = await saleRepository.GetByShopAndDateRangeAsync(query.ShopId, prevStartDate, prevEndDate, cancellationToken);
        var prevExpenses = await expenseRepository.GetByShopAndDateRangeAsync(query.ShopId, prevStartDate, prevEndDate, cancellationToken);

        var customerIds = customers.Select(c => c.Id).ToList();
        var customerBalances = await customerLedgerEntryRepository.GetCustomerBalancesAsync(
            query.ShopId, customerIds, cancellationToken);

        // Sales KPIs
        var salesCount = sales.Count;
        var salesBooked = sales.Sum(s => s.TotalAmount);
        var cashCollected = sales.Sum(s => s.PaidAmount);
        var totalCost = sales.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity);
        var totalTax = sales.Sum(s => s.TotalTaxAmount);
        var profitBeforeTax = salesBooked - totalCost;
        var profitAfterTax = salesBooked - totalTax - totalCost;

        // Expense KPIs
        var expenseRecorded = expenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
        var expenseCorrection = expenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);
        var netExpense = expenseRecorded + expenseCorrection;

        // Payment Behavior
        // Credit behavior should include due portions even when payment method is not Credit.
        var paymentMix = CalculatePaymentMix(sales);
        var creditSalesAmount = paymentMix.Credit;
        var creditSalesPercentage = salesBooked > 0 ? creditSalesAmount / salesBooked : 0m;
        var creditShareWarning = creditSalesPercentage >= CreditShareWarningThreshold;

        // Stock Risk
        var runningLowStock = inventories.Where(i => i.Quantity > 0 && i.Quantity <= i.ReorderLevel).ToList();
        var criticalStock = inventories.Where(i => i.Quantity == 0).ToList();
        var rankedShortageList = inventories
            .Where(i => i.Quantity <= i.ReorderLevel)
            .OrderByDescending(i => i.ReorderLevel - i.Quantity)
            .Select(i => new StockShortageItemDto(
                ItemName: i.Item.Name,
                Quantity: i.Quantity,
                ReorderLevel: i.ReorderLevel,
                Shortage: i.ReorderLevel - i.Quantity))
            .ToList();

        // Receivable Risk
        var customerDueSummaries = customerBalances
            .Where(kvp => kvp.Value > 0)
            .Select(kvp =>
            {
                var customer = customers.FirstOrDefault(c => c.Id == kvp.Key);
                var displayName = customer is not null && !string.IsNullOrWhiteSpace(customer.Name)
                    ? customer.Name
                    : customer?.PhoneNumber ?? "Unknown";
                return new CustomerDueDto(kvp.Key, displayName, kvp.Value);
            })
            .OrderByDescending(d => d.OutstandingDue)
            .ToList();

        var highestDueCustomer = customerDueSummaries.FirstOrDefault();
        var topFiveDueCustomers = customerDueSummaries.Take(5).ToList();

        // Alerts ordered by priority; financial alerts hidden from Staff
        var alerts = new List<DashboardAlertDto>();
        if (criticalStock.Count > 0)
            alerts.Add(new DashboardAlertDto("CriticalStock", 1));
        if (!isStaff && highestDueCustomer is not null)
            alerts.Add(new DashboardAlertDto("HighestDue", 2));
        if (runningLowStock.Count > 0)
            alerts.Add(new DashboardAlertDto("RunningLowStock", 3));
        if (!isStaff && creditShareWarning)
            alerts.Add(new DashboardAlertDto("CreditShareWarning", 4));

        // Sales Booked trend: daily buckets for the range (null for Staff)
        List<SalesTrendPointDto>? salesTrendSeries = null;
        List<ProfitTrendPointDto>? profitTrendSeries = null;
        List<PaymentMixTrendPointDto>? paymentMixTrendSeries = null;
        PreviousPeriodSummaryDto? previousPeriodSummary = null;
        if (!isStaff)
        {
            var salesByDay = sales
                .GroupBy(s => DateOnly.FromDateTime(s.SoldAt.UtcDateTime))
                .ToDictionary(
                    g => g.Key,
                    g => (
                        SalesBooked: g.Sum(s => s.TotalAmount),
                        Cost: g.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity),
                        Tax: g.Sum(s => s.TotalTaxAmount)));

            salesTrendSeries = [];
            profitTrendSeries = [];
            paymentMixTrendSeries = [];
            var paymentMixByDay = sales
                .GroupBy(s => DateOnly.FromDateTime(s.SoldAt.UtcDateTime))
                .ToDictionary(g => g.Key, g => CalculatePaymentMix(g.ToList()));

            for (var day = query.StartDate; day <= query.EndDate; day = day.AddDays(1))
            {
                var dayData = salesByDay.GetValueOrDefault(day, (SalesBooked: 0m, Cost: 0m, Tax: 0m));
                var dayPaymentMix = paymentMixByDay.GetValueOrDefault(day, new PaymentMixDto(0m, 0m, 0m, 0m));
                salesTrendSeries.Add(new SalesTrendPointDto(
                    Date: day,
                    Amount: dayData.SalesBooked));
                profitTrendSeries.Add(new ProfitTrendPointDto(
                    Date: day,
                    ProfitBeforeTax: dayData.SalesBooked - dayData.Cost,
                    ProfitAfterTax: dayData.SalesBooked - dayData.Tax - dayData.Cost));
                paymentMixTrendSeries.Add(new PaymentMixTrendPointDto(
                    Date: day,
                    Cash: dayPaymentMix.Cash,
                    Upi: dayPaymentMix.Upi,
                    Card: dayPaymentMix.Card,
                    Credit: dayPaymentMix.Credit));
            }

            // Previous period aggregates
            var prevSalesBooked = prevSales.Sum(s => s.TotalAmount);
            var prevCost = prevSales.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity);
            var prevCreditSales = CalculatePaymentMix(prevSales).Credit;
            var prevExpenseRecorded = prevExpenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
            var prevExpenseCorrection = prevExpenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);
            previousPeriodSummary = new PreviousPeriodSummaryDto(
                StartDate: prevStartDate,
                EndDate: prevEndDate,
                SalesCount: prevSales.Count,
                SalesBooked: prevSalesBooked,
                ProfitAfterTax: prevSalesBooked - prevSales.Sum(s => s.TotalTaxAmount) - prevCost,
                NetExpense: prevExpenseRecorded + prevExpenseCorrection,
                CreditSalesPercentage: prevSalesBooked > 0 ? prevCreditSales / prevSalesBooked : 0m);
        }

        return new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: query.StartDate,
            EndDate: query.EndDate,
            SalesCount: salesCount,
            HasNoSalesActivity: salesCount == 0,
            SalesBooked: isStaff ? null : salesBooked,
            CashCollected: isStaff ? null : cashCollected,
            ProfitBeforeTax: isStaff ? null : profitBeforeTax,
            ProfitAfterTax: isStaff ? null : profitAfterTax,
            ExpenseRecorded: isStaff ? null : expenseRecorded,
            ExpenseCorrection: isStaff ? null : expenseCorrection,
            NetExpense: isStaff ? null : netExpense,
            CreditSalesAmount: isStaff ? null : creditSalesAmount,
            CreditSalesPercentage: isStaff ? null : creditSalesPercentage,
            PaymentMix: isStaff ? null : paymentMix,
            CreditShareWarning: isStaff ? null : creditShareWarning,
            RunningLowStockCount: runningLowStock.Count,
            CriticalStockCount: criticalStock.Count,
            RankedShortageList: rankedShortageList,
            HighestDueCustomer: isStaff ? null : highestDueCustomer,
            TopFiveDueCustomers: isStaff ? null : topFiveDueCustomers,
            Alerts: alerts,
            SalesTrendSeries: salesTrendSeries,
            ProfitTrendSeries: profitTrendSeries,
                PaymentMixTrendSeries: paymentMixTrendSeries,
            PreviousPeriodSummary: previousPeriodSummary);
    }

    private static PaymentMixDto CalculatePaymentMix(IReadOnlyCollection<Domain.Entities.Sale> sales)
    {
        var cash = 0m;
        var upi = 0m;
        var card = 0m;
        var credit = 0m;

        foreach (var sale in sales)
        {
            var due = Math.Max(0m, sale.DueAmount);
            var paidPortion = Math.Max(0m, sale.TotalAmount - due);

            if (sale.PaymentMethod == PaymentMethod.Credit)
            {
                credit += sale.TotalAmount;
                continue;
            }

            credit += due;

            switch (sale.PaymentMethod)
            {
                case PaymentMethod.Cash:
                    cash += paidPortion;
                    break;
                case PaymentMethod.UPI:
                    upi += paidPortion;
                    break;
                case PaymentMethod.Card:
                    card += paidPortion;
                    break;
                default:
                    // Fallback to avoid dropping value if a new enum is introduced.
                    cash += paidPortion;
                    break;
            }
        }

        return new PaymentMixDto(Cash: cash, Upi: upi, Card: card, Credit: credit);
    }
}

