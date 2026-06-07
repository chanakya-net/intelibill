using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;

public class CreatePurchaseOrderDraftCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IPurchaseOrderNumberGenerator _numberGenerator = Substitute.For<IPurchaseOrderNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CreatePurchaseOrderDraftCommandHandler CreateHandler() =>
        new(_userRepository, _itemRepository, _supplierRepository, _poRepository, _numberGenerator, _unitOfWork);

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
            new CreatePurchaseOrderDraftCommand(actor.Id, shop.Id, null, null, null, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.UserCannotCreatePurchaseOrder.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenManager_Succeeds()
    {
        var actor = User.CreateWithEmail("mgr@test.com", "hash", "Mgr", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");
        var item = Item.Create(shop.Id, "Widget", null, "pcs", "W-1", true, actor.Id);
        ReturnItems(shop.Id, item);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                "Test notes",
                "Acme Traders",
                "SUP-REF-001",
                [new CreatePurchaseOrderLineInput(item.Id, "Widget", 5, 10m)]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("PO-2026-000001", result.Value.PurchaseOrderNumber);
        Assert.Equal("Test notes", result.Value.Notes);
        Assert.Single(result.Value.Lines);
        Assert.Equal(50m, result.Value.ExpectedTotal);
    }

    [Fact]
    public async Task HandleAsync_ZeroQuantity_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var item = Item.Create(shop.Id, "Item A", null, "pcs", "I-1", true, actor.Id);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");
        ReturnItems(shop.Id, item);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput(Guid.NewGuid(), "Widget", 0, 10m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.InvalidLineQuantity.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_NegativeUnitCost_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput(Guid.NewGuid(), "Widget", 1, -1m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.InvalidLineUnitCost.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_EmptyLineDescription_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput(Guid.NewGuid(), "   ", 1, 5m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineDescriptionRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_EmptyLineItemId_ReturnsItemRequiredError()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput(Guid.Empty, "Widget", 1, 5m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineItemRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidOwner_AddsPoAndSaves()
    {
        var (actor, shop) = MakeOwner();
        var item = Item.Create(shop.Id, "Item A", null, "pcs", "I-1", true, actor.Id);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");
        ReturnItems(shop.Id, item);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                " Acme Traders ",
                " SUP-REF-001 ",
                [new CreatePurchaseOrderLineInput(item.Id, "Item A", 2, 100m)]),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _poRepository.Received(1).AddAsync(
            Arg.Is<PurchaseOrder>(po =>
                po.ShopId == shop.Id
                && po.PurchaseOrderNumber == "PO-2026-000001"
                && po.SupplierName == "Acme Traders"
                && po.SupplierReference == "SUP-REF-001"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenLineItemIsUnknown_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var itemId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");
        ReturnItems(shop.Id);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput(itemId, "Item A", 2, 100m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineItemNotFound.Code, result.FirstError.Code);
        await _poRepository.DidNotReceive().AddAsync(Arg.Any<PurchaseOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenLineItemBelongsToAnotherShop_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        var otherShop = Shop.Create("Other", "Addr", "City", "State", "560001", null, null, null);
        var item = Item.Create(otherShop.Id, "Item A", null, "pcs", "I-1", true, actor.Id);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");
        ReturnItems(shop.Id, item);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput(item.Id, "Item A", 2, 100m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineItemNotFound.Code, result.FirstError.Code);
        await _poRepository.DidNotReceive().AddAsync(Arg.Any<PurchaseOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierIsUnknown_ReturnsSupplierNotFound()
    {
        var (actor, shop) = MakeOwner();
        var supplierId = Guid.NewGuid();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetByIdAsync(supplierId, Arg.Any<CancellationToken>()).Returns((Supplier)null!);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(actor.Id, shop.Id, supplierId, null, null, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _poRepository.DidNotReceive().AddAsync(Arg.Any<PurchaseOrder>(), Arg.Any<CancellationToken>());
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
            new CreatePurchaseOrderDraftCommand(actor.Id, shop.Id, supplier.Id, null, null, null, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _poRepository.DidNotReceive().AddAsync(Arg.Any<PurchaseOrder>(), Arg.Any<CancellationToken>());
    }
}
