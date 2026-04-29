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

        var customerIds = customers.Select(c => c.Id).ToList();
        var customerBalances = await customerLedgerEntryRepository.GetCustomerBalancesAsync(
            query.ShopId, customerIds, cancellationToken);

        // Sales KPIs
        var salesCount = sales.Count;
        var salesBooked = sales.Sum(s => s.TotalAmount);
        var cashCollected = sales.Sum(s => s.PaidAmount);
        var totalCost = sales.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity);
        var totalTax = sales.Sum(s => s.TotalTaxAmount);
        var profitBeforeTax = salesBooked - totalTax - totalCost;
        var profitAfterTax = salesBooked - totalCost;

        // Expense KPIs
        var expenseRecorded = expenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
        var expenseCorrection = expenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);
        var netExpense = expenseRecorded + expenseCorrection;

        // Payment Behavior
        var creditSalesAmount = sales.Where(s => s.PaymentMethod == PaymentMethod.Credit).Sum(s => s.TotalAmount);
        var creditSalesPercentage = salesBooked > 0 ? creditSalesAmount / salesBooked : 0m;
        var paymentMix = new PaymentMixDto(
            Cash: sales.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.TotalAmount),
            Upi: sales.Where(s => s.PaymentMethod == PaymentMethod.UPI).Sum(s => s.TotalAmount),
            Card: sales.Where(s => s.PaymentMethod == PaymentMethod.Card).Sum(s => s.TotalAmount),
            Credit: creditSalesAmount);
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
        if (!isStaff)
        {
            var salesByDay = sales
                .GroupBy(s => DateOnly.FromDateTime(s.SoldAt.UtcDateTime))
                .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount));

            salesTrendSeries = [];
            for (var day = query.StartDate; day <= query.EndDate; day = day.AddDays(1))
            {
                salesTrendSeries.Add(new SalesTrendPointDto(
                    Date: day,
                    Amount: salesByDay.GetValueOrDefault(day, 0m)));
            }
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
            SalesTrendSeries: salesTrendSeries);
    }
}

