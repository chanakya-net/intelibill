using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class TillAuditTests
{
    [Fact]
    public void TillName_IsNormalizedAndInvalidReferencesRejected()
    {
        var shop = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var till = Till.Create(shop, "  Front desk  ", actor, DateTimeOffset.UtcNow).Value;
        Assert.Equal("FRONT DESK", till.NormalizedName);
        Assert.Equal("Front desk", till.Name);
        Assert.True(Till.Create(Guid.Empty, "desk", actor, DateTimeOffset.UtcNow).IsError);
        Assert.True(Till.Create(shop, " ", actor, DateTimeOffset.UtcNow).IsError);
    }

    [Fact]
    public void Review_FreezesReasonAndCannotChangeCount()
    {
        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), actor, "desk", "Cashier", actor, now,
            new DateOnly(2026, 11, 8), "Asia/Kolkata", 100m, null).Value;
        var close = session.Close(TillTotals.From([], []), 90m, actor, now, session.BusinessDate, "short", true, false).Value;
        Assert.True(TillCloseReview.Record(close, actor, now, " ").IsError);
        var review = TillCloseReview.Record(close, Guid.NewGuid(), now, "  reviewed shortage  ").Value;
        Assert.Equal("reviewed shortage", review.Reason);
        Assert.Equal(-10m, close.Variance);
        Assert.Equal(90m, close.CountedCash);
        Assert.Equal(close.Id, review.CloseId);
    }

    [Fact]
    public void LatePosting_IsSupplementalAndUnknownEvidenceNeverBecomesCash()
    {
        var now = DateTimeOffset.UtcNow;
        var late = TillLatePosting.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
            new DateOnly(2026, 11, 7), now.AddDays(-1), now, Guid.NewGuid(), [], "Unknown", "legacy tender unavailable");
        Assert.False(late.IsError);
        Assert.False(late.Value.IncludedInCount);
        Assert.Empty(late.Value.TenderLines);
        Assert.True(TillLatePosting.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
            new DateOnly(2026, 11, 7), now.AddDays(-1), now, Guid.NewGuid(), [], "Recorded", "missing session and tender").IsError);
    }
}
