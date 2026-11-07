using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One calendar month of a shop's books. While <see cref="PostingPeriodState.Closed"/>, nothing may
/// post a business date inside it.
/// </summary>
/// <remarks>
/// A month has no row until something touches it; a missing row means open. The row is created on
/// first use and then locked, so "no row" is never a way around a close. Every transition appends a
/// <see cref="PostingPeriodEvent"/> and the row carries PostgreSQL's <c>xmin</c> as its version.
/// </remarks>
public sealed class PostingPeriod : BaseEntity
{
    public const int MaxReasonLength = 500;

    public Guid ShopId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public PostingPeriodState State { get; private set; } = PostingPeriodState.Open;
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedByUserId { get; private set; }

    public uint Version { get; private set; }

    private PostingPeriod() { }

    public static (DateOnly Start, DateOnly End) MonthOf(DateOnly date)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    public static PostingPeriod Open(Guid shopId, DateOnly anyDateInMonth)
    {
        var (start, end) = MonthOf(anyDateInMonth);
        return new PostingPeriod { ShopId = shopId, StartDate = start, EndDate = end };
    }

    public ErrorOr<PostingPeriodEvent> Close(Guid actorUserId, DateTimeOffset now, DateOnly today, string reason)
    {
        var trimmed = NormalizeReason(reason);
        if (trimmed is null)
            return Errors.PostingPeriod.ReasonRequired;

        if (State == PostingPeriodState.Closed)
            return Errors.PostingPeriod.AlreadyClosed;

        if (EndDate >= today)
            return Errors.PostingPeriod.NotEnded;

        State = PostingPeriodState.Closed;
        ClosedAt = now;
        ClosedByUserId = actorUserId;
        return PostingPeriodEvent.Record(this, PostingPeriodAction.Closed, trimmed, actorUserId, now);
    }

    public ErrorOr<PostingPeriodEvent> Reopen(Guid actorUserId, DateTimeOffset now, string reason)
    {
        var trimmed = NormalizeReason(reason);
        if (trimmed is null)
            return Errors.PostingPeriod.ReasonRequired;

        if (State == PostingPeriodState.Open)
            return Errors.PostingPeriod.AlreadyOpen;

        State = PostingPeriodState.Open;
        ClosedAt = null;
        ClosedByUserId = null;
        return PostingPeriodEvent.Record(this, PostingPeriodAction.Reopened, trimmed, actorUserId, now);
    }

    private static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxReasonLength ? null : trimmed;
    }
}
