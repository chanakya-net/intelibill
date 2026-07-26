using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Hubs;

[Authorize]
public sealed class ProductHub : Hub
{
    private const string ActiveShopClaim = "active_shop_id";

    internal static string ActiveShopGroupName(Guid shopId) => $"shop:{shopId}";

    public override async Task OnConnectedAsync()
    {
        // Read the claim from Context.User rather than ICurrentSessionContext:
        // IHttpContextAccessor is not guaranteed to flow into hub callbacks, and
        // a null there would abort every connection.
        var claim = Context.User?.FindFirst(ActiveShopClaim)?.Value;
        if (!Guid.TryParse(claim, out var shopId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ActiveShopGroupName(shopId));

        await base.OnConnectedAsync();
    }
}
