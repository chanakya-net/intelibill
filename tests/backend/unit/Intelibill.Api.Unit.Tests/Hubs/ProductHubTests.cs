using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;

namespace Intelibill.Api.Unit.Tests.Hubs;

public class ProductHubTests : RealtimeHubContractTests<ProductHub>
{
    protected override ProductHub CreateHub(RealtimeConnectionGate gate) => new(gate);

    protected override string ExpectedHubName => nameof(ProductHub);
}
