using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops;

internal sealed class ShopSetupAccessFixture
{
    public IShopRepository Shops { get; } = Substitute.For<IShopRepository>();
    public IMembershipAuthorityRepository Authority { get; } = Substitute.For<IMembershipAuthorityRepository>();
    public ILiveSessionAuthority Sessions { get; } = Substitute.For<ILiveSessionAuthority>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public ITenantWriteScope Scope { get; } = Substitute.For<ITenantWriteScope>();
    public ShopSetupAccess Access { get; }

    public ShopSetupAccessFixture()
    {
        var fence = Substitute.For<IShopLicenceFence>();
        fence.EnterAsync(Arg.Any<Guid>(), Arg.Any<LicenceAction>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<ErrorOr<ShopLicenceLease>>(
                new ShopLicenceLease(call.Arg<Guid>(), null, null, null, new(LicenceOutcome.Allowed, false))));
        Access = new(UnitOfWork, Shops, Authority, Sessions, Scope, fence);
    }

    public SessionClaims Session(Guid user, Guid? shop = null, Guid? sessionId = null)
    {
        var claims = new SessionClaims(user, sessionId ?? Guid.NewGuid(), 0, 0, shop, "Owner");
        Sessions.IsValidAsync(claims, Arg.Any<CancellationToken>()).Returns(true);
        return claims;
    }

    public SessionClaims Member(Guid shopId, User user, ShopRole role, Shop? shop = null)
    {
        var membership = ShopMembership.Create(shopId, user.Id, role, true);
        user.AddShopMembership(membership);
        Shops.GetActiveRoleAsync(user.Id, shopId, Arg.Any<CancellationToken>()).Returns(role);
        Shops.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop);
        Shops.GetCurrentForSetupAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop);
        Authority.LockShopAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop is not null);
        Authority.LockMembershipAsync(shopId, user.Id, Arg.Any<CancellationToken>()).Returns(membership);
        return Session(user.Id, shopId);
    }

    public SessionClaims Owner(Guid shopId)
    {
        var user = User.CreateWithEmail("bank@example.test", "hash", "Bank", "Owner");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        return Member(shopId, user, ShopRole.Owner, shop);
    }
}
