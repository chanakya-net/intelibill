using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;

namespace Intelibill.Application.Features.Items.Events;

public sealed class ItemCreatedDomainHandler(IProductHubNotifier hubNotifier)
{
    public async Task HandleAsync(ItemCreatedDomainEvent @event, CancellationToken cancellationToken)
    {
        await hubNotifier.NotifyProductCreatedAsync(
            @event.ItemId,
            @event.Barcode,
            @event.Name,
            @event.ShopId,
            cancellationToken);
    }
}
