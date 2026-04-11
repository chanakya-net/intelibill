using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.VoidBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.VoidBatch;

public class VoidBatchCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ISupplierLedgerEntryRepository _supplierLedgerEntryRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenBatchNotFound_ReturnsError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((InventoryBatch?)null);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new VoidBatchCommand(Guid.NewGuid(), actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenNoSupplier_SkipsLedgerEntry()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 10m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.LedgerReversalAmount);
        await _supplierLedgerEntryRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoSalesYet_SubtractsOriginalQuantity()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 10m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.RemainingQuantity);
        Assert.Equal(0m, inventory.Quantity);
    }

    [Fact]
    public async Task HandleAsync_VerifiesPerformedAtIsSet()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 10m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        await handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t => t.PerformedAt != default), 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenBatchAlreadyVoided_ReturnsError()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        batch.Void(actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchAlreadyVoided.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_WithPartialSalesAndSupplier_UpdatesAllRecords()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var supplierId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, supplierId, actor.Id).Value;
        
        // Simulate some sales: original 10, remaining 6
        typeof(InventoryBatch).GetProperty(nameof(InventoryBatch.Quantity))!.SetValue(batch, 6m);

        var inventory = DomainInventory.Create(shop.Id, itemId, 30m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.OriginalQuantity);
        Assert.Equal(6m, result.Value.RemainingQuantity);
        Assert.Equal(-800m, result.Value.LedgerReversalAmount);

        Assert.True(batch.IsVoided);
        Assert.Equal(0m, batch.Quantity);
        Assert.Equal(24m, inventory.Quantity); // 30 - 6 = 24

        _inventoryBatchRepository.Received(1).Update(batch);
        await _stockTransactionRepository.Received(1).AddAsync(Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.Reversal && t.Quantity == -10m), Arg.Any<CancellationToken>());
        await _supplierLedgerEntryRepository.Received(1).AddAsync(Arg.Is<SupplierLedgerEntry>(e => e.EntryType == SupplierLedgerEntryType.Reversal && e.Amount == -800m), Arg.Any<CancellationToken>());
        _inventoryRepository.Received(1).Update(inventory);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInventoryGoesNegative_ThrowsInvalidOperationException()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        
        var inventory = DomainInventory.Create(shop.Id, itemId, 5m, 2m, 50m, actor.Id).Value; // Only 5 in inventory, but 10 in batch

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None));
    }

    private VoidBatchCommandHandler CreateHandler() =>
        new(_userRepository, _inventoryBatchRepository, _stockTransactionRepository, _supplierLedgerEntryRepository, _inventoryRepository, _unitOfWork);
}