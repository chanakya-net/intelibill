using Intelibill.Api.Hubs;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Services;

internal sealed class SignalRShopUpdatesNotifier(IHubContext<ShopUpdatesHub> hubContext) : IShopUpdatesNotifier
{
    public Task NotifyShopUpdateAsync(
        string eventType,
        Guid shopId,
        IReadOnlyCollection<Guid> changedIds,
        DateTimeOffset occurredOn,
        CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Group(ShopUpdatesHub.ActiveShopGroupName(shopId)).SendAsync(
            "ShopUpdated",
            new { eventType, shopId, changedIds, occurredOn },
            cancellationToken);
    }
}
