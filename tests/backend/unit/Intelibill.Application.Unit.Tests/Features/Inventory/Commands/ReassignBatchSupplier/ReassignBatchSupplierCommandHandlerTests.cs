using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.ReassignBatchSupplier;

public class ReassignBatchSupplierCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly ISupplierLedgerEntryRepository _supplierLedgerEntryRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenOwnerAndSystemSupplier_ReassignsBatchAndLedger()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(owner.Id);
        var realSupplier = Supplier.Create(owner.Id, "Real Supplier", null, null, null, null, null, null, true, false, false);

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, owner.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "B-1", 5m, 10m, 15m, 14m, 5m, false, null, null, systemSupplier.Id, owner.Id).Value;
        var entry = SupplierLedgerEntry.Create(shop.Id, systemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, 50m, DateOnly.FromDateTime(DateTime.UtcNow), null, owner.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(new[] { entry });
        _supplierRepository.GetByIdAsync(systemSupplier.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);
        _supplierRepository.GetByIdAsync(realSupplier.Id, Arg.Any<CancellationToken>()).Returns(realSupplier);

        var handler = new ReassignBatchSupplierCommandHandler(
            _userRepository,
            _inventoryBatchRepository,
            _supplierLedgerEntryRepository,
            _supplierRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, realSupplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(realSupplier.Id, batch.SupplierId);
        _supplierLedgerEntryRepository.Received(1).Update(Arg.Is<SupplierLedgerEntry>(e => e.SupplierId == realSupplier.Id));
        _inventoryBatchRepository.Received(1).Update(Arg.Is<InventoryBatch>(b => b.SupplierId == realSupplier.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCurrentSupplierReal_ReturnsCannotReassignFromRealSupplier()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var supplier = Supplier.Create(owner.Id, "Real Supplier", null, null, null, null, null, null, true, false, false);
        var newSupplier = Supplier.Create(owner.Id, "Real Supplier 2", null, null, null, null, null, null, true, false, false);

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, owner.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "B-1", 5m, 10m, 15m, 14m, 5m, false, null, null, supplier.Id, owner.Id).Value;
        var entry = SupplierLedgerEntry.Create(shop.Id, supplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, 50m, DateOnly.FromDateTime(DateTime.UtcNow), null, owner.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(new[] { entry });
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);
        _supplierRepository.GetByIdAsync(newSupplier.Id, Arg.Any<CancellationToken>()).Returns(newSupplier);

        var handler = new ReassignBatchSupplierCommandHandler(
            _userRepository,
            _inventoryBatchRepository,
            _supplierLedgerEntryRepository,
            _supplierRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, newSupplier.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.CannotReassignFromRealSupplier.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenActorNotOwner_ReturnsUserIsNotOwner()
    {
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        manager.AddShopMembership(ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(manager.Id, Arg.Any<CancellationToken>()).Returns(manager);

        var handler = new ReassignBatchSupplierCommandHandler(
            _userRepository,
            _inventoryBatchRepository,
            _supplierLedgerEntryRepository,
            _supplierRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(manager.Id, shop.Id, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.UserIsNotOwner.Code, result.FirstError.Code);
    }
}
