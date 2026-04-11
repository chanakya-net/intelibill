using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.AddInventory;

public class AddInventoryCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ISupplierLedgerEntryRepository _supplierLedgerEntryRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenStaffRole_ReturnsForbidden()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNameAndBarcodeMapToDifferentItems_ReturnsValidationError()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var barcodeItem = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);
        var nameItem = Item.Create(shop.Id, "Rice", null, "kg", "222", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(barcodeItem);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(nameItem);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.ItemIdentityConflict.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNewItemAndBatch_CreatesAllRecords()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DomainInventory?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, Arg.Any<Guid>(), "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var supplierId = Guid.NewGuid();
        var knownSupplier = Supplier.Create(actor.Id, "Known Supplier", null, null, null, null, null, null, true, false, false);

        _supplierRepository.GetByIdAsync(supplierId, Arg.Any<CancellationToken>())
            .Returns(knownSupplier);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, supplierId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.BatchQuantity);
        Assert.Equal(10m, result.Value.TotalQuantity);
        Assert.Equal(knownSupplier.Id, result.Value.SupplierId);

        await _itemRepository.Received(1).AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
        await _inventoryBatchRepository.Received(1).AddAsync(Arg.Is<InventoryBatch>(b => 
            b.SupplierId == knownSupplier.Id && 
            b.OriginalQuantity == 10m && 
            b.Quantity == 10m &&
            !b.IsVoided), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.In && t.Quantity == 10m), Arg.Any<CancellationToken>());
        await _supplierLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e =>
                e.EntryType == SupplierLedgerEntryType.GoodsReceived
                && e.BatchId.HasValue
                && e.SupplierId == knownSupplier.Id
                && e.Amount == 800m),
            Arg.Any<CancellationToken>());
        await _inventoryRepository.Received(1).AddAsync(Arg.Any<DomainInventory>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExistingActiveBatch_ReturnsConflict()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        var batch = InventoryBatch.Create(
            shop.Id,
            item.Id,
            "B-1",
            quantity: 5m,
            costPrice: 80m,
            mrp: 120m,
            salesPrice: 110m,
            taxRatePercent: 5m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, item.Id, "B-1", Arg.Any<CancellationToken>()).Returns(batch);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchNumberAlreadyExists.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenExistingVoidedBatch_CreatesNewBatch()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        var batch = InventoryBatch.Create(
            shop.Id,
            item.Id,
            "B-1",
            quantity: 5m,
            costPrice: 80m,
            mrp: 120m,
            salesPrice: 110m,
            taxRatePercent: 5m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId: null,
            createdBy: actor.Id).Value;
        batch.Void(actor.Id);

        var inventory = DomainInventory.Create(shop.Id, item.Id, quantity: 20m, reorderLevel: 2m, maxLevel: 50m, createdBy: actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, item.Id, "B-1", Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.BatchQuantity);
        Assert.Equal(30m, result.Value.TotalQuantity);

        await _inventoryBatchRepository.Received(1).AddAsync(Arg.Is<InventoryBatch>(b => b.BatchNumber == "B-1" && !b.IsVoided), Arg.Any<CancellationToken>());
        _inventoryRepository.Received(1).Update(Arg.Is<DomainInventory>(i => i.Quantity == 30m));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierMissingInRequest_UsesSystemSupplierAndReceiptNote()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetSystemByOwnerUserIdAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DomainInventory?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, Arg.Any<Guid>(), "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, supplierId: null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(systemSupplier.Id, result.Value.SupplierId);

        await _supplierLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e =>
                e.SupplierId == systemSupplier.Id
                && e.EntryType == SupplierLedgerEntryType.GoodsReceived
                && e.Notes == "Receipt with no supplier assigned"
                && e.BatchId.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSystemSupplierMissing_ReturnsSystemSupplierNotFound()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetSystemByOwnerUserIdAsync(actor.Id, Arg.Any<CancellationToken>()).Returns((Supplier?)null);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, Arg.Any<Guid>(), "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, supplierId: null), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SystemSupplierNotFound.Code, result.FirstError.Code);
    }

    private AddInventoryCommandHandler CreateHandler() =>
        new(_userRepository, _supplierRepository, _itemRepository, _inventoryBatchRepository, _stockTransactionRepository, _supplierLedgerEntryRepository, _inventoryRepository, _unitOfWork);

    private void SetupSystemSupplierLookup()
    {
        _supplierRepository.GetSystemByOwnerUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Supplier.CreateUnknownSystemSupplier(callInfo.Arg<Guid>()));
    }

    private static AddInventoryCommand CreateCommand(Guid actorId, Guid shopId, Guid? supplierId = null) =>
        new(
            actorId,
            shopId,
            ItemName: "Rice",
            Barcode: "111",
            ItemDescription: "Sona masuri",
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 10m,
            CostPrice: 80m,
            Mrp: 120m,
            SalesPrice: 110m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: supplierId,
            ReferenceNumber: "PO-123",
            Notes: "Initial stock",
            PerformedAt: null);
}
