using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;
namespace Intelibill.Application.Features.Inventory.Events;
public sealed class InventoryStockChangedDomainHandler(IShopUpdatesNotifier notifier)
{
    public Task HandleAsync(InventoryStockChangedDomainEvent @event,CancellationToken cancellationToken) =>
        notifier.NotifyShopUpdateAsync("InventoryStockChanged",@event.ShopId,@event.ItemIds,@event.OccurredOn,cancellationToken);
}
