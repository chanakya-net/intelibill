using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Queries.GetSaleByIdempotencyKey;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetSaleByIdempotencyKey;

public sealed class GetSaleByIdempotencyKeyQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();

    private GetSaleByIdempotencyKeyQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository);

    private (User User, Shop Shop) ArrangeMember()
    {
        var user = User.CreateWithEmail("cashier@test.com", "hash", "Cash", "Ier");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, true));
        return (user, shop);
    }

    private static Sale MakeSale(Guid shopId, Guid actorId, string idempotencyKey)
    {
        var line = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceId: null,
            LineName: "Rice",
            LineCode: "BC-TEST",
            1m,
            80m,
            100m,
            120m,
            18m,
            false,
            false,
            PreTaxAmountBeforeDiscount: 100m,
            TaxableAmount: 100m,
            TaxAmount: 18m,
            TotalAmount: 118m);
        return Sale.Record(
            shopId,
            actorId,
            idempotencyKey,
            "HASH",
            "INV-TEST",
            [line],
            null,
            null,
            null,
            PaymentMethod.Cash,
            118m,
            0m,
            DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public async Task Handle_WhenKeyCommittedByCallerInActiveShop_ReturnsSaleId()
    {
        var (user, shop) = ArrangeMember();
        var sale = MakeSale(shop.Id, user.Id, "checkout-1");
        _saleRepository.GetByIdempotencyKeyAsync(shop.Id, user.Id, "checkout-1", Arg.Any<CancellationToken>())
            .Returns(sale);

        var result = await CreateHandler().Handle(
            new GetSaleByIdempotencyKeyQuery(user.Id, shop.Id, " checkout-1 "),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(sale.Id, result.Value);
    }

    [Fact]
    public async Task Handle_LooksUpOnlyWithinCallerAndActiveShop()
    {
        var (user, shop) = ArrangeMember();
        _saleRepository.GetByIdempotencyKeyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Sale?)null);

        var result = await CreateHandler().Handle(
            new GetSaleByIdempotencyKeyQuery(user.Id, shop.Id, "checkout-1"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.IdempotencyKeyNotFound.Code, result.FirstError.Code);
        await _saleRepository.Received(1)
            .GetByIdempotencyKeyAsync(shop.Id, user.Id, "checkout-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotAMember_DoesNotLookUp()
    {
        var (user, shop) = ArrangeMember();
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new GetSaleByIdempotencyKeyQuery(user.Id, shop.Id, "checkout-1"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
        await _saleRepository.DidNotReceiveWithAnyArgs()
            .GetByIdempotencyKeyAsync(default, default, default!, default);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("x")]
    public async Task Handle_RejectsBlankOrOverlongKeys(string seed)
    {
        var (user, shop) = ArrangeMember();
        var key = seed == "x" ? new string('x', 129) : seed;

        var result = await CreateHandler().Handle(
            new GetSaleByIdempotencyKeyQuery(user.Id, shop.Id, key),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.Validation, result.FirstError.Type);
        await _saleRepository.DidNotReceiveWithAnyArgs()
            .GetByIdempotencyKeyAsync(default, default, default!, default);
    }
}
