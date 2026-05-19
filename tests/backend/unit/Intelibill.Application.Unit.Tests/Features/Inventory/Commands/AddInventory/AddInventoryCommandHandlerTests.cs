using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
        var knownSupplier = Supplier.Create(shop.Id, "Known Supplier", null, null, null, null, null, null, true, false, false);

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

        await _itemRepository.Received(1).AddAsync(
            Arg.Is<Item>(i => i.DefaultTaxRatePercent == 5m && i.DefaultTaxIncluded == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExistingItem_UpdatesTaxDefaults()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var existingItem = Item.Create(shop.Id, "Rice", "Desc", "kg", "111", true, actor.Id);
        existingItem.UpdateTaxDefaults("1001", 12m, true);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(existingItem);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(existingItem);
        _inventoryRepository.GetByItemAsync(shop.Id, existingItem.Id, Arg.Any<CancellationToken>())
            .Returns((DomainInventory?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, existingItem.Id, "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, hsnCode: "2002"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("2002", existingItem.HsnCode);
        Assert.Equal(5m, existingItem.DefaultTaxRatePercent);
        Assert.False(existingItem.DefaultTaxIncluded);
        _itemRepository.Received(1).Update(existingItem);
    }

    [Fact]
    public async Task HandleAsync_WhenPerformedAtHasOffset_StoresUtcInstant()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        var performedAt = new DateTimeOffset(2026, 5, 15, 9, 30, 0, TimeSpan.FromHours(5.5));
        var expectedUtc = performedAt.ToUniversalTime();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DomainInventory?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, Arg.Any<Guid>(), "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, performedAt: performedAt), CancellationToken.None);

        Assert.False(result.IsError);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t => t.PerformedAt == expectedUtc && t.PerformedAt.Offset == TimeSpan.Zero),
            Arg.Any<CancellationToken>());
        await _supplierLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e => e.EntryDate == DateOnly.FromDateTime(expectedUtc.UtcDateTime)),
            Arg.Any<CancellationToken>());
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

        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shop.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _supplierRepository.GetSystemByShopIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(systemSupplier);
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
        _supplierRepository.GetSystemByShopIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns((Supplier?)null);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, Arg.Any<Guid>(), "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, supplierId: null), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SystemSupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrencyConflictOnFirstAttempt_RetriesAndSucceeds()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        // First attempt sees qty=10, second attempt (after simulated reload) sees qty=12
        var inventory1 = DomainInventory.Create(shop.Id, item.Id, 10m, 0m, 0m, actor.Id).Value;
        var inventory2 = DomainInventory.Create(shop.Id, item.Id, 12m, 0m, 0m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, item.Id, "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns(inventory1, inventory2);

        // First SaveChanges throws concurrency; second succeeds
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<int>(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>())),
                _ => Task.FromResult(1));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        // inventory2 (qty=12) + command qty (10) = 22
        Assert.Equal(22m, result.Value.TotalQuantity);

        // Batch/stock tx/ledger added exactly once — not repeated on retry
        await _inventoryBatchRepository.Received(1).AddAsync(Arg.Any<InventoryBatch>(), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _supplierLedgerEntryRepository.Received(1).AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrencyExhaustsAllRetries_ReturnsUpdateConflictError()
    {
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        var inventory = DomainInventory.Create(shop.Id, item.Id, 10m, 0m, 0m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, item.Id, "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>()).Returns(inventory);

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.UpdateConflict", result.FirstError.Code);

        await _unitOfWork.Received(3).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSingleRequest_CompletesInOneAttempt()
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

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private AddInventoryCommandHandler CreateHandler() =>
        new(_userRepository, _supplierRepository, _itemRepository, _inventoryBatchRepository, _stockTransactionRepository, _supplierLedgerEntryRepository, _inventoryRepository, _unitOfWork);

    private void SetupSystemSupplierLookup()
    {
        _supplierRepository.GetSystemByShopIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Supplier.CreateUnknownSystemSupplier(callInfo.Arg<Guid>()));
    }

    private static AddInventoryCommand CreateCommand(Guid actorId, Guid shopId, Guid? supplierId = null, DateTimeOffset? performedAt = null, string? hsnCode = null) =>
        new(
            actorId,
            shopId,
            ItemName: "Rice",
            Barcode: "111",
            ItemDescription: "Sona masuri",
            HsnCode: hsnCode,
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
            PerformedAt: performedAt);
}
