using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class PostingPeriodTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MonthOf_ReturnsFirstAndLastDay()
    {
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), PostingPeriod.MonthOf(new DateOnly(2026, 2, 14)));
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), PostingPeriod.MonthOf(new DateOnly(2028, 2, 1)));
    }

    [Fact]
    public void Open_CoversTheMonthAndIsOpen()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 17));

        Assert.Equal(new DateOnly(2026, 6, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 6, 30), period.EndDate);
        Assert.Equal(PostingPeriodState.Open, period.State);
        Assert.Null(period.ClosedAt);
    }

    [Fact]
    public void Close_AfterMonthEnd_RecordsActorAndTime()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 1));

        var closed = period.Close(Actor, Now, new DateOnly(2026, 7, 1), "  June books final  ");

        Assert.False(closed.IsError);
        Assert.Equal(PostingPeriodState.Closed, period.State);
        Assert.Equal(Now, period.ClosedAt);
        Assert.Equal(Actor, period.ClosedByUserId);
        Assert.Equal(PostingPeriodAction.Closed, closed.Value.Action);
        Assert.Equal("June books final", closed.Value.Reason);
        Assert.Equal(Actor, closed.Value.ActorUserId);
        Assert.Equal(Now, closed.Value.OccurredAt);
        Assert.Equal(period.Id, closed.Value.PostingPeriodId);
        Assert.Equal(Shop, closed.Value.ShopId);
    }

    [Fact]
    public void Close_BeforeMonthEnded_IsNotEnded()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 7, 1));

        var closed = period.Close(Actor, Now, new DateOnly(2026, 7, 31), "early");

        Assert.Equal("PostingPeriod.NotEnded", closed.FirstError.Code);
        Assert.Equal(PostingPeriodState.Open, period.State);
    }

    [Fact]
    public void Close_Twice_IsAlreadyClosed()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 1));
        Assert.False(period.Close(Actor, Now, new DateOnly(2026, 7, 1), "done").IsError);

        Assert.Equal("PostingPeriod.AlreadyClosed", period.Close(Actor, Now, new DateOnly(2026, 7, 1), "again").FirstError.Code);
    }

    [Fact]
    public void Reopen_OpenPeriod_IsAlreadyOpen()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 1));

        Assert.Equal("PostingPeriod.AlreadyOpen", period.Reopen(Actor, Now, "fix").FirstError.Code);
    }

    [Fact]
    public void Reopen_ClosedPeriod_OpensAndAudits()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 1));
        Assert.False(period.Close(Actor, Now, new DateOnly(2026, 7, 1), "done").IsError);

        var reopened = period.Reopen(Actor, Now.AddDays(1), "missed invoice");

        Assert.False(reopened.IsError);
        Assert.Equal(PostingPeriodState.Open, period.State);
        Assert.Null(period.ClosedAt);
        Assert.Null(period.ClosedByUserId);
        Assert.Equal(PostingPeriodAction.Reopened, reopened.Value.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankReason_IsRejected(string? reason)
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 1));

        Assert.Equal("PostingPeriod.ReasonRequired", period.Close(Actor, Now, new DateOnly(2026, 7, 1), reason!).FirstError.Code);
        Assert.Equal(PostingPeriodState.Open, period.State);
    }

    [Fact]
    public void ReasonOver500_IsRejected_ExactlyAtLimitAccepted()
    {
        var period = PostingPeriod.Open(Shop, new DateOnly(2026, 6, 1));

        Assert.Equal("PostingPeriod.ReasonRequired", period.Close(Actor, Now, new DateOnly(2026, 7, 1), new string('x', 501)).FirstError.Code);
        Assert.False(period.Close(Actor, Now, new DateOnly(2026, 7, 1), new string('x', 500)).IsError);
    }
}
