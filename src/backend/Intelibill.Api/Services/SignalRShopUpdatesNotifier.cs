using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;
using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Api.Services;

internal sealed class SignalRShopUpdatesNotifier(IAuthorizedRealtimeDispatcher<ShopUpdatesHub> dispatcher)
    : IShopUpdatesNotifier
{
    public Task NotifyShopUpdateAsync(
        string eventType,
        Guid shopId,
        IReadOnlyCollection<Guid> changedIds,
        DateTimeOffset occurredOn,
        CancellationToken cancellationToken = default)
    {
        return dispatcher.SendShopAsync(
            shopId,
            "ShopUpdated",
            new { eventType, shopId, changedIds, occurredOn },
            cancellationToken);
    }
}
