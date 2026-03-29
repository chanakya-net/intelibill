using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.Queries.GetShopUsers;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Queries.GetShopUsers;

public class GetShopUsersQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();

    [Fact]
    public async Task HandleAsync_WhenCallerNotInShop_ReturnsForbidden()
    {
        var caller = User.CreateWithEmail("caller@test.com", "hash", "Caller", "One");
        var query = new GetShopUsersQuery(caller.Id, Guid.NewGuid());

        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var handler = new GetShopUsersQueryHandler(_userRepository, _shopRepository);
        var result = await handler.HandleAsync(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReturnsOrderedUsers()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var staff = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");

        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var managerMembership = ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false);
        var staffMembership = ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false);

        shop.AddMembership(ownerMembership);
        shop.AddMembership(managerMembership);
        shop.AddMembership(staffMembership);

        owner.AddShopMembership(ownerMembership);
        manager.AddShopMembership(managerMembership);
        staff.AddShopMembership(staffMembership);

        var query = new GetShopUsersQuery(owner.Id, shop.Id);

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _shopRepository.GetByIdWithMembersAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);

        var handler = new GetShopUsersQueryHandler(_userRepository, _shopRepository);
        var result = await handler.HandleAsync(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("Owner", result.Value[0].Role);
        Assert.Equal("Manager", result.Value[1].Role);
        Assert.Equal("SalesPerson", result.Value[2].Role);
    }
}
