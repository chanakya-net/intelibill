using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record ItemCreatedDomainEvent(
    Guid ItemId,
    string Barcode,
    string Name,
    Guid ShopId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
