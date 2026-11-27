using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;

namespace Intelibill.Application.Features.Items.Events;

public sealed class ItemApplicabilityChangedDomainHandler(IShopUpdatesNotifier notifier)
{
    [LicenceAction(LicenceAction.InternalService)]
    public Task HandleAsync(ItemApplicabilityChangedDomainEvent @event, CancellationToken cancellationToken)
    {
        return notifier.NotifyShopUpdateAsync(
            "ItemApplicabilityChanged",
            @event.ShopId,
            [@event.ItemId],
            @event.OccurredOn,
            cancellationToken);
    }
}
