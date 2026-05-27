using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetSales;

public sealed class GetSalesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();

    private GetSalesQueryHandler CreateHandler() => new(_userRepository, _shopRepository, _saleRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("sales@test.com", "hash", "Sales", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().Handle(
            new GetSalesQuery(userId, shopId, From: null, To: null, Search: null, Status: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var result = await CreateHandler().Handle(
            new GetSalesQuery(user.Id, shopId, From: null, To: null, Search: null, Status: null, Page: 1, PageSize: 20),
            CancellationToken.None);

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
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new GetSalesQuery(user.Id, shop.Id, From: null, To: null, Search: null, Status: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenNoSales_ReturnsEmptyPaginatedResult()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);

        _saleRepository.GetHistoryAsync(Arg.Any<SaleHistoryFilter>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<SaleHistoryReadModel>(), 0));
        _saleRepository.GetHistorySummaryAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new SalesHistorySummaryReadModel(0m, 0, 0m));

        var result = await CreateHandler().Handle(
            new GetSalesQuery(user.Id, shop.Id, From: null, To: null, Search: null, Status: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Equal(0, result.Value.Summary.InvoiceCount);
    }

    [Fact]
    public async Task Handle_NormalizesPageAndCapsPageSize()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);

        _saleRepository.GetHistoryAsync(Arg.Any<SaleHistoryFilter>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<SaleHistoryReadModel>(), 0));
        _saleRepository.GetHistorySummaryAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new SalesHistorySummaryReadModel(0m, 0, 0m));

        await CreateHandler().Handle(
            new GetSalesQuery(user.Id, shop.Id, From: null, To: null, Search: null, Status: null, Page: 0, PageSize: 999),
            CancellationToken.None);

        await _saleRepository.Received(1).GetHistoryAsync(
            Arg.Is<SaleHistoryFilter>(f => f.ShopId == shop.Id && f.PageNumber == 1 && f.PageSize == 100),
            Arg.Any<CancellationToken>());
    }
}

