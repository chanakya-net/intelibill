namespace Intelibill.Application.Features.Sales.Services.Pricing;

public sealed record EconomicLine(decimal NetRevenue, decimal EconomicCost, decimal GrossProfit, decimal? GrossMarginPercent, decimal? MarkupPercent);

/// <summary>Recorded costs are preserved: purchase-tax recoverability is not established by a tax-included flag.</summary>
public static class SaleEconomicsCalculator
{
    public static EconomicLine Calculate(decimal netRevenue, decimal recordedCost)
    {
        var revenue = Round(netRevenue);
        var cost = Round(recordedCost);
        var profit = Round(revenue - cost);
        return new(revenue, cost, profit, revenue == 0m ? null : Round(profit / revenue * 100m), cost == 0m ? null : Round(profit / cost * 100m));
    }

    public static decimal RecordedCost(decimal quantity, decimal unitCost) => Round(quantity * unitCost);
    public static decimal DiscountCapacity(decimal netBeforeDiscount, decimal recordedCost) => Math.Max(0m, Round(netBeforeDiscount) - Round(recordedCost));
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
