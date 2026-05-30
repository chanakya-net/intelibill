using Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetSaleByReturnNumber;

public sealed class GetSaleByReturnNumberQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();

    private GetSaleByReturnNumberQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleReturnRepository);

    [Fact]
    public async Task Handle_WhenReturnNumberExistsInActiveShop_ReturnsOriginalSaleId()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        var saleId = Guid.NewGuid();
        var saleReturn = SaleReturn.Record(
            shop.Id,
            saleId,
            "RET-001",
            DateTimeOffset.UtcNow,
            user.Id,
            notes: null,
            totalRefundAmount: 0m,
            dueReductionAmount: 0m,
            payoutAmount: 0m,
            payoutMethod: null,
            totalTaxableAmount: 0m,
            totalTaxAmount: 0m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            []).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
        _saleReturnRepository.GetByReturnNumberAsync(shop.Id, "RET-001", Arg.Any<CancellationToken>())
            .Returns(saleReturn);

        var result = await CreateHandler().Handle(
            new GetSaleByReturnNumberQuery(user.Id, shop.Id, " RET-001 "),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(saleId, result.Value);
    }

    [Fact]
    public async Task Handle_WhenReturnNumberMissingInActiveShop_ReturnsNotFound()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Shop", "Address", "City", "State", "560001", null, null, null);
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));
        _saleReturnRepository.GetByReturnNumberAsync(shop.Id, "RET-404", Arg.Any<CancellationToken>())
            .Returns((SaleReturn?)null);

        var result = await CreateHandler().Handle(
            new GetSaleByReturnNumberQuery(user.Id, shop.Id, "RET-404"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("SaleReturn.NotFound", result.FirstError.Code);
    }
}
