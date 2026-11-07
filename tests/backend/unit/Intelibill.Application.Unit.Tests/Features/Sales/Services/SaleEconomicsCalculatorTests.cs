using Intelibill.Application.Features.Sales.Services.Pricing;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class SaleEconomicsCalculatorTests
{
    [Theory]
    [InlineData(200, 100, 100, 50d, 100d)]
    [InlineData(0, 0, 0, null, null)]
    [InlineData(100, 0, 100, 100d, null)]
    [InlineData(-200, -100, -100, 50d, 100d)]
    public void Calculate_UsesRevenueForMarginAndCostForMarkup(decimal revenue, decimal cost, decimal profit, double? margin, double? markup)
    {
        var result = SaleEconomicsCalculator.Calculate(revenue, cost);
        Assert.Equal(profit, result.GrossProfit);
        Assert.Equal(margin.HasValue ? (decimal?)margin.Value : null, result.GrossMarginPercent);
        Assert.Equal(markup.HasValue ? (decimal?)markup.Value : null, result.MarkupPercent);
    }

    [Fact]
    public void RecordedCost_RoundsFractionalQuantityOnce() => Assert.Equal(33.30m, SaleEconomicsCalculator.RecordedCost(.333m, 100m));
}
