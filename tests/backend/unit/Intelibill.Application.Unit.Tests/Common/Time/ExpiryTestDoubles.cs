using ErrorOr;
using Intelibill.Application.Features.Expiry;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Common.Time;

internal static class ExpiryTestDoubles
{
    public static IExpiryPolicyFence Fence()
    {
        var fence = Substitute.For<IExpiryPolicyFence>();
        fence.AcquireAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        return fence;
    }
    private static IExpiryPolicyRepository Policies()
    {
        var policies = Substitute.For<IExpiryPolicyRepository>();
        policies.GetEffectiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(new EffectiveExpiryPolicy(ExpiryPolicy.Default, 1, true));
        policies.GetEffectiveForItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<Guid, EffectiveExpiryPolicy>>(call.ArgAt<IReadOnlyList<Guid>>(1).Distinct().ToDictionary(id => id, _ => new EffectiveExpiryPolicy(ExpiryPolicy.Default, 1, true))));
        policies.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => ShopExpiryState.Create(call.Arg<Guid>(), DateTimeOffset.UtcNow));
        return policies;
    }
    private static IShopRepository Shops()
    {
        var shops = Substitute.For<IShopRepository>();
        shops.GetActiveRoleAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ShopRole.Owner);
        return shops;
    }
    public static SaleExpiryGuard Guard() => new(Policies(), Shops(), BusinessTestDoubles.RealClock());
    public static BatchExpiryRevisionService Dates() => new(Policies(), Substitute.For<IBatchExpiryRevisionRepository>(), Shops(), BusinessTestDoubles.RealClock());
    public static IUnitOfWork UnitOfWork() => Substitute.For<IUnitOfWork>();
}
