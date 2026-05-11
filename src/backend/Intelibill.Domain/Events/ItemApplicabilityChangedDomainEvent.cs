using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record ItemApplicabilityChangedDomainEvent(
    Guid ShopId,
    Guid ItemId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
