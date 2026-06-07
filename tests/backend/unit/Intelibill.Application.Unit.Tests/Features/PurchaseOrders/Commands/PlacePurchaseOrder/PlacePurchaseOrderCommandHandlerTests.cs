using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.PlacePurchaseOrder;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.PlacePurchaseOrder;

public class PlacePurchaseOrderCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private PlacePurchaseOrderCommandHandler CreateHandler() =>
        new(_userRepository, _supplierRepository, _poRepository, _unitOfWork);

    private static (User actor, Shop shop) MakeOwner()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        return (actor, shop);
    }

    private static PurchaseOrder MakeDraftWithLine(Guid shopId, Guid supplierId)
    {
        var po = PurchaseOrder.CreateDraft(shopId, "PO-2026-000001", supplierId, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        return po;
    }

    [Fact]
    public async Task HandleAsync_WhenStaff_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, Guid.NewGuid()),
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
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.NotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyPlaced_ReturnsCannotPlaceNonDraft()
    {
        var (actor, shop) = MakeOwner();
        var supplierId = Guid.NewGuid();
        var po = MakeDraftWithLine(shop.Id, supplierId);
        var supplier = Supplier.Create(shop.Id, "Acme", null, null, null, null, null, null, true, false);
        po.Place(supplier.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CannotPlaceNonDraft.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenNoSupplierOnPo_ReturnsSupplierRequired()
    {
        var (actor, shop) = MakeOwner();
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.SupplierRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierIsSystem_ReturnsSupplierInvalidForPlacement()
    {
        var (actor, shop) = MakeOwner();
        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", systemSupplier.Id, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _supplierRepository.GetByIdAsync(systemSupplier.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.SupplierInvalidForPlacement.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierIsInactive_ReturnsSupplierInvalidForPlacement()
    {
        var (actor, shop) = MakeOwner();
        var inactiveSupplier = Supplier.Create(shop.Id, "Inactive Co", null, null, null, null, null, null, false, false);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", inactiveSupplier.Id, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _supplierRepository.GetByIdAsync(inactiveSupplier.Id, Arg.Any<CancellationToken>()).Returns(inactiveSupplier);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.SupplierInvalidForPlacement.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierBelongsToAnotherShop_ReturnsSupplierInvalidForPlacement()
    {
        var (actor, shop) = MakeOwner();
        var otherShop = Shop.Create("Other", "Addr", "City", "State", "560001", null, null, null);
        var otherSupplier = Supplier.Create(otherShop.Id, "Other Supplier", null, null, null, null, null, null, true, false);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", otherSupplier.Id, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _supplierRepository.GetByIdAsync(otherSupplier.Id, Arg.Any<CancellationToken>()).Returns(otherSupplier);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.SupplierInvalidForPlacement.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenNoLines_ReturnsAtLeastOneLineRequired()
    {
        var (actor, shop) = MakeOwner();
        var supplier = Supplier.Create(shop.Id, "Acme", null, null, null, null, null, null, true, false);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", supplier.Id, null, null, null, null);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.AtLeastOneLineRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidOwner_PlacesPoAndSaves()
    {
        var (actor, shop) = MakeOwner();
        var supplier = Supplier.Create(shop.Id, "Acme", null, null, null, null, null, null, true, false);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", supplier.Id, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Placed, result.Value.Status);
        _poRepository.Received(1).Update(Arg.Is<PurchaseOrder>(p => p.Status == PurchaseOrderStatus.Placed));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidManager_Succeeds()
    {
        var actor = User.CreateWithEmail("mgr@test.com", "hash", "Mgr", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var supplier = Supplier.Create(shop.Id, "Acme", null, null, null, null, null, null, true, false);
        var po = PurchaseOrder.CreateDraft(shop.Id, "PO-2026-000001", supplier.Id, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 2, 50m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetByShopAndIdAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await CreateHandler().HandleAsync(
            new PlacePurchaseOrderCommand(actor.Id, shop.Id, po.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Placed, result.Value.Status);
    }
}
