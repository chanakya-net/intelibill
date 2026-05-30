using ErrorOr;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Services;

public sealed class InventoryUpdateContext
{
    internal Dictionary<Guid, DomainInventory> ByItemId { get; } = new();
}

public interface IInventoryUpdater
{
    Task<ErrorOr<DomainInventory>> GetOrUpdateAsync(
        Guid shopId,
        Guid itemId,
        decimal quantityToAdd,
        Guid actorUserId,
        InventoryUpdateContext context,
        CancellationToken cancellationToken);
}
