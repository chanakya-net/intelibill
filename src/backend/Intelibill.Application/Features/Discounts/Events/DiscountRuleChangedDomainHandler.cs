using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Events;

namespace Intelibill.Application.Features.Discounts.Events;

public sealed class DiscountRuleChangedDomainHandler(IShopUpdatesNotifier notifier)
{
    public Task HandleAsync(DiscountRuleChangedDomainEvent @event, CancellationToken cancellationToken)
    {
        return notifier.NotifyShopUpdateAsync(
            "DiscountRuleChanged",
            @event.ShopId,
            @event.DiscountRuleIds,
            @event.OccurredOn,
            cancellationToken);
    }
}
