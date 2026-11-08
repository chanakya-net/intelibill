using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class TillSessionTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 8);
    private static TillSession Open() => TillSession.Open(Shop, Guid.NewGuid(), Actor, "Front", "Cashier", Actor,
        Now, Today, "Asia/Kolkata", 1000m, null).Value;

    [Fact]
    public void Close_FreezesCashAndRecordedNonCashTotalsAndVariance()
    {
        var session = Open();
        var movements = new List<TillMovement>();
        foreach (var amount in new[] {500m, 200m, -100m, -50m, -300m})
            movements.Add(Append(session, amount));
        movements.Add(Append(session, 700m, ReceiptTenderMethod.Upi));
        var totals = TillTotals.From(movements, []);
        var closed = session.Close(totals, 1240m, Actor, Now.AddHours(8), Today, "short ten", true, false);
        Assert.False(closed.IsError);
        Assert.Equal(1250m, closed.Value.ExpectedCash);
        Assert.Equal(-10m, closed.Value.Variance);
        Assert.Equal(700m, closed.Value.Totals.RecordedTenderTotals[ReceiptTenderMethod.Upi]);
        Assert.True(closed.Value.AwaitingReview);
        Assert.Equal(6, closed.Value.LastMovementSequence);
        Assert.Equal(TillSessionState.Closed, session.State);
        Assert.True(session.NextSequence().IsError);
        Assert.True(session.Close(totals, 1250m, Actor, Now, Today, null, false, false).IsError);
    }

    [Fact]
    public void CashChangeAndNonDrawerSettlement_RemainSeparate()
    {
        var session = Open();
        var tender = new TenderLine(ReceiptTenderMethod.Cash, 300m, 500m, 200m, null, null);
        var source = Source(session, 20m, 50m, 100m);
        var movement = TillMovement.Record(session, source, 1, tender, 1m, Actor, Now, null).Value;
        var totals = TillTotals.From([movement], [source]);
        Assert.Equal(300m, totals.RecordedTenderTotals[ReceiptTenderMethod.Cash]);
        Assert.Equal(20m, totals.StoreCreditIssued);
        Assert.Equal(50m, totals.StoreCreditRedeemed);
        Assert.Equal(100m, totals.ReceivableChange);
    }

    [Fact]
    public void Close_RequiresVarianceReasonAndExplicitOverdueFinalization()
    {
        var session = Open();
        var totals = TillTotals.From([], []);
        Assert.True(session.Close(totals, 999m, Actor, Now, Today, null, true, false).IsError);
        Assert.True(session.Close(totals, 1000m, Actor, Now.AddDays(1), Today.AddDays(1), "late", false, false).IsError);
        var close = session.Close(totals, 1000m, Actor, Now.AddDays(1), Today.AddDays(1), "late count", false, true);
        Assert.True(close.Value.IsOverdue);
        Assert.Equal(Today, close.Value.BusinessDate);
    }

    [Fact]
    public void Close_OwnsItsFrozenTotalsEvenIfCallerChangesItsInput()
    {
        var session = Open();
        var tender = new Dictionary<ReceiptTenderMethod, decimal> { [ReceiptTenderMethod.Cash] = 0m };
        var purposes = new Dictionary<string, decimal> { ["sale"] = 0m };
        var totals = new TillTotals(tender, purposes, 0m, 0m, 0m, 0);
        var close = session.Close(totals, 1000m, Actor, Now, Today, null, false, false).Value;
        tender[ReceiptTenderMethod.Cash] = 9999m;
        purposes["sale"] = 9999m;
        Assert.Equal(0m, close.Totals.RecordedTenderTotals[ReceiptTenderMethod.Cash]);
        Assert.Equal(0m, close.Totals.PurposeTotals["sale"]);
    }

    [Fact]
    public void ActingForAnotherCashier_RequiresPersistedOverrideForCloseAndSource()
    {
        var session = Open();
        var manager = Guid.NewGuid();
        var source = TillSourceRecord.Record(session, TillMovementSource.ManualCash, Guid.NewGuid(), "cash-in", "key", manager,
            Now, Now, "float top-up", null, new());
        Assert.True(source.IsError);
        Assert.True(session.Close(TillTotals.From([], []), 1000m, manager, Now, Today, null, false, false).IsError);
    }

    private static TillSourceRecord Source(TillSession session, decimal issued = 0, decimal redeemed = 0, decimal due = 0) =>
        TillSourceRecord.Record(session, TillMovementSource.OnlineSale, Guid.NewGuid(), "sale", "operation", Actor,
            Now, Now, null, null, new TillNonCashSettlement(issued, redeemed, due)).Value;

    private static TillMovement Append(TillSession session, decimal amount, ReceiptTenderMethod method = ReceiptTenderMethod.Cash) =>
        TillMovement.Record(session, Source(session), 1, new TenderLine(method, Math.Abs(amount), null, null, null, null),
            Math.Sign(amount), Actor, Now, null).Value;
}
