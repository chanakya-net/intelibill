using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;

namespace Intelibill.Application.Features.Inventory.Events;

public sealed class InventoryBatchVoidedDomainHandler(IShopUpdatesNotifier notifier)
{
    public Task HandleAsync(InventoryBatchVoidedDomainEvent @event, CancellationToken cancellationToken)
    {
        return notifier.NotifyShopUpdateAsync(
            "InventoryBatchVoided",
            @event.ShopId,
            [@event.ItemId],
            @event.OccurredOn,
            cancellationToken);
    }
}
