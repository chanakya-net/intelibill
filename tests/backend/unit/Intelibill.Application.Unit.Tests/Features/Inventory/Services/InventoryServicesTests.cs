using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Services;

public class InventoryServicesTests
{
    [Fact]
    public async Task ItemResolver_WhenNameAndBarcodeResolveDifferentItems_ReturnsIdentityConflict()
    {
        var repo = Substitute.For<IItemRepository>();
        var resolver = new ItemResolver(repo);
        var shopId = Guid.NewGuid();

        var itemByBarcode = Item.Create(shopId, "Rice", "desc", "kg", "111", true, Guid.NewGuid());
        var itemByName = Item.Create(shopId, "Rice", "desc", "kg", "222", true, Guid.NewGuid());

        repo.GetByBarcodeAsync(shopId, "111", Arg.Any<CancellationToken>()).Returns(itemByBarcode);
        repo.GetByNameAsync(shopId, "Rice", Arg.Any<CancellationToken>()).Returns(itemByName);

        var result = await resolver.ResolveAsync(
            shopId,
            "Rice",
            "111",
            "desc",
            "kg",
            Guid.NewGuid(),
            new ItemResolutionContext(),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.ItemIdentityConflict.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task ItemResolver_WhenBarcodeExistsButNameMismatches_ReturnsNameBarcodeMismatch()
    {
        var repo = Substitute.For<IItemRepository>();
        var resolver = new ItemResolver(repo);
        var shopId = Guid.NewGuid();

        var existing = Item.Create(shopId, "Rice", "desc", "kg", "111", true, Guid.NewGuid());
        repo.GetByBarcodeAsync(shopId, "111", Arg.Any<CancellationToken>()).Returns(existing);
        repo.GetByNameAsync(shopId, "Sugar", Arg.Any<CancellationToken>()).Returns((Item?)null);

        var result = await resolver.ResolveAsync(
            shopId,
            "Sugar",
            "111",
            "desc",
            "kg",
            Guid.NewGuid(),
            new ItemResolutionContext(),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.ItemNameBarcodeMismatch.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task SupplierResolver_WhenSupplierFromDifferentShop_ReturnsSupplierNotFound()
    {
        var repo = Substitute.For<ISupplierRepository>();
        var resolver = new SupplierResolver(repo);

        var requestedShopId = Guid.NewGuid();
        var foreignShopId = Guid.NewGuid();
        var supplier = Supplier.Create(foreignShopId, "Foreign", null, null, null, null, null, null, true, false);

        repo.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>()).Returns(supplier);

        var result = await resolver.ResolveAsync(requestedShopId, supplier.Id, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task BatchFactory_WhenActiveBatchWithSameNumberExists_ReturnsBatchNumberAlreadyExists()
    {
        var batchRepo = Substitute.For<IInventoryBatchRepository>();
        var txRepo = Substitute.For<IStockTransactionRepository>();
        var ledgerRepo = Substitute.For<ISupplierLedgerEntryRepository>();
        var factory = new BatchFactory(batchRepo, txRepo, ledgerRepo);

        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var supplier = Supplier.Create(shopId, "Known Supplier", null, null, null, null, null, null, true, false);
        var existing = InventoryBatch.Create(shopId, itemId, "B-1", 1m, 10m, 15m, 12m, 5m, false, null, null, supplier.Id, actorId).Value;

        batchRepo.GetByBatchNumberAsync(shopId, itemId, "B-1", Arg.Any<CancellationToken>()).Returns(existing);

        var row = CreateBatchRow("B-1", supplier.Id);
        var result = await factory.CreateBatchAsync(shopId, itemId, row, supplier, actorId, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchNumberAlreadyExists.Code, result.FirstError.Code);
        await batchRepo.DidNotReceive().AddAsync(Arg.Any<InventoryBatch>(), Arg.Any<CancellationToken>());
        await txRepo.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await ledgerRepo.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BatchFactory_WhenSupplierNotProvided_AddsSystemSupplierNote()
    {
        var batchRepo = Substitute.For<IInventoryBatchRepository>();
        var txRepo = Substitute.For<IStockTransactionRepository>();
        var ledgerRepo = Substitute.For<ISupplierLedgerEntryRepository>();
        var factory = new BatchFactory(batchRepo, txRepo, ledgerRepo);

        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var systemSupplier = Supplier.CreateUnknownSystemSupplier(shopId);

        batchRepo.GetByBatchNumberAsync(shopId, itemId, "B-1", Arg.Any<CancellationToken>()).Returns((InventoryBatch?)null);

        var row = CreateBatchRow("B-1", supplierId: null);
        var result = await factory.CreateBatchAsync(shopId, itemId, row, systemSupplier, actorId, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Receipt with no supplier assigned", result.Value.LedgerEntry.Notes);
    }

    [Fact]
    public async Task BatchFactory_WhenExplicitSupplierProvided_LeavesSystemSupplierNoteNull()
    {
        var batchRepo = Substitute.For<IInventoryBatchRepository>();
        var txRepo = Substitute.For<IStockTransactionRepository>();
        var ledgerRepo = Substitute.For<ISupplierLedgerEntryRepository>();
        var factory = new BatchFactory(batchRepo, txRepo, ledgerRepo);

        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var supplier = Supplier.Create(shopId, "Known Supplier", null, null, null, null, null, null, true, false);

        batchRepo.GetByBatchNumberAsync(shopId, itemId, "B-1", Arg.Any<CancellationToken>()).Returns((InventoryBatch?)null);

        var row = CreateBatchRow("B-1", supplier.Id);
        var result = await factory.CreateBatchAsync(shopId, itemId, row, supplier, actorId, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.LedgerEntry.Notes);
    }

    [Fact]
    public async Task BatchFactory_WhenPerformedAtHasOffset_CreatesUtcInstant()
    {
        var batchRepo = Substitute.For<IInventoryBatchRepository>();
        var txRepo = Substitute.For<IStockTransactionRepository>();
        var ledgerRepo = Substitute.For<ISupplierLedgerEntryRepository>();
        var factory = new BatchFactory(batchRepo, txRepo, ledgerRepo);

        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var supplier = Supplier.Create(shopId, "Known Supplier", null, null, null, null, null, null, true, false);
        var performedAt = new DateTimeOffset(2026, 5, 15, 9, 30, 0, TimeSpan.FromHours(5.5));
        var expectedUtc = performedAt.ToUniversalTime();

        batchRepo.GetByBatchNumberAsync(shopId, itemId, "B-1", Arg.Any<CancellationToken>()).Returns((InventoryBatch?)null);

        var row = CreateBatchRow("B-1", supplier.Id, performedAt);
        var result = await factory.CreateBatchAsync(shopId, itemId, row, supplier, actorId, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(expectedUtc, result.Value.StockTransaction.PerformedAt);
        Assert.Equal(TimeSpan.Zero, result.Value.StockTransaction.PerformedAt.Offset);
        Assert.Equal(DateOnly.FromDateTime(expectedUtc.UtcDateTime), result.Value.LedgerEntry.EntryDate);
    }

    [Fact]
    public async Task InventoryUpdater_WhenInventoryMissing_CreatesAndAddsInventory()
    {
        var repo = Substitute.For<IInventoryRepository>();
        var updater = new InventoryUpdater(repo);
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        repo.GetByItemAsync(shopId, itemId, Arg.Any<CancellationToken>()).Returns((Domain.Entities.Inventory?)null);

        var result = await updater.GetOrUpdateAsync(
            shopId,
            itemId,
            quantityToAdd: 5m,
            actorUserId: actorId,
            context: new InventoryUpdateContext(),
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(5m, result.Value.Quantity);
        await repo.Received(1).AddAsync(Arg.Any<Domain.Entities.Inventory>(), Arg.Any<CancellationToken>());
        repo.DidNotReceive().Update(Arg.Any<Domain.Entities.Inventory>());
    }

    [Fact]
    public async Task InventoryUpdater_WhenInventoryExists_AddsQuantityAndUpdatesInventory()
    {
        var repo = Substitute.For<IInventoryRepository>();
        var updater = new InventoryUpdater(repo);
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var inventory = Domain.Entities.Inventory.Create(shopId, itemId, 10m, 0m, 0m, actorId).Value;
        repo.GetByItemAsync(shopId, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var result = await updater.GetOrUpdateAsync(
            shopId,
            itemId,
            quantityToAdd: 5m,
            actorUserId: actorId,
            context: new InventoryUpdateContext(),
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(15m, result.Value.Quantity);
        repo.Received(1).Update(inventory);
        await repo.DidNotReceive().AddAsync(Arg.Any<Domain.Entities.Inventory>(), Arg.Any<CancellationToken>());
    }

    private static AddInventoryBatchRowCommand CreateBatchRow(string batchNumber, Guid? supplierId, DateTimeOffset? performedAt = null) =>
        new(
            ClientRowId: "row-1",
            ItemName: "Rice",
            Barcode: "111",
            ItemDescription: "desc",
            Uom: "kg",
            BatchNumber: batchNumber,
            Quantity: 10m,
            CostPrice: 80m,
            Mrp: 120m,
            SalesPrice: 100m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: supplierId,
            ReferenceNumber: "PO-1",
            Notes: "initial",
            PerformedAt: performedAt,
            HsnCode: null);
}
