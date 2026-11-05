using ErrorOr;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Services;

public class InboundInventoryLineProcessorTests
{
    private readonly IItemResolver _itemResolver = Substitute.For<IItemResolver>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ISupplierResolver _supplierResolver = Substitute.For<ISupplierResolver>();
    private readonly IBatchFactory _batchFactory = Substitute.For<IBatchFactory>();
    private readonly IInventoryUpdater _inventoryUpdater = Substitute.For<IInventoryUpdater>();
    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Supplier _systemSupplier;

    public InboundInventoryLineProcessorTests()
    {
        _systemSupplier = Supplier.CreateUnknownSystemSupplier(_shopId);
        _supplierResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_systemSupplier);
    }

    [Fact]
    public async Task ProcessAsync_WithHsnCode_UpdatesTaxDefaultsOnItem()
    {
        var item = ExistingItem("Rice", "111");
        item.UpdateHsnCode("OLD");
        item.UpdateTaxDefaults("OLD", 12m, true);
        ArrangeSuccessfulRow(item);

        var result = await CreateProcessor().ProcessAsync(
            _shopId, Row("Rice", "111", hsnCode: "NEW"), _actorId, new ItemResolutionContext(), new InventoryUpdateContext(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("NEW", item.HsnCode);
        Assert.Equal(5m, item.DefaultTaxRatePercent);
        Assert.False(item.DefaultTaxIncluded);
        _itemRepository.Received(1).Update(item);
    }

    [Fact]
    public async Task ProcessAsync_WithNullHsnCode_PreservesHsnButStillUpdatesTaxDefaults()
    {
        var item = ExistingItem("Rice", "111");
        item.UpdateHsnCode("OLD");
        item.UpdateTaxDefaults("OLD", 12m, true);
        ArrangeSuccessfulRow(item);

        var result = await CreateProcessor().ProcessAsync(
            _shopId, Row("Rice", "111", hsnCode: null), _actorId, new ItemResolutionContext(), new InventoryUpdateContext(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("OLD", item.HsnCode);
        Assert.Equal(5m, item.DefaultTaxRatePercent);
        Assert.False(item.DefaultTaxIncluded);
        _itemRepository.Received(1).Update(item);
    }

    [Fact]
    public async Task ProcessAsync_WhenBatchRejected_DoesNotUpdateTaxDefaults()
    {
        var item = ExistingItem("Rice", "111");
        item.UpdateTaxDefaults("OLD", 12m, true);
        _itemResolver.ResolveAsync(_shopId, "Rice", "111", "Description", "kg", _actorId, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(item);
        _batchFactory.CreateBatchAsync(_shopId, item.Id, Arg.Any<InboundInventoryLineInput>(), _systemSupplier, _actorId, Arg.Any<CancellationToken>())
            .Returns(Error.Validation("Inventory.BatchRejected", "Batch rejected."));

        var result = await CreateProcessor().ProcessAsync(
            _shopId, Row("Rice", "111", hsnCode: "FAILED"), _actorId, new ItemResolutionContext(), new InventoryUpdateContext(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("OLD", item.HsnCode);
        Assert.Equal(12m, item.DefaultTaxRatePercent);
        Assert.True(item.DefaultTaxIncluded);
        _itemRepository.DidNotReceive().Update(item);
        await _inventoryUpdater.DidNotReceive().GetOrUpdateAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<Guid>(), Arg.Any<InventoryUpdateContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenQuantityExceedsExistingItemScale_RefusesBeforeBuildingTheBatch()
    {
        var item = Item.Create(_shopId, "Soap", "Desc", "PCS", "222", true, _actorId);
        _itemResolver.ResolveAsync(_shopId, "Soap", "222", "Description", "PCS", _actorId, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(item);

        var result = await CreateProcessor().ProcessAsync(
            _shopId,
            Row("Soap", "222", hsnCode: null) with { Uom = "PCS", Quantity = 2.5m },
            _actorId,
            new ItemResolutionContext(),
            new InventoryUpdateContext(),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.QuantityPrecisionExceeded", result.FirstError.Code);
        await _batchFactory.DidNotReceive().CreateBatchAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<InboundInventoryLineInput>(), Arg.Any<Supplier>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private InboundInventoryLineProcessor CreateProcessor() =>
        new(_itemResolver, _itemRepository, _supplierResolver, _batchFactory, _inventoryUpdater);

    private Item ExistingItem(string name, string barcode)
    {
        var item = Item.Create(_shopId, name, "Desc", "kg", barcode, true, _actorId);
        _itemRepository.GetByBarcodeAsync(_shopId, barcode, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    private void ArrangeSuccessfulRow(Item item)
    {
        _itemResolver.ResolveAsync(_shopId, item.Name, item.Barcode, "Description", "kg", _actorId, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(item);

        _batchFactory.CreateBatchAsync(_shopId, item.Id, Arg.Any<InboundInventoryLineInput>(), _systemSupplier, _actorId, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var row = callInfo.Arg<InboundInventoryLineInput>();
                var unitCost = decimal.Round(row.TotalPurchaseCost / row.Quantity, 2, MidpointRounding.AwayFromZero);
                var batch = InventoryBatch.Create(_shopId, item.Id, row.BatchNumber, row.Quantity, unitCost, row.Mrp, row.SalesPrice, row.TaxRatePercent, row.TaxIncluded, null, null, _systemSupplier.Id, _actorId).Value;
                var tx = StockTransaction.Create(_shopId, item.Id, batch.Id, StockTransactionType.In, row.Quantity, null, null, DateTimeOffset.UtcNow, _actorId, _actorId).Value;
                var ledger = SupplierLedgerEntry.Create(_shopId, _systemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, row.TotalPurchaseCost, DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime), null, _actorId).Value;
                return new BatchCreationResult(batch, tx, ledger);
            });

        var inventory = Domain.Entities.Inventory.Create(_shopId, item.Id, 10m, 0, 0, _actorId).Value;
        _inventoryUpdater.GetOrUpdateAsync(_shopId, item.Id, Arg.Any<decimal>(), _actorId, Arg.Any<InventoryUpdateContext>(), Arg.Any<CancellationToken>())
            .Returns(inventory);
    }

    private static InboundInventoryLineInput Row(string itemName, string barcode, string? hsnCode) =>
        new(
            ItemId: null,
            itemName,
            barcode,
            ItemDescription: "Description",
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 10m,
            TotalPurchaseCost: 800m,
            Mrp: 120m,
            SalesPrice: 110m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            PurchaseTaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: null,
            ReferenceNumber: "PO-1",
            Notes: "Initial",
            PerformedAt: null,
            HsnCode: hsnCode);
}
