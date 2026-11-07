using ErrorOr;
using Intelibill.Domain.Interfaces.Repositories;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Services;

internal sealed class InventoryUpdater(
    IInventoryRepository inventoryRepository) : IInventoryUpdater
{
    public async Task<ErrorOr<DomainInventory>> GetOrUpdateAsync(
        Guid shopId,
        Guid itemId,
        decimal quantityToAdd,
        Guid actorUserId,
        InventoryUpdateContext context,
        CancellationToken cancellationToken)
    {
        if (!context.ByItemId.TryGetValue(itemId, out var inventory))
        {
            inventory = await inventoryRepository.GetByItemAsync(shopId, itemId, cancellationToken);
            if (inventory is not null)
                context.ByItemId[itemId] = inventory;
        }

        if (inventory is null)
        {
            var inventoryResult = DomainInventory.Create(
                shopId,
                itemId,
                quantityToAdd,
                reorderLevel: 0,
                maxLevel: 0,
                createdBy: actorUserId);

            if (inventoryResult.IsError)
                return inventoryResult.Errors;

            inventory = inventoryResult.Value;
            await inventoryRepository.AddAsync(inventory, cancellationToken);
            context.AddedItemIds.Add(itemId);
            context.ByItemId[itemId] = inventory;
        }
        else
        {
            var addResult = inventory.AddQuantity(quantityToAdd, actorUserId);
            if (addResult.IsError)
                return addResult.Errors;

            // A later lot can mutate the same unsaved aggregate. Keep its Added state so the
            // receipt's single save inserts the accumulated quantity rather than updating no row.
            if (!context.AddedItemIds.Contains(itemId))
                inventoryRepository.Update(inventory);
        }

        return inventory;
    }
}
