using Intelibill.Application.Common.Interfaces;
using Intelibill.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Services;

internal sealed class SignalRProductHubNotifier(IHubContext<ProductHub> hubContext) : IProductHubNotifier
{
    public Task NotifyProductCreatedAsync(
        Guid itemId,
        string barcode,
        string name,
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Group(ProductHub.ActiveShopGroupName(shopId)).SendAsync(
            "ProductAdded",
            new { itemId, barcode, name, shopId },
            cancellationToken);
    }
}
