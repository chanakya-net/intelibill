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

    public async Task<ErrorOr<DashboardDto>> Handle(
        GetDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var reportingDay = query.ReportingDay ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var sales = await saleRepository.GetByShopAndDateAsync(query.ShopId, reportingDay, cancellationToken);
        var expenses = await expenseRepository.GetByShopAndDateAsync(query.ShopId, reportingDay, cancellationToken);
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

        return new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            ReportingDay: reportingDay,
            SalesCount: salesCount,
            SalesBooked: salesBooked,
            CashCollected: cashCollected,
            ProfitBeforeTax: profitBeforeTax,
            ProfitAfterTax: profitAfterTax,
            ExpenseRecorded: expenseRecorded,
            ExpenseCorrection: expenseCorrection,
            NetExpense: netExpense,
            CreditSalesAmount: creditSalesAmount,
            CreditSalesPercentage: creditSalesPercentage,
            PaymentMix: paymentMix,
            CreditShareWarning: creditShareWarning,
            RunningLowStockCount: runningLowStock.Count,
            CriticalStockCount: criticalStock.Count,
            RankedShortageList: rankedShortageList,
            HighestDueCustomer: highestDueCustomer,
            TopFiveDueCustomers: topFiveDueCustomers);
    }
}

