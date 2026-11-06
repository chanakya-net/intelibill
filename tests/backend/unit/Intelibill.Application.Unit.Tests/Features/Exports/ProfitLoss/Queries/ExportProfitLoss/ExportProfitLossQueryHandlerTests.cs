using ErrorOr;
using Intelibill.Application.Features.Exports.ProfitLoss;
using Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

public sealed class ExportProfitLossQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenOnlyFromIsGivenAndResolvesAfterToday_ReturnsInvalidDateRange()
    {
        var userRepository = Substitute.For<IUserRepository>();
        var shopRepository = Substitute.For<IShopRepository>();
        var user = User.CreateWithEmail("export@test.com", "hash", "Export", "User");
        var shop = Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);
        userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true));

        // The range is rejected before the report builder or renderer is touched.
        var handler = new ExportProfitLossQueryHandler(
            userRepository,
            shopRepository,
            null!,
            Substitute.For<IProfitLossExcelExportRenderer>(),
            BusinessTestDoubles.RealClock());

        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var result = await handler.Handle(
            new ExportProfitLossQuery(user.Id, shop.Id, from, null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Exports.ProfitLoss.InvalidDateRange", result.FirstError.Code);
    }
}
