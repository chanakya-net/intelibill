using Intelibill.Application.Common.Licensing;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Common.Licensing;

internal static class LicenceTestDoubles
{
    internal static IShopLicenceFence ObserveOnly()
    {
        var fence = Substitute.For<IShopLicenceFence>();
        fence.EnterAsync(Arg.Any<Guid>(), Arg.Any<LicenceAction>(), Arg.Any<CancellationToken>())
            .Returns(call => new ShopLicenceLease(call.Arg<Guid>(), null, null, null, new(LicenceOutcome.Allowed, false)));
        return fence;
    }
}
