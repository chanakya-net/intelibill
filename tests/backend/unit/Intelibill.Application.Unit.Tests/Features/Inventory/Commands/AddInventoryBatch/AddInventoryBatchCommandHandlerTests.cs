using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.AddInventoryBatch;

public class AddInventoryBatchCommandHandlerTests
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
    public async Task HandleAsync_WhenMoreThanHundredRows_ReturnsValidationError()
    {
        SetupSystemSupplierLookup();
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
    public async Task HandleAsync_WhenStaffRole_ReturnsForbidden()
    {
        SetupSystemSupplierLookup();
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
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var existingItem = Item.Create(shop.Id, "Rice", "Desc", "kg", "111", true, actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, "111", Arg.Any<CancellationToken>()).Returns(existingItem);
        _itemRepository.GetByNameAsync(shop.Id, "Rice", Arg.Any<CancellationToken>()).Returns(existingItem);
        _itemRepository.GetByNameAsync(shop.Id, "Different Name", Arg.Any<CancellationToken>()).Returns((Item?)null);
        _inventoryBatchRepository.GetByBatchNumberAsync(shop.Id, Arg.Any<Guid>(), "B-1", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((DomainInventory?)null);

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
        SetupSystemSupplierLookup();
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemRepository.GetByBarcodeAsync(shop.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Item?)null);
        _itemRepository.GetByNameAsync(shop.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Item?)null);

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

    private AddInventoryBatchCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _supplierRepository,
            _itemRepository,
            _inventoryBatchRepository,
            _stockTransactionRepository,
            _supplierLedgerEntryRepository,
            _inventoryRepository,
            _unitOfWork);

    private void SetupSystemSupplierLookup()
    {
        _supplierRepository.GetSystemByShopIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Supplier.CreateUnknownSystemSupplier(callInfo.Arg<Guid>()));
    }

    private static AddInventoryBatchRowCommand CreateRow(string clientRowId, string itemName, string barcode) =>
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
            PerformedAt: null);
}