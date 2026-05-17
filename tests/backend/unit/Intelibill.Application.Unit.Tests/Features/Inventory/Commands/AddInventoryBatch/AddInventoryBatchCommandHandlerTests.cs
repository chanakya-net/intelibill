using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.AddInventoryBatch;

public class AddInventoryBatchCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemResolver _itemResolver = Substitute.For<IItemResolver>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ISupplierResolver _supplierResolver = Substitute.For<ISupplierResolver>();
    private readonly IBatchFactory _batchFactory = Substitute.For<IBatchFactory>();
    private readonly IInventoryUpdater _inventoryUpdater = Substitute.For<IInventoryUpdater>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly Supplier SystemSupplier = Supplier.CreateUnknownSystemSupplier(Guid.NewGuid());

    public AddInventoryBatchCommandHandlerTests()
    {
        _supplierResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SystemSupplier);
    }

    [Fact]
    public async Task HandleAsync_WhenMoreThanHundredRows_ReturnsValidationError()
    {
        var command = new AddInventoryBatchCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Enumerable.Range(1, 101)
                .Select(index => CreateRow($"row-{index}", $"Item-{index}", $"BC-{index}"))
                .ToArray());

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.BatchLimitExceeded", result.FirstError.Code);
        Assert.Equal("Only 100 items are allowed in a batch.", result.FirstError.Description);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenEmptyBatch_ReturnsValidationError()
    {
        var command = new AddInventoryBatchCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            []);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.BatchEmpty", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenStaffRole_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new AddInventoryBatchCommand(actor.Id, shop.Id, [CreateRow("row-1", "Rice", "111")]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenOneRowFails_PersistsSucceededRows()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var existingItem = Item.Create(shop.Id, "Rice", "Desc", "kg", "111", true, actor.Id);
        _itemResolver.ResolveAsync(shop.Id, "Rice", "111", "Description", "kg", actor.Id, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(existingItem);
        _itemResolver.ResolveAsync(shop.Id, "Different Name", "111", "Description", "kg", actor.Id, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Inventory.ItemNameBarcodeMismatch);

        _batchFactory.CreateBatchAsync(shop.Id, existingItem.Id, Arg.Any<AddInventoryBatchRowCommand>(), SystemSupplier, actor.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var row = callInfo.Arg<AddInventoryBatchRowCommand>();
                var batch = InventoryBatch.Create(shop.Id, existingItem.Id, row.BatchNumber, row.Quantity, row.CostPrice, row.Mrp, row.SalesPrice, row.TaxRatePercent, row.TaxIncluded, null, null, SystemSupplier.Id, actor.Id).Value;
                var tx = StockTransaction.Create(shop.Id, existingItem.Id, batch.Id, StockTransactionType.In, row.Quantity, null, null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
                var ledger = SupplierLedgerEntry.Create(shop.Id, SystemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, row.CostPrice * row.Quantity, DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime), null, actor.Id).Value;
                return new BatchCreationResult(batch, tx, ledger);
            });

        var inventory = Domain.Entities.Inventory.Create(shop.Id, existingItem.Id, 10m, 0, 0, actor.Id).Value;
        _inventoryUpdater.GetOrUpdateAsync(shop.Id, existingItem.Id, Arg.Any<decimal>(), actor.Id, Arg.Any<InventoryUpdateContext>(), Arg.Any<CancellationToken>())
            .Returns(inventory);

        var command = new AddInventoryBatchCommand(
            actor.Id,
            shop.Id,
            [
                CreateRow("row-1", "Rice", "111"),
                CreateRow("row-2", "Different Name", "111")
            ]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.RequestedCount);
        Assert.Equal(1, result.Value.SuccessCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Single(result.Value.Succeeded);
        Assert.Equal("row-2", result.Value.Failed[0].ClientRowId);
        Assert.Equal(Errors.Inventory.ItemNameBarcodeMismatch.Code, result.Value.Failed[0].Errors[0].Code);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAllRowsSucceed_CommitsEverything()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        _itemResolver.ResolveAsync(shop.Id, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), actor.Id, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var name = callInfo.ArgAt<string>(1);
                var barcode = callInfo.ArgAt<string>(2);
                var desc = callInfo.ArgAt<string?>(3);
                var uom = callInfo.ArgAt<string>(4);
                return Item.Create(shop.Id, name, desc, uom, barcode, true, actor.Id);
            });

        _batchFactory.CreateBatchAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<AddInventoryBatchRowCommand>(), SystemSupplier, actor.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var itemId = callInfo.ArgAt<Guid>(1);
                var row = callInfo.Arg<AddInventoryBatchRowCommand>();
                var batch = InventoryBatch.Create(shop.Id, itemId, row.BatchNumber, row.Quantity, row.CostPrice, row.Mrp, row.SalesPrice, row.TaxRatePercent, row.TaxIncluded, null, null, SystemSupplier.Id, actor.Id).Value;
                var tx = StockTransaction.Create(shop.Id, itemId, batch.Id, StockTransactionType.In, row.Quantity, null, null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
                var ledger = SupplierLedgerEntry.Create(shop.Id, SystemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, row.CostPrice * row.Quantity, DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime), null, actor.Id).Value;
                return new BatchCreationResult(batch, tx, ledger);
            });

        var inventory = Domain.Entities.Inventory.Create(shop.Id, Guid.NewGuid(), 10m, 0, 0, actor.Id).Value;
        _inventoryUpdater.GetOrUpdateAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<decimal>(), actor.Id, Arg.Any<InventoryUpdateContext>(), Arg.Any<CancellationToken>())
            .Returns(inventory);

        var command = new AddInventoryBatchCommand(
            actor.Id,
            shop.Id,
            [
                CreateRow("row-1", "Rice", "111"),
                CreateRow("row-2", "Sugar", "222")
            ]);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.SuccessCount);
        Assert.Equal(0, result.Value.FailedCount);
        Assert.Equal(2, result.Value.Succeeded.Count);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithHsnCode_CallsUpdateHsnCodeOnItem()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var existingItem = Item.Create(shop.Id, "Rice", "Desc", "kg", "111", true, actor.Id);
        existingItem.UpdateHsnCode("OLD");

        _itemResolver.ResolveAsync(shop.Id, "Rice", "111", "Description", "kg", actor.Id, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(existingItem);

        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(existingItem);

        _batchFactory.CreateBatchAsync(shop.Id, existingItem.Id, Arg.Any<AddInventoryBatchRowCommand>(), SystemSupplier, actor.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var row = callInfo.Arg<AddInventoryBatchRowCommand>();
                var batch = InventoryBatch.Create(shop.Id, existingItem.Id, row.BatchNumber, row.Quantity, row.CostPrice, row.Mrp, row.SalesPrice, row.TaxRatePercent, row.TaxIncluded, null, null, SystemSupplier.Id, actor.Id).Value;
                var tx = StockTransaction.Create(shop.Id, existingItem.Id, batch.Id, StockTransactionType.In, row.Quantity, null, null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
                var ledger = SupplierLedgerEntry.Create(shop.Id, SystemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, row.CostPrice * row.Quantity, DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime), null, actor.Id).Value;
                return new BatchCreationResult(batch, tx, ledger);
            });

        var inventory = Domain.Entities.Inventory.Create(shop.Id, existingItem.Id, 10m, 0, 0, actor.Id).Value;
        _inventoryUpdater.GetOrUpdateAsync(shop.Id, existingItem.Id, Arg.Any<decimal>(), actor.Id, Arg.Any<InventoryUpdateContext>(), Arg.Any<CancellationToken>())
            .Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new AddInventoryBatchCommand(actor.Id, shop.Id, [CreateRow("row-1", "Rice", "111", hsnCode: "NEW")]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("NEW", existingItem.HsnCode);
        _itemRepository.Received(1).Update(existingItem);
    }

    [Fact]
    public async Task Handle_WithNullHsnCode_DoesNotCallUpdateHsnCodeOnItem()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var existingItem = Item.Create(shop.Id, "Rice", "Desc", "kg", "111", true, actor.Id);
        existingItem.UpdateHsnCode("OLD");

        _itemResolver.ResolveAsync(shop.Id, "Rice", "111", "Description", "kg", actor.Id, Arg.Any<ItemResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(existingItem);

        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(existingItem);

        _batchFactory.CreateBatchAsync(shop.Id, existingItem.Id, Arg.Any<AddInventoryBatchRowCommand>(), SystemSupplier, actor.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var row = callInfo.Arg<AddInventoryBatchRowCommand>();
                var batch = InventoryBatch.Create(shop.Id, existingItem.Id, row.BatchNumber, row.Quantity, row.CostPrice, row.Mrp, row.SalesPrice, row.TaxRatePercent, row.TaxIncluded, null, null, SystemSupplier.Id, actor.Id).Value;
                var tx = StockTransaction.Create(shop.Id, existingItem.Id, batch.Id, StockTransactionType.In, row.Quantity, null, null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
                var ledger = SupplierLedgerEntry.Create(shop.Id, SystemSupplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, row.CostPrice * row.Quantity, DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime), null, actor.Id).Value;
                return new BatchCreationResult(batch, tx, ledger);
            });

        var inventory = Domain.Entities.Inventory.Create(shop.Id, existingItem.Id, 10m, 0, 0, actor.Id).Value;
        _inventoryUpdater.GetOrUpdateAsync(shop.Id, existingItem.Id, Arg.Any<decimal>(), actor.Id, Arg.Any<InventoryUpdateContext>(), Arg.Any<CancellationToken>())
            .Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new AddInventoryBatchCommand(actor.Id, shop.Id, [CreateRow("row-1", "Rice", "111", hsnCode: null)]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("OLD", existingItem.HsnCode);
        _itemRepository.DidNotReceive().Update(existingItem);
    }

    private AddInventoryBatchCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _itemResolver,
            _itemRepository,
            _supplierResolver,
            _batchFactory,
            _inventoryUpdater,
            _unitOfWork);

    private static AddInventoryBatchRowCommand CreateRow(string clientRowId, string itemName, string barcode, string? hsnCode = null) =>
        new(
            clientRowId,
            itemName,
            barcode,
            ItemDescription: "Description",
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
            SupplierId: null,
            ReferenceNumber: "PO-1",
            Notes: "Initial",
            PerformedAt: null,
            HsnCode: hsnCode);
}
