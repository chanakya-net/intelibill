using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetSales;

public class GetSalesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();

    private GetSalesQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository, _saleReturnRepository);

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

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSalesQuery(userId, shopId), CancellationToken.None);

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

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSalesQuery(user.Id, shopId), CancellationToken.None);

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

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSalesQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenNoSales_ReturnsEmptyList()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSalesQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_WhenSalesExist_CallsRepositoryWithCorrectShopId()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());

        var handler = CreateHandler();
        await handler.Handle(new GetSalesQuery(user.Id, shop.Id), CancellationToken.None);

        await _saleRepository.Received(1).GetByShopAsync(shop.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSaleHasReturns_IncludesActiveReturnNumbers()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            customerId: null,
            customerName: null,
            customerPhone: null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            paidAmount: 100m,
            dueAmount: 0m,
            totalAmount: 100m,
            totalTaxAmount: 0m,
            []);
        var activeReturn = MakeReturn(shop.Id, sale.Id, "RET-001");
        var voidedReturn = MakeReturn(shop.Id, sale.Id, "RET-VOID");
        voidedReturn.Void(DateTimeOffset.UtcNow, user.Id, "Mistake");

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns([sale]);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns([activeReturn, voidedReturn]);

        var result = await CreateHandler().Handle(new GetSalesQuery(user.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(["RET-001"], result.Value.Single().ReturnNumbers);
    }

    private static SaleReturn MakeReturn(Guid shopId, Guid saleId, string returnNumber) =>
        SaleReturn.Create(
            shopId,
            saleId,
            returnNumber,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: 0m,
            dueReductionAmount: 0m,
            payoutAmount: 0m,
            totalTaxableAmount: 0m,
            totalTaxAmount: 0m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            []).Value;
}
