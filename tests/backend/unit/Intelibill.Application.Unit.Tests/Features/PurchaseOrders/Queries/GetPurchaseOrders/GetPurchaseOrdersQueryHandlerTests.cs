using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.DTOs;
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
        _poRepository.GetByShopAsync(
                Arg.Is<PurchaseOrderListFilter>(filter =>
                    filter.ShopId == shop.Id
                    && filter.PageNumber == 1
                    && filter.PageSize == 20),
                Arg.Any<CancellationToken>())
            .Returns((Array.Empty<PurchaseOrder>(), 0));

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrdersQuery(actor.Id, shop.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(20, result.Value.PageSize);

        await _poRepository.Received(1).GetByShopAsync(
            Arg.Is<PurchaseOrderListFilter>(filter =>
                filter.ShopId == shop.Id
                && filter.PageNumber == 1
                && filter.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NormalizesPaginationAndPassesFilters()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "M", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAsync(Arg.Any<PurchaseOrderListFilter>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<PurchaseOrder>(), 0));

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrdersQuery(
                actor.Id,
                shop.Id,
                "  rice  ",
                PurchaseOrderStatus.Draft,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                0,
                999),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Items);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(100, result.Value.PageSize);
        await _poRepository.Received(1).GetByShopAsync(
            Arg.Is<PurchaseOrderListFilter>(filter =>
                filter.ShopId == shop.Id
                && filter.Search == "rice"
                && filter.Status == PurchaseOrderStatus.Draft
                && filter.OrderDateFrom == new DateOnly(2026, 6, 1)
                && filter.OrderDateTo == new DateOnly(2026, 6, 30)
                && filter.PageNumber == 1
                && filter.PageSize == 100),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MapsSupplierFieldsAndReceivedProgress()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "O", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var order = PurchaseOrder.CreateDraft(
            shop.Id,
            "PO-2026-000001",
            null,
            null,
            null,
            null,
            null,
            "Acme Traders",
            "SUP-REF-001");
        order.AddLine(Guid.NewGuid(), "Rice", 5, 10m, receivedQuantity: 3);
        order.AddLine(Guid.NewGuid(), "Oil", 2, 20m, receivedQuantity: 2);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAsync(Arg.Any<PurchaseOrderListFilter>(), Arg.Any<CancellationToken>())
            .Returns((new[] { order }, 1));

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrdersQuery(actor.Id, shop.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal("Acme Traders", item.SupplierName);
        Assert.Equal("SUP-REF-001", item.SupplierReference);
        Assert.Equal(7, item.ExpectedQuantity);
        Assert.Equal(5, item.ReceivedQuantity);
    }
}
