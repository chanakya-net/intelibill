using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Queries.GetPurchaseOrderDetail;

public class GetPurchaseOrderDetailQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();

    private GetPurchaseOrderDetailQueryHandler CreateHandler() =>
        new(_userRepository, _poRepository);

    [Fact]
    public async Task HandleAsync_WhenPoNotFound_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "S", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns((PurchaseOrder?)null);

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrderDetailQuery(actor.Id, shop.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.NotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenFound_ReturnsDetailDto()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "O", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", "some notes");
        po.AddLine("Widget", 3, 50m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(
                shop.Id,
                po.Id,
                Arg.Any<CancellationToken>())
            .Returns(po);

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrderDetailQuery(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("PO-2026-000001", result.Value.PurchaseOrderNumber);
        Assert.Equal("some notes", result.Value.Notes);
        Assert.Single(result.Value.Lines);
        Assert.Equal(150m, result.Value.ExpectedTotal);

        await _poRepository.Received(1).GetByShopAndIdAsync(
            shop.Id,
            po.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenActorNotMember_ReturnsMembershipError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "O", "U");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrderDetailQuery(actor.Id, shop.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);

        await _poRepository.DidNotReceive().GetByShopAndIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPoBelongsToAnotherShop_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "S", "U");
        var activeShop = Shop.Create("Active Shop", "Addr", "City", "State", "560001", null, null, null);
        var otherShop = Shop.Create("Other Shop", "Addr", "City", "State", "560002", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(activeShop.Id, actor.Id, ShopRole.Staff, true));

        var po = PurchaseOrder.CreateDraft(otherShop.Id, "PO-2026-000001", "other notes");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        // Stabbing it so that querying by activeShop.Id and po.Id returns null
        _poRepository.GetByShopAndIdAsync(activeShop.Id, po.Id, Arg.Any<CancellationToken>())
            .Returns((PurchaseOrder?)null);

        var result = await CreateHandler().HandleAsync(
            new GetPurchaseOrderDetailQuery(actor.Id, activeShop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.NotFound.Code, result.FirstError.Code);

        await _poRepository.Received(1).GetByShopAndIdAsync(
            activeShop.Id,
            po.Id,
            Arg.Any<CancellationToken>());
    }
}
