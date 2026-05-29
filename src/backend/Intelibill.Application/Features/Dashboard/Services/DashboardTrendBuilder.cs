using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Dashboard.Services;

internal static class DashboardTrendBuilder
{
    internal static (List<SalesTrendPointDto> SalesTrend, List<ProfitTrendPointDto> ProfitTrend, List<PaymentMixTrendPointDto> PaymentMixTrend) BuildTrendSeries(
        IReadOnlyCollection<Sale> sales,
        IReadOnlyCollection<SaleReturn>? saleReturns,
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyCollection<InventoryAdjustment>? adjustmentLosses = null)
    {
        static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(value.UtcDateTime);

        var activeReturns = GetActiveReturns(saleReturns);
        var saleItemTypes = sales.SelectMany(s => s.Items).ToDictionary(i => i.Id, i => i.LineType);
        var byDay = sales
            .GroupBy(s => LocalDate(s.SoldAt))
            .ToDictionary(
                g => g.Key,
                g => (
                    SalesBooked: g.Sum(s => s.TotalAmount),
                    Cost: g.SelectMany(s => s.Items).Where(i => i.LineType != SaleLineType.Service).Sum(i => i.CostPrice * i.Quantity),
                    Tax: g.Sum(s => s.TotalTaxAmount),
                    PaymentMix: SalesKpiCalculator.CalculatePaymentMix(g.ToList())));
        var returnsByDay = activeReturns
            .GroupBy(r => LocalDate(r.ProcessedAt))
            .ToDictionary(
                g => g.Key,
                g => (
                    Refund: g.Sum(r => r.TotalRefundAmount),
                    RefundTax: CalculateApprovedRefundTax(g),
                    RestockableCost: g.SelectMany(r => r.Items)
                        .Where(i => i.Condition == SaleReturnCondition.Restockable)
                        .Sum(i =>
                        {
                            var isService = saleItemTypes.GetValueOrDefault(i.SaleItemId) == SaleLineType.Service;
                            return isService ? 0m : i.OriginalCostPrice * i.Quantity;
                        })));
        var adjustmentLossByDay = GetActiveDecreaseAdjustments(adjustmentLosses)
            .GroupBy(a => LocalDate(a.PerformedAt))
            .ToDictionary(g => g.Key, g => g.Sum(a => a.CostImpact));

        var salesTrend = new List<SalesTrendPointDto>();
        var profitTrend = new List<ProfitTrendPointDto>();
        var paymentMixTrend = new List<PaymentMixTrendPointDto>();

        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            var dayData = byDay.TryGetValue(day, out var foundDayData)
                ? foundDayData
                : (SalesBooked: 0m, Cost: 0m, Tax: 0m, PaymentMix: new PaymentMixDto(0m, 0m, 0m, 0m));
            var returnData = returnsByDay.TryGetValue(day, out var foundReturnData)
                ? foundReturnData
                : (Refund: 0m, RefundTax: 0m, RestockableCost: 0m);
            var netSalesBooked = dayData.SalesBooked - returnData.Refund;
            var netCost = dayData.Cost - returnData.RestockableCost;
            var netTax = dayData.Tax - returnData.RefundTax;
            var adjustmentLoss = adjustmentLossByDay.GetValueOrDefault(day, 0m);
            salesTrend.Add(new SalesTrendPointDto(Date: day, Amount: dayData.SalesBooked, NetAmount: netSalesBooked));
            profitTrend.Add(new ProfitTrendPointDto(
                Date: day,
                ProfitBeforeTax: netSalesBooked - netCost - adjustmentLoss,
                ProfitAfterTax: netSalesBooked - netTax - netCost - adjustmentLoss));
            paymentMixTrend.Add(new PaymentMixTrendPointDto(
                Date: day, Cash: dayData.PaymentMix.Cash, Upi: dayData.PaymentMix.Upi,
                Card: dayData.PaymentMix.Card, Credit: dayData.PaymentMix.Credit));
        }

        return (salesTrend, profitTrend, paymentMixTrend);
    }

    internal static PreviousPeriodSummaryDto BuildPreviousPeriodSummary(
        IReadOnlyCollection<Sale> prevSales,
        IReadOnlyCollection<SaleReturn>? prevSaleReturns,
        IReadOnlyCollection<Expense> prevExpenses,
        DateOnly prevStartDate,
        DateOnly prevEndDate,
        IReadOnlyCollection<InventoryAdjustment>? prevAdjustmentLosses = null)
    {
        var salesKpis = SalesKpiCalculator.CalculateSalesKpis(prevSales, prevSaleReturns, prevAdjustmentLosses);
        var prevCreditSales = SalesKpiCalculator.CalculatePaymentMix(prevSales).Credit;
        var prevExpenseRecorded = prevExpenses.Where(e => e.OriginalExpenseId is null).Sum(e => e.Amount);
        var prevExpenseCorrection = prevExpenses.Where(e => e.OriginalExpenseId is not null).Sum(e => e.Amount);

        return new PreviousPeriodSummaryDto(
            StartDate: prevStartDate,
            EndDate: prevEndDate,
            SalesCount: prevSales.Count,
            SalesBooked: salesKpis.SalesBooked,
            NetSalesBooked: salesKpis.NetSalesBooked,
            ProfitAfterTax: salesKpis.ProfitAfterTax,
            NetExpense: prevExpenseRecorded + prevExpenseCorrection,
            CreditSalesPercentage: salesKpis.SalesBooked > 0 ? prevCreditSales / salesKpis.SalesBooked : 0m);
    }

    private static List<SaleReturn> GetActiveReturns(IReadOnlyCollection<SaleReturn>? saleReturns) =>
        saleReturns?.Where(r => !r.IsVoided).ToList() ?? [];

    private static decimal CalculateActiveDecreaseAdjustmentLoss(IReadOnlyCollection<InventoryAdjustment>? adjustments) =>
        GetActiveDecreaseAdjustments(adjustments).Sum(a => a.CostImpact);

    private static IEnumerable<InventoryAdjustment> GetActiveDecreaseAdjustments(IReadOnlyCollection<InventoryAdjustment>? adjustments) =>
        adjustments?.Where(a => a.Direction == InventoryAdjustmentDirection.Decrease && !a.IsVoided) ?? [];

    private static decimal CalculateApprovedRefundTax(IEnumerable<SaleReturn> saleReturns) =>
        saleReturns.SelectMany(r => r.Items).Sum(CalculateApprovedRefundTax);

    private static decimal CalculateApprovedRefundTax(SaleReturnItem item)
    {
        if (item.ApprovedRefundAmount <= 0m || item.MaxRefundAmount <= 0m || item.TaxAmount <= 0m)
        {
            return 0m;
        }

        var tax = item.ApprovedRefundAmount * item.TaxAmount / item.MaxRefundAmount;
        return Math.Round(tax, 2, MidpointRounding.AwayFromZero);
    }
}
