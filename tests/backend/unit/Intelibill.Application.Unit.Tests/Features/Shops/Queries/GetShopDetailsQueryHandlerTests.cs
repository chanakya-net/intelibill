using ErrorOr;
using Intelibill.Application.Features.Shops.Queries.GetShopDetails;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Queries;

public class GetShopDetailsQueryHandlerTests
{
    private readonly ShopSetupAccessFixture _setup = new();

    private GetShopDetailsQueryHandler Handler() => new(_setup.Access, _setup.Shops, Substitute.For<IBankAccountRepository>());

    [Fact]
    public async Task HandleAsync_WhenUserNotFound_ReturnsError()
    {

        var result = await Handler().HandleAsync(new GetShopDetailsQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipNotFound_ReturnsError()
    {
        var user = User.CreateWithEmail("user@test.com", "pass", "Test", "User");

        var result = await Handler().HandleAsync(new GetShopDetailsQuery(user.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_WhenShopNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "Test", "User");
        var membership = ShopMembership.Create(shopId, userId, ShopRole.Owner, false);
        user.AddShopMembership(membership);

        _setup.Shops.GetActiveRoleAsync(userId, shopId, Arg.Any<CancellationToken>()).Returns(ShopRole.Owner);

        var result = await Handler().HandleAsync(new GetShopDetailsQuery(userId, shopId), CancellationToken.None);

        Assert.True(result.IsError);
    }
}
