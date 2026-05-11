using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record DiscountRuleChangedDomainEvent(
    Guid ShopId,
    IReadOnlyCollection<Guid> DiscountRuleIds) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
