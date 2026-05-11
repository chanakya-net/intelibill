using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;

namespace Intelibill.Application.Features.Inventory.Events;

public sealed class InventoryBatchPricingChangedDomainHandler(IShopUpdatesNotifier notifier)
{
    public Task HandleAsync(InventoryBatchPricingChangedDomainEvent @event, CancellationToken cancellationToken)
    {
        return notifier.NotifyShopUpdateAsync(
            "InventoryBatchPricingChanged",
            @event.ShopId,
            [@event.ItemId],
            @event.OccurredOn,
            cancellationToken);
    }
}
