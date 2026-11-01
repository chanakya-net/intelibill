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
        return hubContext.Clients.Group(GroupName(shopId)).SendAsync(
            "ProductAdded",
            new { itemId, barcode, name, shopId },
            cancellationToken);
    }

    private static string GroupName(Guid shopId) => $"shop:{shopId}";
}
