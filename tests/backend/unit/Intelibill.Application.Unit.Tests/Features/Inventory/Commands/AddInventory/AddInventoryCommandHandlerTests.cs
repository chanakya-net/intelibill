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
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenStaffRole_ReturnsForbidden()
    {
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
        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id, supplierId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.BatchQuantity);
        Assert.Equal(10m, result.Value.TotalQuantity);
        Assert.Equal(supplierId, result.Value.SupplierId);

        await _itemRepository.Received(1).AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
        await _inventoryBatchRepository.Received(1).AddAsync(Arg.Is<InventoryBatch>(b => b.SupplierId == supplierId), Arg.Any<CancellationToken>());
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.In && t.Quantity == 10m), Arg.Any<CancellationToken>());
        await _inventoryRepository.Received(1).AddAsync(Arg.Any<DomainInventory>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExistingBatchAndInventory_IncrementsQuantities()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var item = Item.Create(shop.Id, "Rice", null, "kg", "111", true, actor.Id);

        var batchResult = InventoryBatch.Create(
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
            createdBy: actor.Id);
        Assert.False(batchResult.IsError);
        var batch = batchResult.Value;

        var inventoryResult = DomainInventory.Create(shop.Id, item.Id, quantity: 20m, reorderLevel: 2m, maxLevel: 50m, createdBy: actor.Id);
        Assert.False(inventoryResult.IsError);
        var inventory = inventoryResult.Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(item);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(item);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, item.Id, "B-1", Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, item.Id, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(15m, result.Value.BatchQuantity);
        Assert.Equal(30m, result.Value.TotalQuantity);

        _inventoryBatchRepository.Received(1).Update(Arg.Is<InventoryBatch>(b => b.Quantity == 15m));
        _inventoryRepository.Received(1).Update(Arg.Is<DomainInventory>(i => i.Quantity == 30m));
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private AddInventoryCommandHandler CreateHandler() =>
        new(_userRepository, _itemRepository, _inventoryBatchRepository, _stockTransactionRepository, _inventoryRepository, _unitOfWork);

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
