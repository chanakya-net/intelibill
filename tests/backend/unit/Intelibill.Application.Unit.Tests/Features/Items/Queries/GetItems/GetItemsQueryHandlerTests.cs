using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Queries.GetItems;

public class GetItemsQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    [Fact]
    public async Task HandleAsync_WhenCallerNotInActiveShop_ReturnsForbidden()
    {
        var caller = User.CreateWithEmail("member@test.com", "hash", "Member", "One");
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var handler = new GetItemsQueryHandler(_userRepository, _itemRepository);
        var result = await handler.HandleAsync(new GetItemsQuery(caller.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReturnsItemsForActiveShop()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var managerMembership = ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false);
        shop.AddMembership(ownerMembership);
        shop.AddMembership(managerMembership);
        manager.AddShopMembership(managerMembership);

        _userRepository.GetByIdWithDetailsAsync(manager.Id, Arg.Any<CancellationToken>()).Returns(manager);

        _itemRepository.GetByShopIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([
            Item.Create(shop.Id, "Milk", null, "ltr", "B001", true, null, owner.Id),
            Item.Create(shop.Id, "Rice", "Premium", "kg", "B002", true, null, owner.Id),
        ]);

        var handler = new GetItemsQueryHandler(_userRepository, _itemRepository);
        var result = await handler.HandleAsync(new GetItemsQuery(manager.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Milk", result.Value[0].Name);
        Assert.Equal("Rice", result.Value[1].Name);
    }
}
