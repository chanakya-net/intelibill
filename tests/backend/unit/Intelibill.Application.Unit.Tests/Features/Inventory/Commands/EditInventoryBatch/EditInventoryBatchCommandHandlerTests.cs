using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.EditInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.EditInventoryBatch;

public class EditInventoryBatchCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly ISupplierLedgerEntryRepository _supplierLedgerEntryRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenBatchNotFound_ReturnsNotFound()
    {
        var actor = BuildActor(ShopRole.Manager);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(CreateCommand(actor.Id, actor.ShopMemberships[0].ShopId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenCostOrQuantityChanges_CreatesReversalAndCorrectedEntries()
    {
        var actor = BuildActor(ShopRole.Owner);
        var shopId = actor.ShopMemberships[0].ShopId;
        var item = Item.Create(shopId, "Rice", null, "kg", "111", true, actor.Id);
        var supplierId = Guid.NewGuid();

        var batchResult = InventoryBatch.Create(
            shopId,
            item.Id,
            "B-1",
            quantity: 10m,
            costPrice: 80m,
            mrp: 120m,
            salesPrice: 110m,
            taxRatePercent: 5m,
            taxIncluded: false,
            expiryDate: null,
            manufacturingDate: null,
            supplierId,
            createdBy: actor.Id);
        Assert.False(batchResult.IsError);
        var batch = batchResult.Value;

        var inventoryResult = DomainInventory.Create(shopId, item.Id, quantity: 25m, reorderLevel: 5m, maxLevel: 100m, createdBy: actor.Id);
        Assert.False(inventoryResult.IsError);
        var inventory = inventoryResult.Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shopId, item.Id, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var command = CreateCommand(actor.Id, shopId, batch.Id) with
        {
            Quantity = 12m,
            CostPrice = 85m,
            SupplierId = supplierId,
            EntryDate = new DateOnly(2026, 4, 6),
            Notes = "Correction"
        };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(27m, inventory.Quantity);

        await _supplierLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e =>
                e.EntryType == SupplierLedgerEntryType.RecordAdjusted
                && e.SupplierId == supplierId
                && e.BatchId == null
                && e.Amount == -800m),
            Arg.Any<CancellationToken>());

        await _supplierLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e =>
                e.EntryType == SupplierLedgerEntryType.GoodsReceived
                && e.SupplierId == supplierId
                && e.BatchId == batch.Id
                && e.Amount == 1020m),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoCorrectionRelevantChange_DoesNotCreateLedgerEntries()
    {
        var actor = BuildActor(ShopRole.Owner);
        var shopId = actor.ShopMemberships[0].ShopId;
        var item = Item.Create(shopId, "Rice", null, "kg", "111", true, actor.Id);

        var batchResult = InventoryBatch.Create(
            shopId,
            item.Id,
            "B-1",
            quantity: 10m,
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

        var inventoryResult = DomainInventory.Create(shopId, item.Id, quantity: 25m, reorderLevel: 5m, maxLevel: 100m, createdBy: actor.Id);
        Assert.False(inventoryResult.IsError);
        var inventory = inventoryResult.Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shopId, item.Id, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var command = CreateCommand(actor.Id, shopId, batch.Id) with
        {
            Quantity = 10m,
            CostPrice = 80m,
            SupplierId = null
        };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _supplierLedgerEntryRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private EditInventoryBatchCommandHandler CreateHandler() =>
        new(_userRepository, _inventoryBatchRepository, _inventoryRepository, _supplierLedgerEntryRepository, _unitOfWork);

    private static User BuildActor(ShopRole role)
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, role, true));
        return actor;
    }

    private static EditInventoryBatchCommand CreateCommand(Guid actorId, Guid shopId, Guid? batchId = null) =>
        new(
            actorId,
            shopId,
            batchId ?? Guid.NewGuid(),
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
            Notes: "Correction",
            EntryDate: new DateOnly(2026, 4, 6));
}
