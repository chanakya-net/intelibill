using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrders;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Queries.GetPurchaseOrders;

public class GetPurchaseOrdersQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();

    private GetPurchaseOrdersQueryHandler CreateHandler() =>
        new(_userRepository, _poRepository);

    [Fact]
    public async Task HandleAsync_ValidMember_ReturnsList()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "S", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAsync(shop.Id, 1, 20, Arg.Any<CancellationToken>())
            .Returns(new List<PurchaseOrder>().AsReadOnly() as IReadOnlyList<PurchaseOrder>);

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrdersQuery(actor.Id, shop.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);

        await _poRepository.Received(1).GetByShopAsync(
            shop.Id,
            1,
            20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPageAndPageSize_CustomSortInput_IsPassedToRepository()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "M", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAsync(shop.Id, 2, 50, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PurchaseOrder>());

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrdersQuery(actor.Id, shop.Id, 2, 50),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
        await _poRepository.Received(1).GetByShopAsync(shop.Id, 2, 50, Arg.Any<CancellationToken>());
    }
}
