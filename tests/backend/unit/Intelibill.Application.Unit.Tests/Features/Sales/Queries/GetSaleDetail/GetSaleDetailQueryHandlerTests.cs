using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetSaleDetail;

public class GetSaleDetailQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    private GetSaleDetailQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _itemRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("test@test.com", "hash", "Test", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(userId, shopId, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shopId, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenSaleNotFound_ReturnsNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var saleId = Guid.NewGuid();

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByIdAsync(saleId, shop.Id, Arg.Any<CancellationToken>()).Returns((Sale?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, saleId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Sale.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenSaleFound_ReturnsItemNamesInDetail()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = SaleItem.Create(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            quantity: 2m,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);
        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 220m,
            dueAmount: 0m,
            totalAmount: 220m,
            totalTaxAmount: 20m,
            [saleItem]);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Items);
        Assert.Equal("Rice", result.Value.Items[0].ItemName);
    }
}
