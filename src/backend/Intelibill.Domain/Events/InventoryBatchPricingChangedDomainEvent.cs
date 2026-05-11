using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record InventoryBatchPricingChangedDomainEvent(
    Guid ShopId,
    Guid ItemId,
    Guid BatchId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
