using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>One close or reopening of a <see cref="PostingPeriod"/>: who, when and why. Never edited.</summary>
public sealed class PostingPeriodEvent : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid PostingPeriodId { get; private set; }
    public PostingPeriodAction Action { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private PostingPeriodEvent() { }

    internal static PostingPeriodEvent Record(
        PostingPeriod period,
        PostingPeriodAction action,
        string reason,
        Guid actorUserId,
        DateTimeOffset occurredAt) =>
        new()
        {
            ShopId = period.ShopId,
            PostingPeriodId = period.Id,
            Action = action,
            Reason = reason,
            ActorUserId = actorUserId,
            OccurredAt = occurredAt,
        };
}
