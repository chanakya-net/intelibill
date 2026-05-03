using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Dashboard.Services;

public static class DashboardKpiCalculator
{
    public const decimal CreditShareWarningThreshold = 0.40m;

    public sealed record SalesKpis(
        int SalesCount,
        decimal SalesBooked,
        decimal CashCollected,
        decimal TotalCost,
        decimal TotalTax,
        decimal ProfitBeforeTax,
        decimal ProfitAfterTax);

    public sealed record ExpenseKpis(
        decimal ExpenseRecorded,
        decimal ExpenseCorrection,
        decimal NetExpense);

    public sealed record StockRiskKpis(
        int RunningLowStockCount,
        int CriticalStockCount,
        IReadOnlyList<StockShortageItemDto> RankedShortageList);

    public static SalesKpis CalculateSalesKpis(IReadOnlyCollection<Sale> sales)
    {
        var salesBooked = sales.Sum(s => s.TotalAmount);
        var cashCollected = sales.Sum(s => s.PaidAmount);
        var totalCost = sales.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity);
        var totalTax = sales.Sum(s => s.TotalTaxAmount);

        return new SalesKpis(
            sales.Count,
            salesBooked,
            cashCollected,
            totalCost,
            totalTax,
            salesBooked - totalCost,
            salesBooked - totalTax - totalCost);
    }

    public static ExpenseKpis CalculateExpenseKpis(IReadOnlyCollection<Expense> expenses)
    {
        var recorded = expenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
        var correction = expenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);
        return new ExpenseKpis(recorded, correction, recorded + correction);
    }

    public static PaymentMixDto CalculatePaymentMix(IReadOnlyCollection<Sale> sales)
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
                    cash += paidPortion;
                    break;
            }
        }

        return new PaymentMixDto(Cash: cash, Upi: upi, Card: card, Credit: credit);
    }

    public static StockRiskKpis CalculateStockRisk(IReadOnlyCollection<Domain.Entities.Inventory> inventories)
    {
        var runningLow = inventories.Where(i => i.Quantity > 0 && i.Quantity <= i.ReorderLevel).ToList();
        var critical = inventories.Where(i => i.Quantity == 0).ToList();
        var ranked = inventories
            .Where(i => i.Quantity <= i.ReorderLevel)
            .OrderByDescending(i => i.ReorderLevel - i.Quantity)
            .Select(i => new StockShortageItemDto(
                ItemName: i.Item.Name,
                Quantity: i.Quantity,
                ReorderLevel: i.ReorderLevel,
                Shortage: i.ReorderLevel - i.Quantity))
            .ToList();

        return new StockRiskKpis(runningLow.Count, critical.Count, ranked);
    }

    public static (CustomerDueDto? Highest, List<CustomerDueDto> TopFive) CalculateCustomerDues(
        IReadOnlyDictionary<Guid, decimal> customerBalances,
        IReadOnlyCollection<Customer> customers)
    {
        var summaries = customerBalances
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

        return (summaries.FirstOrDefault(), summaries.Take(5).ToList());
    }

    public static List<DashboardAlertDto> BuildAlerts(
        bool isStaff,
        int criticalStockCount,
        int runningLowStockCount,
        CustomerDueDto? highestDueCustomer,
        decimal creditSalesPercentage)
    {
        var alerts = new List<DashboardAlertDto>();
        if (criticalStockCount > 0)
            alerts.Add(new DashboardAlertDto("CriticalStock", 1));
        if (!isStaff && highestDueCustomer is not null)
            alerts.Add(new DashboardAlertDto("HighestDue", 2));
        if (runningLowStockCount > 0)
            alerts.Add(new DashboardAlertDto("RunningLowStock", 3));
        if (!isStaff && creditSalesPercentage >= CreditShareWarningThreshold)
            alerts.Add(new DashboardAlertDto("CreditShareWarning", 4));
        return alerts;
    }

    public static (List<SalesTrendPointDto> SalesTrend, List<ProfitTrendPointDto> ProfitTrend, List<PaymentMixTrendPointDto> PaymentMixTrend) BuildTrendSeries(
        IReadOnlyCollection<Sale> sales,
        DateOnly startDate,
        DateOnly endDate)
    {
        var byDay = sales
            .GroupBy(s => DateOnly.FromDateTime(s.SoldAt.UtcDateTime))
            .ToDictionary(
                g => g.Key,
                g => (
                    SalesBooked: g.Sum(s => s.TotalAmount),
                    Cost: g.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity),
                    Tax: g.Sum(s => s.TotalTaxAmount),
                    PaymentMix: CalculatePaymentMix(g.ToList())));

        var salesTrend = new List<SalesTrendPointDto>();
        var profitTrend = new List<ProfitTrendPointDto>();
        var paymentMixTrend = new List<PaymentMixTrendPointDto>();

        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            var dayData = byDay.GetValueOrDefault(day, (SalesBooked: 0m, Cost: 0m, Tax: 0m, PaymentMix: new PaymentMixDto(0m, 0m, 0m, 0m)));
            salesTrend.Add(new SalesTrendPointDto(Date: day, Amount: dayData.SalesBooked));
            profitTrend.Add(new ProfitTrendPointDto(
                Date: day,
                ProfitBeforeTax: dayData.SalesBooked - dayData.Cost,
                ProfitAfterTax: dayData.SalesBooked - dayData.Tax - dayData.Cost));
            paymentMixTrend.Add(new PaymentMixTrendPointDto(
                Date: day, Cash: dayData.PaymentMix.Cash, Upi: dayData.PaymentMix.Upi,
                Card: dayData.PaymentMix.Card, Credit: dayData.PaymentMix.Credit));
        }

        return (salesTrend, profitTrend, paymentMixTrend);
    }

    public static PreviousPeriodSummaryDto BuildPreviousPeriodSummary(
        IReadOnlyCollection<Sale> prevSales,
        IReadOnlyCollection<Expense> prevExpenses,
        DateOnly prevStartDate,
        DateOnly prevEndDate)
    {
        var prevSalesBooked = prevSales.Sum(s => s.TotalAmount);
        var prevCost = prevSales.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity);
        var prevCreditSales = CalculatePaymentMix(prevSales).Credit;
        var prevExpenseRecorded = prevExpenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
        var prevExpenseCorrection = prevExpenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);

        return new PreviousPeriodSummaryDto(
            StartDate: prevStartDate,
            EndDate: prevEndDate,
            SalesCount: prevSales.Count,
            SalesBooked: prevSalesBooked,
            ProfitAfterTax: prevSalesBooked - prevSales.Sum(s => s.TotalTaxAmount) - prevCost,
            NetExpense: prevExpenseRecorded + prevExpenseCorrection,
            CreditSalesPercentage: prevSalesBooked > 0 ? prevCreditSales / prevSalesBooked : 0m);
    }
}
