using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;

namespace Intelibill.Api.Unit.Tests.Hubs;

public class ShopUpdatesHubTests : RealtimeHubContractTests<ShopUpdatesHub>
{
    protected override ShopUpdatesHub CreateHub(RealtimeConnectionGate gate) => new(gate);

    protected override string ExpectedHubName => nameof(ShopUpdatesHub);
}
