using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.PostingPeriods.Services;

internal static class PostingPeriodDtoMapper
{
    public static PostingPeriodDto Map(PostingPeriod period, IEnumerable<PostingPeriodEvent> events) =>
        new(
            period.StartDate,
            period.EndDate,
            period.State.ToString(),
            period.ClosedAt,
            period.ClosedByUserId,
            events
                .Where(e => e.PostingPeriodId == period.Id)
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.Action)
                .Select(e => new PostingPeriodEventDto(e.Action.ToString(), e.Reason, e.ActorUserId, e.OccurredAt))
                .ToList());

    /// <summary>A month nothing has touched: open, with no history.</summary>
    public static PostingPeriodDto Untouched(DateOnly start)
    {
        var (_, end) = PostingPeriod.MonthOf(start);
        return new PostingPeriodDto(start, end, PostingPeriodState.Open.ToString(), null, null, []);
    }
}
