using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Hubs;

[Authorize]
public sealed class ShopUpdatesHub(ICurrentSessionContext currentSessionContext) : Hub
{
    internal static string ActiveShopGroupName(Guid shopId) => $"shop:{shopId}";

    public override async Task OnConnectedAsync()
    {
        var shopId = currentSessionContext.ActiveShopId;
        if (!shopId.HasValue)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ActiveShopGroupName(shopId.Value));

        await base.OnConnectedAsync();
    }
}
