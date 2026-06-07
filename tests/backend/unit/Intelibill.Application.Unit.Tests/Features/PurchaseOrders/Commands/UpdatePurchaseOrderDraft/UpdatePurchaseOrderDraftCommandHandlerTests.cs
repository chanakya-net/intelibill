using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.UpdatePurchaseOrderDraft;

public class UpdatePurchaseOrderDraftCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private UpdatePurchaseOrderDraftCommandHandler CreateHandler() =>
        new(_userRepository, _itemRepository, _supplierRepository, _poRepository, _unitOfWork);

    private static (User actor, Shop shop) MakeOwner()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        return (actor, shop);
    }

    private void ReturnItems(Guid shopId, params Item[] items) =>
        _itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(items);

    [Fact]
    public async Task HandleAsync_WhenStaff_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(actor.Id, shop.Id, Guid.NewGuid(), null, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.UserCannotCreatePurchaseOrder.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenPoNotFound_ReturnsNotFound()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((PurchaseOrder)null!);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(actor.Id, shop.Id, Guid.NewGuid(), null, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.NotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenPoNotDraft_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);
        typeof(PurchaseOrder).GetProperty("Status")!.SetValue(po, (PurchaseOrderStatus)999);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(actor.Id, shop.Id, po.Id, null, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CannotUpdateNonDraft.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidDraftUpdate_Succeeds()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, "old notes");
        po.AddLine(Guid.NewGuid(), "old line", 10, 15m);
        var item1 = Item.Create(shop.Id, "new line 1", null, "pcs", "I-1", true, actor.Id);
        var item2 = Item.Create(shop.Id, "new line 2", null, "pcs", "I-2", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        ReturnItems(shop.Id, item1, item2);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                po.Id,
                null,
                DateOnly.FromDateTime(DateTime.UtcNow.Date),
                DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)),
                "SUP-1",
                "new notes",
                [new UpdatePurchaseOrderLineInput(item1.Id, "new line 1", 5, 20m), new UpdatePurchaseOrderLineInput(item2.Id, "new line 2", 2, 50m)]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("new notes", result.Value.Notes);
        Assert.Equal("SUP-1", result.Value.SupplierReferenceNumber);
        Assert.Equal(2, result.Value.Lines.Count);
        Assert.Equal(200m, result.Value.ExpectedTotal);
        _poRepository.Received(1).Update(po);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DuplicateDescription_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);
        var itemId = Guid.NewGuid();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                po.Id,
                null,
                null,
                null,
                null,
                null,
                [new UpdatePurchaseOrderLineInput(itemId, "Widget", 2, 10m), new UpdatePurchaseOrderLineInput(itemId, "Other display", 3, 5m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.DuplicateItem.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_EmptyLineItemId_ReturnsItemRequiredError()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                po.Id,
                null,
                null,
                null,
                null,
                null,
                [new UpdatePurchaseOrderLineInput(Guid.Empty, "Widget", 2, 10m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineItemRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenLineItemIsUnknown_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);
        var itemId = Guid.NewGuid();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        ReturnItems(shop.Id);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                po.Id,
                null,
                null,
                null,
                null,
                null,
                [new UpdatePurchaseOrderLineInput(itemId, "Widget", 2, 10m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineItemNotFound.Code, result.FirstError.Code);
        _poRepository.DidNotReceive().Update(po);
    }

    [Fact]
    public async Task HandleAsync_WhenLineItemBelongsToAnotherShop_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var otherShop = Shop.Create("Other", "Addr", "City", "State", "560001", null, null, null);
        var item = Item.Create(otherShop.Id, "Widget", null, "pcs", "W-1", true, actor.Id);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        ReturnItems(shop.Id, item);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                po.Id,
                null,
                null,
                null,
                null,
                null,
                [new UpdatePurchaseOrderLineInput(item.Id, "Widget", 2, 10m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineItemNotFound.Code, result.FirstError.Code);
        _poRepository.DidNotReceive().Update(po);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierIsUnknown_ReturnsSupplierNotFound()
    {
        var (actor, shop) = MakeOwner();
        var supplierId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(supplierId, Arg.Any<CancellationToken>()).Returns((Supplier)null!);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(actor.Id, shop.Id, Guid.NewGuid(), supplierId, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        _poRepository.DidNotReceive().Update(Arg.Any<PurchaseOrder>());
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierBelongsToAnotherShop_ReturnsSupplierNotFound()
    {
        var (actor, shop) = MakeOwner();
        var otherShop = Shop.Create("Other", "Addr", "City", "State", "560001", null, null, null);
        var supplier = Supplier.Create(otherShop.Id, "Other Supplier", null, null, null, null, null, null, true, false);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(
            new UpdatePurchaseOrderDraftCommand(actor.Id, shop.Id, Guid.NewGuid(), supplier.Id, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        _poRepository.DidNotReceive().Update(Arg.Any<PurchaseOrder>());
    }
}
