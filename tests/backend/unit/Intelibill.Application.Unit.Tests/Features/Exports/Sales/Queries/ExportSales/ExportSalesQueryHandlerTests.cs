using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Exports.Sales.Queries.ExportSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Sales.Queries.ExportSales;

public class ExportSalesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();

    private ExportSalesQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("export@test.com", "hash", "Export", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId, ShopRole role) =>
        ShopMembership.Create(shopId, userId, role, true);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsUserNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            userId,
            shopId,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("user.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shopId,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenUserIsStaff_ReturnsForbiddenError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var staffMembership = MakeMembership(shop.Id, user.Id, ShopRole.Staff);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(staffMembership);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Export.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenUserIsOwner_PassesAuthorizationCheck()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var ownerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Owner);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ownerMembership);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        // Should not return authorization error
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenUserIsManager_PassesAuthorizationCheck()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var managerMembership = MakeMembership(shop.Id, user.Id, ShopRole.Manager);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(managerMembership);

        var handler = CreateHandler();
        var query = new ExportSalesQuery(
            user.Id,
            shop.Id,
            "xlsx",
            "summary",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        var result = await handler.Handle(query, CancellationToken.None);

        // Should not return authorization error
        Assert.False(result.IsError);
    }
}
