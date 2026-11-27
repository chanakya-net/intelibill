using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;
using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Api.Services;

internal sealed class SignalRProductHubNotifier(IAuthorizedRealtimeDispatcher<ProductHub> dispatcher)
    : IProductHubNotifier
{
    public Task NotifyProductCreatedAsync(
        Guid itemId,
        string barcode,
        string name,
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        return dispatcher.SendShopAsync(
            shopId,
            "ProductAdded",
            new { itemId, barcode, name, shopId },
            cancellationToken);
    }
}
