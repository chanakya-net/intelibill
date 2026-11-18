using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;
namespace Intelibill.Application.Features.Inventory.Events;
public sealed class InventoryStockChangedDomainHandler(IShopUpdatesNotifier notifier)
{
    [LicenceAction(LicenceAction.InternalService)]
    public Task HandleAsync(InventoryStockChangedDomainEvent @event,CancellationToken cancellationToken) =>
        notifier.NotifyShopUpdateAsync("InventoryStockChanged",@event.ShopId,@event.ItemIds,@event.OccurredOn,cancellationToken);
}
