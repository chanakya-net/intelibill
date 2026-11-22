using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders;

internal static class PurchaseOrderAccessTestDoubles
{
    public static SessionClaims Session(Guid user, Guid shop) => new(user, Guid.NewGuid(), 0, 0, shop, nameof(ShopRole.Owner));

    public static StockIntakeAccess TradingAccess(IUserRepository users) =>
        new(ShopRoles(users), ValidSessions(), Fence());

    public static PurchaseOrderTrading Trading(IUserRepository users, IUnitOfWork transaction) =>
        new(TradingAccess(users), transaction);

    public static PurchaseOrderHistory History(IUserRepository users) =>
        new(Substitute.For<IUnitOfWork>(), ShopRoles(users), ValidSessions(), Fence());

    private static IShopLicenceFence Fence()
    {
        var fence = Substitute.For<IShopLicenceFence>();
        fence.EnterAsync(Arg.Any<Guid>(), Arg.Any<LicenceAction>(), Arg.Any<CancellationToken>())
            .Returns(call => new ShopLicenceLease(call.Arg<Guid>(), null, null, null,
                new LicenceDecision(LicenceOutcome.Allowed, false)));
        return fence;
    }

    private static ILiveSessionAuthority ValidSessions()
    {
        var sessions = Substitute.For<ILiveSessionAuthority>();
        sessions.IsValidAsync(Arg.Any<SessionClaims>(), Arg.Any<CancellationToken>()).Returns(true);
        return sessions;
    }

    private static IShopRepository ShopRoles(IUserRepository users)
    {
        var shops = Substitute.For<IShopRepository>();
        shops.GetActiveRoleAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var user = await users.GetByIdWithDetailsAsync(call.ArgAt<Guid>(0), call.Arg<CancellationToken>());
                return user?.ShopMemberships.SingleOrDefault(member => member.IsActive && member.ShopId == call.ArgAt<Guid>(1))?.Role;
            });
        return shops;
    }
}
