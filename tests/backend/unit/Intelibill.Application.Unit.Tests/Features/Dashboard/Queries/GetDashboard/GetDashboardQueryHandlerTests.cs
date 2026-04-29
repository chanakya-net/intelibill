using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Dashboard.Queries.GetDashboard;

public class GetDashboardQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();

    private GetDashboardQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("dash@test.com", "hash", "Dash", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "1 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenValid_ReturnsDashboardWithSalesCountAndFreshnessStamp()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        var sale = Sale.Create(
            shop.Id, "INV-001", null, "John", null, PaymentMethod.Cash,
            DateTimeOffset.UtcNow, 100m, 0m, 100m, 10m,
            [SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 80m, 100m, 120m, 10m, false, false)]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([sale]);

        var before = DateTimeOffset.UtcNow;
        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.SalesCount);
        Assert.InRange(result.Value.GeneratedAt, before, after);
    }

    [Fact]
    public async Task Handle_WhenNoSales_ReturnsSalesCountZero()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(
            new GetDashboardQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.SalesCount);
    }
}
