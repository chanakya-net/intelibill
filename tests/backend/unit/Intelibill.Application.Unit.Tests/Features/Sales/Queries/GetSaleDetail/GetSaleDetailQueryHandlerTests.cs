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
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    private GetSaleDetailQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _saleReturnRepository, _itemRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("test@test.com", "hash", "Test", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    private static SaleItem MakeSaleItem(Guid shopId, Guid itemId, decimal quantity = 5m) =>
        SaleItem.Create(
            shopId,
            itemId,
            Guid.NewGuid(),
            quantity,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 10m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);

    private static Sale MakeSale(Guid shopId, SaleItem saleItem) =>
        Sale.Create(
            shopId,
            "INV-001",
            null,
            null,
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 550m,
            dueAmount: 0m,
            totalAmount: 550m,
            totalTaxAmount: 50m,
            [saleItem]);

    private static SaleReturn MakeSaleReturn(
        Guid shopId,
        Guid saleId,
        Guid saleItemId,
        decimal quantity,
        bool voided = false)
    {
        var returnItem = SaleReturnItem.Create(
            shopId,
            saleId,
            saleItemId,
            quantity,
            SaleReturnCondition.Restockable,
            originalCostPrice: 80m,
            originalSalesPrice: 100m,
            originalTaxRatePercent: 10m,
            originalIsPriceIncludingTax: false,
            maxRefundAmount: quantity * 100m,
            approvedRefundAmount: quantity * 100m,
            taxableAmount: quantity * 100m,
            taxAmount: quantity * 10m,
            notes: "Accepted").Value;

        var saleReturn = SaleReturn.Create(
            shopId,
            saleId,
            $"RET-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "Customer returned items",
            totalRefundAmount: quantity * 100m,
            dueReductionAmount: 0m,
            payoutAmount: quantity * 100m,
            totalTaxableAmount: quantity * 100m,
            totalTaxAmount: quantity * 10m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [returnItem]).Value;

        if (voided)
            saleReturn.Void(DateTimeOffset.UtcNow, Guid.NewGuid(), "Mistake");

        return saleReturn;
    }

    private void ArrangeAuthorizedSale(User user, Shop shop, Sale sale, Item item)
    {
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(MakeMembership(shop.Id, user.Id));
        _saleRepository.GetByIdAsync(sale.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);
    }

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
    public async Task Handle_WhenSaleFoundWithoutReturns_ReturnsReturnAwareLineDefaults()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id);
        var sale = MakeSale(shop.Id, saleItem);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Items);
        Assert.Equal("Rice", result.Value.Items[0].ItemName);
        Assert.Empty(result.Value.Returns);
        Assert.Equal(0m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(5m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("NotReturned", result.Value.Items[0].ReturnStatus);
    }

    [Fact]
    public async Task Handle_WhenSaleHasPartialReturn_ExposesReturnHistoryAndLineQuantities()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 5m);
        var sale = MakeSale(shop.Id, saleItem);
        var saleReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 2m);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([saleReturn]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Returns);
        Assert.Equal(saleReturn.Id, result.Value.Returns[0].SaleReturnId);
        Assert.Equal(2m, result.Value.Returns[0].Items[0].Quantity);
        Assert.Equal(2m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(3m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("PartiallyReturned", result.Value.Items[0].ReturnStatus);
        Assert.Equal(550m, result.Value.TotalAmount);
        Assert.Equal(50m, result.Value.TotalTaxAmount);
        Assert.Equal(550m, result.Value.PaidAmount);
    }

    [Fact]
    public async Task Handle_WhenSaleLineFullyReturned_ReturnableQuantityIsZero()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 5m);
        var sale = MakeSale(shop.Id, saleItem);
        var firstReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 2m);
        var secondReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 3m);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns([firstReturn, secondReturn]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Returns.Count);
        Assert.Equal(5m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(0m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("FullyReturned", result.Value.Items[0].ReturnStatus);
        Assert.Equal(550m, result.Value.TotalAmount);
        Assert.Equal(50m, result.Value.TotalTaxAmount);
    }

    [Fact]
    public async Task Handle_WhenReturnIsVoided_IgnoresItForHistoryAndQuantities()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var item = Item.Create(shop.Id, "Rice", "desc", "kg", "BC-001", true, Guid.NewGuid());
        var saleItem = MakeSaleItem(shop.Id, item.Id, quantity: 5m);
        var sale = MakeSale(shop.Id, saleItem);
        var voidedReturn = MakeSaleReturn(shop.Id, sale.Id, saleItem.Id, quantity: 5m, voided: true);

        ArrangeAuthorizedSale(user, shop, sale, item);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>()).Returns([voidedReturn]);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSaleDetailQuery(user.Id, shop.Id, sale.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Returns);
        Assert.Equal(0m, result.Value.Items[0].ReturnedQuantity);
        Assert.Equal(5m, result.Value.Items[0].ReturnableQuantity);
        Assert.Equal("NotReturned", result.Value.Items[0].ReturnStatus);
    }
}
