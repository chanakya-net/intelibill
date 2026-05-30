using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Queries.StreamItems;

public class StreamItemsQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private ItemCatalogStreamingService Service() => new(_userRepository, _itemRepository);

    [Fact]
    public async Task ValidateAccessAsync_WhenUserNotFound_ReturnsUserNotFoundError()
    {
        _userRepository.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await Service().ValidateAccessAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task ValidateAccessAsync_WhenCallerNotMemberOfActiveShop_ReturnsMembershipNotFoundError()
    {
        var caller = User.CreateWithEmail("user@test.com", "hash", "Test", "User");
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var result = await Service().ValidateAccessAsync(caller.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task ValidateAccessAsync_WhenCallerIsMemberOfActiveShop_ReturnsSuccess()
    {
        var caller = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, caller.Id, ShopRole.Owner, true);
        caller.AddShopMembership(membership);
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var result = await Service().ValidateAccessAsync(caller.Id, shop.Id, CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ValidateAccessAsync_WhenCallerIsManagerOfShop_ReturnsSuccess()
    {
        var caller = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, caller.Id, ShopRole.Manager, false);
        caller.AddShopMembership(membership);
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var result = await Service().ValidateAccessAsync(caller.Id, shop.Id, CancellationToken.None);

        Assert.False(result.IsError);
    }
}
