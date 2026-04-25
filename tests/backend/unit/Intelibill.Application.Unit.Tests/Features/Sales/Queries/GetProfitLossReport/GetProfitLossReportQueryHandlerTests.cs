using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Queries.GetProfitLossReport;

public class GetProfitLossReportQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();

    private GetProfitLossReportQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _saleRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("sales@test.com", "hash", "Sales", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    [Fact]
    public async Task Handle_CalculatesProfitLossCorrectly()
    {
        // Arrange
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);

        // Item 1: Price includes tax. 110 total, 10% tax -> 100 base, 10 tax. Cost 80. Profit Before Tax: 20. Profit After Tax: 10.
        var item1 = SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 80, 110, 120, 10, true, false);
        // Item 2: Price excludes tax. 200 base, 5% tax -> 200 base, 10 tax. Cost 150. Profit Before Tax: 50. Profit After Tax: 40.
        var item2 = SaleItem.Create(shop.Id, Guid.NewGuid(), Guid.NewGuid(), 1, 150, 200, 250, 5, false, false);

        var sale = Sale.Create(
            shop.Id, "INV-001", null, "John Doe", null, PaymentMethod.Cash,
            DateTimeOffset.Now, 320, 0, 320, 20, new List<SaleItem> { item1, item2 });

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _saleRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(new[] { sale });

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetProfitLossReportQuery(user.Id, shop.Id), CancellationToken.None);

        // Assert
        Assert.False(result.IsError);
        var report = result.Value[0];
        
        // Total Cost: 80 + 150 = 230
        Assert.Equal(230, report.TotalCost);
        
        // Revenue Before Tax: 100 + 200 = 300
        Assert.Equal(300, report.RevenueBeforeTax);
        
        // Revenue After Tax: 110 + 210 = 320
        Assert.Equal(320, report.RevenueAfterTax);
        
        // Profit Before Tax: 300 - 230 = 70
        Assert.Equal(70, report.ProfitBeforeTax);
        
        // Profit After Tax: 300 - 230 - 20 (tax) = 50
        Assert.Equal(50, report.ProfitAfterTax);
    }
}
