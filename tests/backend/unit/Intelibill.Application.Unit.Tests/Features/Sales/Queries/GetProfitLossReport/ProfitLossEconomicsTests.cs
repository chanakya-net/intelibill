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
