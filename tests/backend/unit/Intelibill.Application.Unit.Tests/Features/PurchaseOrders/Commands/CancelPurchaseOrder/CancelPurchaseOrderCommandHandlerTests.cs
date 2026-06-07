using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.CancelPurchaseOrder;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.CancelPurchaseOrder;

public class CancelPurchaseOrderCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CancelPurchaseOrderCommandHandler CreateHandler() =>
        new(_userRepository, _poRepository, _unitOfWork);

    private static (User actor, Shop shop) MakeOwner()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        return (actor, shop);
    }

    [Fact]
    public async Task HandleAsync_WhenStaff_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new CancelPurchaseOrderCommand(actor.Id, shop.Id, Guid.NewGuid(), "reason"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.UserCannotMutatePurchaseOrder.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenPoNotFound_ReturnsNotFound()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((PurchaseOrder)null!);

        var result = await CreateHandler().HandleAsync(
            new CancelPurchaseOrderCommand(actor.Id, shop.Id, Guid.NewGuid(), null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.NotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyCancelled_ReturnsCannotCancelInvalidStatus()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);
        po.Cancel("first");

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new CancelPurchaseOrderCommand(actor.Id, shop.Id, po.Id, "second"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CannotCancelInvalidStatus.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenPlacedWithReceivedItems_ReturnsCannotCancelAfterReceipt()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", Guid.NewGuid(), null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m, receivedQuantity: 2);
        po.Place(po.SupplierId!.Value);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new CancelPurchaseOrderCommand(actor.Id, shop.Id, po.Id, "Can't cancel"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CannotCancelAfterReceipt.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_DraftWithNoReceipts_CancelsAndSaves()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new CancelPurchaseOrderCommand(actor.Id, shop.Id, po.Id, "Ordered by mistake"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Cancelled, result.Value.Status);
        Assert.Equal("Ordered by mistake", result.Value.CancellationReason);
        _poRepository.Received(1).Update(Arg.Is<PurchaseOrder>(p => p.Status == PurchaseOrderStatus.Cancelled));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PlacedWithZeroReceived_CancelsAndSaves()
    {
        var (actor, shop) = MakeOwner();
        var supplier = Supplier.Create(shop.Id, "Acme", null, null, null, null, null, null, true, false);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", supplier.Id, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        po.Place(supplier.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new CancelPurchaseOrderCommand(actor.Id, shop.Id, po.Id, "Supplier unavailable"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Cancelled, result.Value.Status);
    }
}
