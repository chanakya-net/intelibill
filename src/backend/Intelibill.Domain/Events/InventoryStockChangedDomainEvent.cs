using Intelibill.Domain.Common;
namespace Intelibill.Domain.Events;
/// <summary>Idempotent invalidation of current item stock, published only after a committed operation.</summary>
public sealed record InventoryStockChangedDomainEvent(Guid ShopId, IReadOnlyCollection<Guid> ItemIds) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
