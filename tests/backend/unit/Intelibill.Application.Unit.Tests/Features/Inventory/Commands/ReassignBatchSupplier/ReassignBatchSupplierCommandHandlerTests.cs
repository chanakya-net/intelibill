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
    private readonly IPurchaseReceiptValueRepository _purchaseReceiptValueRepository = Substitute.For<IPurchaseReceiptValueRepository>();
    private readonly IPaymentReceiptRepository _paymentReceiptRepository = Substitute.For<IPaymentReceiptRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenOwnerAndSystemSupplier_ReassignsBatchAndLedger()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        var realSupplier = Supplier.Create(shop.Id, "Real Supplier", null, null, null, null, null, null, true, false, false);

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
            _purchaseReceiptValueRepository,
            _paymentReceiptRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, realSupplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(realSupplier.Id, batch.SupplierId);
        _supplierLedgerEntryRepository.Received(1).Update(Arg.Is<SupplierLedgerEntry>(e => e.SupplierId == realSupplier.Id));
        _inventoryBatchRepository.Received(1).Update(Arg.Is<InventoryBatch>(b => b.SupplierId == realSupplier.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MovesCostAdjustmentsAndReceiptValueWithTheReceipt()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        var realSupplier = Supplier.Create(shop.Id, "Real Supplier", null, null, null, null, null, null, true, false, false);
        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "B-1", 5m, 10m, 15m, 14m, 5m, false, null, null, systemSupplier.Id, owner.Id).Value;
        var goods = SupplierLedgerEntry.Create(shop.Id, systemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, 50m, DateOnly.FromDateTime(DateTime.UtcNow), null, owner.Id).Value;
        var correction = SupplierLedgerEntry.Create(shop.Id, systemSupplier.Id, batch.Id, SupplierLedgerEntryType.CostAdjusted, 5m, DateOnly.FromDateTime(DateTime.UtcNow), null, owner.Id).Value;
        var receiptValue = PurchaseReceiptValue.Create(shop.Id, batch.Id, batch.ItemId, systemSupplier.Id, 5m, 50m, null, DateTimeOffset.UtcNow, owner.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(new[] { goods, correction });
        _purchaseReceiptValueRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(receiptValue);
        _supplierRepository.GetByIdAsync(systemSupplier.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);
        _supplierRepository.GetByIdAsync(realSupplier.Id, Arg.Any<CancellationToken>()).Returns(realSupplier);

        var result = await CreateHandler().HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, realSupplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(realSupplier.Id, goods.SupplierId);
        Assert.Equal(realSupplier.Id, correction.SupplierId);
        Assert.Equal(realSupplier.Id, receiptValue.SupplierId);
        _purchaseReceiptValueRepository.Received(1).Update(receiptValue);
    }

    [Fact]
    public async Task HandleAsync_WhenBatchVoided_IsRefused()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));
        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "B-1", 5m, 10m, 15m, 14m, 5m, false, null, null, Guid.NewGuid(), owner.Id).Value;
        batch.Void(owner.Id);
        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        var result = await CreateHandler().HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchAlreadyVoided.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private ReassignBatchSupplierCommandHandler CreateHandler() =>
        new(_userRepository, _inventoryBatchRepository, _supplierLedgerEntryRepository, _supplierRepository, _purchaseReceiptValueRepository, _paymentReceiptRepository, _unitOfWork);

    [Fact]
    public async Task HandleAsync_WhenFreeReceiptHasNoPayable_ReassignsBatchOnly()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        var realSupplier = Supplier.Create(shop.Id, "Real Supplier", null, null, null, null, null, null, true, false, false);

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, owner.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "FREE-1", 5m, 0m, 15m, 14m, 5m, false, null, null, systemSupplier.Id, owner.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SupplierLedgerEntry>());
        _supplierRepository.GetByIdAsync(systemSupplier.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);
        _supplierRepository.GetByIdAsync(realSupplier.Id, Arg.Any<CancellationToken>()).Returns(realSupplier);

        var handler = new ReassignBatchSupplierCommandHandler(
            _userRepository,
            _inventoryBatchRepository,
            _supplierLedgerEntryRepository,
            _supplierRepository,
            _purchaseReceiptValueRepository,
            _paymentReceiptRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, realSupplier.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(realSupplier.Id, batch.SupplierId);
        _supplierLedgerEntryRepository.DidNotReceive().Update(Arg.Any<SupplierLedgerEntry>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPricedBatchHasNoPayable_ReturnsLedgerEntryInvalid()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, owner.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "B-1", 5m, 10m, 15m, 14m, 5m, false, null, null, systemSupplier.Id, owner.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SupplierLedgerEntry>());

        var handler = new ReassignBatchSupplierCommandHandler(
            _userRepository,
            _inventoryBatchRepository,
            _supplierLedgerEntryRepository,
            _supplierRepository,
            _purchaseReceiptValueRepository,
            _paymentReceiptRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(owner.Id, shop.Id, batch.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.SupplierLedgerEntryInvalid.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenCurrentSupplierReal_ReturnsCannotReassignFromRealSupplier()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        owner.AddShopMembership(ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true));

        var supplier = Supplier.Create(shop.Id, "Real Supplier", null, null, null, null, null, null, true, false, false);
        var newSupplier = Supplier.Create(shop.Id, "Real Supplier 2", null, null, null, null, null, null, true, false, false);

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
            _purchaseReceiptValueRepository,
            _paymentReceiptRepository,
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
            _purchaseReceiptValueRepository,
            _paymentReceiptRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new ReassignBatchSupplierCommand(manager.Id, shop.Id, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.UserIsNotOwner.Code, result.FirstError.Code);
    }
}
