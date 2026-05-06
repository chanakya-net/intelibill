using Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.VoidAdjustment;

public sealed class VoidAdjustmentCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryAdjustmentRepository _adjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_OwnerVoidsDecreaseAdjustment_RestoresStockAndPersistsReversal()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        batch.SubtractQuantity(3m, actor.Id);
        var inventory = DomainInventory.Create(shop.Id, itemId, 7m, 2m, 50m, actor.Id).Value;
        var performedAt = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero);
        var adjustment = InventoryAdjustment.Create(
            shop.Id,
            itemId,
            batch.Id,
            "ADJ-20260501-ABCDEF12",
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            3m,
            80m,
            240m,
            10m,
            7m,
            10m,
            7m,
            performedAt,
            actor.Id,
            "Damaged in storage",
            actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _adjustmentRepository.GetByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(adjustment);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new VoidAdjustmentCommand(adjustment.Id, actor.Id, shop.Id, "Entered twice"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(adjustment.Id, result.Value.AdjustmentId);
        Assert.Equal(10m, result.Value.BatchQuantityAfter);
        Assert.Equal(10m, result.Value.InventoryQuantityAfter);
        Assert.True(adjustment.IsVoided);
        Assert.Equal("Entered twice", adjustment.VoidReason);
        Assert.Equal(actor.Id, adjustment.VoidedBy);
        Assert.NotNull(adjustment.ReversalStockTransactionId);
        Assert.Equal(10m, batch.Quantity);
        Assert.Equal(10m, inventory.Quantity);

        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t =>
                t.TransactionType == StockTransactionType.Reversal
                && t.Quantity == 3m
                && t.ReferenceNumber == adjustment.AdjustmentNumber
                && t.Notes == "Entered twice"),
            Arg.Any<CancellationToken>());
        _adjustmentRepository.Received(1).Update(adjustment);
        _batchRepository.Received(1).Update(batch);
        _inventoryRepository.Received(1).Update(inventory);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_OwnerVoidsIncreaseAdjustment_SubtractsStockAndPersistsNegativeReversal()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 10m, 2m, 50m, actor.Id).Value;
        var adjustment = CreateAdjustment(
            shop.Id,
            itemId,
            batch.Id,
            actor.Id,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            3m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _adjustmentRepository.GetByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(adjustment);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var result = await CreateHandler().HandleAsync(
            new VoidAdjustmentCommand(adjustment.Id, actor.Id, shop.Id, "Found stock not valid"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(7m, result.Value.BatchQuantityAfter);
        Assert.Equal(7m, result.Value.InventoryQuantityAfter);
        Assert.Equal(7m, batch.Quantity);
        Assert.Equal(7m, inventory.Quantity);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t =>
                t.TransactionType == StockTransactionType.Reversal
                && t.Quantity == -3m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ShopRole.Manager)]
    [InlineData(ShopRole.Staff)]
    public async Task HandleAsync_WhenActorIsNotOwner_ReturnsForbidden(ShopRole role)
    {
        var actor = User.CreateWithEmail("member@test.com", "hash", "Member", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, role, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new VoidAdjustmentCommand(Guid.NewGuid(), actor.Id, shop.Id, "No access"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.UserIsNotOwner.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAdjustmentAlreadyVoided_ReturnsAlreadyVoided()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var adjustment = CreateAdjustment(shop.Id, batch.ItemId, batch.Id, actor.Id, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 3m);
        adjustment.Void(DateTimeOffset.UtcNow, actor.Id, "Already fixed", Guid.NewGuid());

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _adjustmentRepository.GetByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(adjustment);

        var result = await CreateHandler().HandleAsync(
            new VoidAdjustmentCommand(adjustment.Id, actor.Id, shop.Id, "Try again"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.AdjustmentAlreadyVoided.Code, result.FirstError.Code);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAdjustmentBelongsToOtherShop_ReturnsNotFound()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var activeShop = Shop.Create("Active", "Address", "City", "State", "560001", null, null, null);
        var otherShop = Shop.Create("Other", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(activeShop.Id, actor.Id, ShopRole.Owner, true));
        var batch = InventoryBatch.Create(otherShop.Id, Guid.NewGuid(), "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var adjustment = CreateAdjustment(otherShop.Id, batch.ItemId, batch.Id, actor.Id, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 3m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _adjustmentRepository.GetByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(adjustment);

        var result = await CreateHandler().HandleAsync(
            new VoidAdjustmentCommand(adjustment.Id, actor.Id, activeShop.Id, "Wrong shop"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.AdjustmentNotFound.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReasonIsBlank_ReturnsValidationError()
    {
        var result = await CreateHandler().HandleAsync(
            new VoidAdjustmentCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " "),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.AdjustmentVoidReasonRequired.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenIncreaseVoidWouldMakeInventoryNegative_ReturnsConflictWithoutMutatingStock()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 5m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 1m, 2m, 50m, actor.Id).Value;
        var adjustment = CreateAdjustment(
            shop.Id,
            itemId,
            batch.Id,
            actor.Id,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            3m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _adjustmentRepository.GetByIdAsync(adjustment.Id, Arg.Any<CancellationToken>()).Returns(adjustment);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var result = await CreateHandler().HandleAsync(
            new VoidAdjustmentCommand(adjustment.Id, actor.Id, shop.Id, "Invalid found stock"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.InsufficientStock", result.FirstError.Code);
        Assert.Equal(5m, batch.Quantity);
        Assert.Equal(1m, inventory.Quantity);
        Assert.False(adjustment.IsVoided);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private VoidAdjustmentCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _adjustmentRepository,
            _batchRepository,
            _inventoryRepository,
            _stockTransactionRepository,
            _unitOfWork);

    private static InventoryAdjustment CreateAdjustment(
        Guid shopId,
        Guid itemId,
        Guid batchId,
        Guid actorId,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity)
    {
        const decimal quantityBefore = 7m;
        var quantityAfter = direction == InventoryAdjustmentDirection.Increase
            ? quantityBefore + quantity
            : quantityBefore - quantity;

        return InventoryAdjustment.Create(
            shopId,
            itemId,
            batchId,
            "ADJ-20260501-ABCDEF12",
            direction,
            reason,
            quantity,
            80m,
            decimal.Round(quantity * 80m, 2, MidpointRounding.AwayFromZero),
            quantityBefore,
            quantityAfter,
            quantityBefore,
            quantityAfter,
            new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero),
            actorId,
            "Stock count",
            actorId).Value;
    }
}
