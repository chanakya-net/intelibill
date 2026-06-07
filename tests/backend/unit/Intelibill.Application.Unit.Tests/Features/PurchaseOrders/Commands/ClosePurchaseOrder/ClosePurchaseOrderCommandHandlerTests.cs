using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.ClosePurchaseOrder;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.ClosePurchaseOrder;

public class ClosePurchaseOrderCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ClosePurchaseOrderCommandHandler CreateHandler() =>
        new(_userRepository, _poRepository, _unitOfWork);

    private static (User actor, Shop shop) MakeActor(ShopRole role = ShopRole.Owner)
    {
        var actor = User.CreateWithEmail($"{role.ToString().ToLowerInvariant()}@test.com", "hash", "Test", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, role, true));
        return (actor, shop);
    }

    private static PurchaseOrder MakePartiallyReceivedPo(Guid shopId, Guid supplierId)
    {
        var po = PurchaseOrder.CreateDraft(shopId, "PO-2026-000001", supplierId, null, null, null, null);
        var line = po.AddLine(Guid.NewGuid(), "Widget", 2, 10m);
        po.Place(supplierId);
        po.ApplyReceipt(line.Id, 1);
        return po;
    }

    [Fact]
    public async Task HandleAsync_WhenPartiallyReceived_ClosesAndSaves()
    {
        var (actor, shop) = MakeActor(ShopRole.Manager);
        var po = MakePartiallyReceivedPo(shop.Id, Guid.NewGuid());
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new ClosePurchaseOrderCommand(actor.Id, shop.Id, po.Id, "Supplier short shipped"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Closed, result.Value.Status);
        Assert.Equal("Supplier short shipped", result.Value.CloseReason);
        Assert.Equal(actor.Id, result.Value.ClosedBy);
        Assert.NotNull(result.Value.ClosedAt);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPlaced_ReturnsInvalidStatus()
    {
        var (actor, shop) = MakeActor();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", Guid.NewGuid(), null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 2, 10m);
        po.Place(po.SupplierId!.Value);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new ClosePurchaseOrderCommand(actor.Id, shop.Id, po.Id, "reason"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CannotCloseInvalidStatus.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenBlankReason_ReturnsRequired()
    {
        var (actor, shop) = MakeActor();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new ClosePurchaseOrderCommand(actor.Id, shop.Id, Guid.NewGuid(), " "),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CloseReasonRequired.Code, result.FirstError.Code);
        await _poRepository.DidNotReceive().GetByShopAndIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
