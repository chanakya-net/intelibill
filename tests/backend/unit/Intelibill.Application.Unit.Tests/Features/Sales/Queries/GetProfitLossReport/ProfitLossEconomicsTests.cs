using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetProfitLossReport;

public sealed class ProfitLossEconomicsTests
{
    private readonly Guid _shop = Guid.NewGuid();
    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _returns = Substitute.For<ISaleReturnRepository>();
    private readonly IInventoryAdjustmentRepository _adjustments = Substitute.For<IInventoryAdjustmentRepository>();
    private readonly IExpenseRepository _expenses = Substitute.For<IExpenseRepository>();
    private ProfitLossReportBuilder Builder => new(_sales,
        new ReturnAdjustmentReader(_returns, _sales, Substitute.For<ICreditNoteRepository>()),
        _adjustments, BusinessTestDoubles.RealClock(), _expenses);

    [Theory]
    [InlineData(SaleReturnCondition.Restockable)]
    [InlineData(SaleReturnCondition.Wastage)]
    public async Task FractionalReturnHistory_ConservesCostAcrossWindowsResidualAndExactVoid(SaleReturnCondition middleCondition)
    {
        var actor = Guid.NewGuid();
        var item = SaleItem.CreateGoods(_shop, Guid.NewGuid(), Guid.NewGuid(), "Fractional", "F", 1.5m, 100.05m, 200m, 200m, 0m, false, false);
        var soldAt = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
        var sale = Sale.Create(_shop, "COST", null, null, null, PaymentMethod.Cash, soldAt, 300m, 0m, 300m, 0m, [item]);
        SaleReturn Return(int month, SaleReturnCondition condition) => SaleReturn.Record(_shop, sale.Id, $"RET-{month}",
            new(2026, month, 1, 10, 0, 0, TimeSpan.Zero), new(new(2026, month, 1), "Asia/Kolkata"), actor, "Returned", 100m, 0m, 100m,
            ReturnPayoutDestination.Refund, 100m, 0m, null, null,
            [new(_shop, item.Id, .5m, condition, 100.05m, 200m, 0m, false, 100m, 100m, 100m, 0m, "Returned")]).Value;
        var first = Return(5, SaleReturnCondition.Restockable);
        var middle = Return(6, middleCondition);
        var last = Return(7, SaleReturnCondition.Restockable);
        _sales.GetByIdsAsync(_shop, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([sale]);
        _sales.GetByShopAndDateRangeAsync(_shop, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<DateOnly>(1) <= sale.BusinessDate && call.ArgAt<DateOnly>(2) >= sale.BusinessDate ? new[] { sale } : Array.Empty<Sale>());
        var history = new List<SaleReturn> { last, first, middle };
        _returns.GetPostedInRangeAsync(_shop, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(history);
        _returns.GetBySaleAsync(_shop, sale.Id, Arg.Any<CancellationToken>()).Returns(history);
        async Task<Intelibill.Application.Features.Sales.DTOs.ProfitLossEconomicsDto> Month(int month) => (await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, month, 1), new(2026, month, 28), "all", null, CancellationToken.None)).Summary.Economics!;
        Assert.Equal(-50.03m, (await Month(5)).Cogs);
        var june = await Month(6);
        Assert.Equal(middleCondition == SaleReturnCondition.Restockable ? -50.02m : 0m, june.Cogs);
        Assert.Equal(middleCondition == SaleReturnCondition.Wastage ? 50.02m : 0m, june.ReturnedGoodsWastageIncludedInCogs);
        Assert.Equal(-50.03m, (await Month(7)).Cogs);
        var full = await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 4, 1), new(2026, 7, 28), "all", null, CancellationToken.None);
        Assert.Equal(middleCondition == SaleReturnCondition.Restockable ? 0m : 50.02m, full.Summary.Economics!.Cogs);
        middle.Void(new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero), new(2026, 8, 1), actor, "Undo middle return");
        var august = await Month(8);
        Assert.Equal(-june.Cogs, august.Cogs);
        Assert.Equal(-june.ReturnedGoodsWastageIncludedInCogs, august.ReturnedGoodsWastageIncludedInCogs);
        history.Add(Return(9, SaleReturnCondition.Restockable));
        Assert.Equal(-50.02m, (await Month(9)).Cogs);
        Assert.Equal(june.Cogs, (await Month(6)).Cogs);
        Assert.Equal(0m, (await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 4, 1), new(2026, 9, 28), "all", null, CancellationToken.None)).Summary.Economics!.Cogs);
    }

    [Theory]
    [InlineData("300")]
    [InlineData("150.08")]
    [InlineData("149.92")]
    public async Task NumericSearch_MatchesDisplayedNetCostAndProfit(string search)
    {
        var item = SaleItem.CreateGoods(_shop, Guid.NewGuid(), Guid.NewGuid(), "Fractional", "FR", 1.5m, 100.05m, 200m, 250m, 18m, false, false);
        var sale = Sale.Create(_shop, "SEARCH", null, "Buyer", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 354m, 0m, 354m, 54m, [item]);
        _sales.GetByShopAndDateRangeAsync(_shop, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([sale]);
        var report = await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 6, 1), new(2026, 6, 30), "all", search, CancellationToken.None);
        Assert.Single(report.Items);
        Assert.Equal("FilteredActivity", report.Summary.Economics!.OperatingProfitStatus);
        Assert.Null(report.Summary.Economics.OperatingExpenses);
        Assert.Null(report.Summary.Economics.OperatingProfit);
    }

    [Fact]
    public async Task StockLossAndLaterVoid_EachBelongToTheirOwnIstMonth()
    {
        var actor = Guid.NewGuid();
        // June30 23:59 IST; reversal July1 00:00 IST.
        var loss = InventoryAdjustment.Create(_shop, Guid.NewGuid(), Guid.NewGuid(), "LOSS", InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged, 1m, 10m, 10m, 2m, 1m, 2m, 1m,
            DateTimeOffset.Parse("2026-06-30T18:29:59Z", System.Globalization.CultureInfo.InvariantCulture), actor, null, actor).Value;
        loss.Void(DateTimeOffset.Parse("2026-06-30T18:30:00Z", System.Globalization.CultureInfo.InvariantCulture), actor, "Wrong count", Guid.NewGuid());
        _adjustments.GetPostedInRangeAsync(_shop, Arg.Any<UtcDateWindow>(), Arg.Any<CancellationToken>()).Returns([loss]);
        var june = await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 6, 1), new(2026, 6, 30), "all", null, CancellationToken.None);
        var july = await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 7, 1), new(2026, 7, 31), "all", null, CancellationToken.None);
        Assert.Equal(10m, june.Summary.Economics!.StockLoss);
        Assert.Equal(-10m, july.Summary.Economics!.StockLoss);
        Assert.False(Assert.Single(june.Items).IsReversal);
        Assert.True(Assert.Single(july.Items).IsReversal);
        Assert.Equal(10m, july.Summary.Economics.GrossTradingProfit);
    }

    [Theory]
    [InlineData("sale", null)]
    [InlineData("all", "INV")]
    public async Task FilteredActivity_DoesNotChargeWholeShopExpenses(string type, string? search)
    {
        _expenses.GetByShopAndDateRangeAsync(_shop, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([
            Expense.Create(_shop, Guid.NewGuid(), 20m, "Rent", null, new(2026, 6, 1), Guid.NewGuid(), ExpenseTreatment.Operating)]);
        var report = await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 6, 1), new(2026, 6, 30), type, search, CancellationToken.None);
        Assert.Null(report.Summary.Economics!.OperatingExpenses);
        Assert.Null(report.Summary.Economics.OperatingProfit);
        Assert.Equal("FilteredActivity", report.Summary.Economics.OperatingProfitStatus);
    }

    [Fact]
    public async Task EmptyPeriod_ShowsRealZeroAndUndefinedRatios()
    {
        var report = await Builder.BuildAsync(_shop, "Asia/Kolkata", new(2026, 6, 1), new(2026, 6, 30), "all", null, CancellationToken.None);
        Assert.Equal(0m, report.Summary.Economics!.OperatingProfit);
        Assert.Null(report.Summary.Economics.GrossMarginPercent);
        Assert.Null(report.Summary.Economics.MarkupPercent);
    }
}
