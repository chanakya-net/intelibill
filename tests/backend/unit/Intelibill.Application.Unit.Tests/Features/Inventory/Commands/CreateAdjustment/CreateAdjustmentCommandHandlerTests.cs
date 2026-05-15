using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.CreateAdjustment;

public sealed class CreateAdjustmentCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly IInventoryAdjustmentRepository _adjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();
    private readonly IInventoryAdjustmentNumberGenerator _numberGenerator = Substitute.For<IInventoryAdjustmentNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public CreateAdjustmentCommandHandlerTests()
    {
        _numberGenerator.Generate(Arg.Any<DateTimeOffset?>()).Returns("ADJ-20260505-ABCDEF12");
    }

    [Fact]
    public async Task HandleAsync_DecreaseDamagedStock_UpdatesBatchInventoryAndPersistsAdjustment()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 15m, 2m, 50m, actor.Id).Value;
        var performedAt = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new CreateAdjustmentCommand(
                batch.Id,
                actor.Id,
                shop.Id,
                InventoryAdjustmentDirection.Decrease,
                InventoryAdjustmentReason.Damaged,
                3m,
                performedAt,
                "Damaged in storage"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("ADJ-20260505-ABCDEF12", result.Value.AdjustmentNumber);
        Assert.Equal(10m, result.Value.BatchQuantityBefore);
        Assert.Equal(7m, result.Value.BatchQuantityAfter);
        Assert.Equal(15m, result.Value.InventoryQuantityBefore);
        Assert.Equal(12m, result.Value.InventoryQuantityAfter);
        Assert.Equal(240m, result.Value.CostImpact);
        Assert.Equal(performedAt, result.Value.PerformedAt);

        Assert.Equal(7m, batch.Quantity);
        Assert.Equal(12m, inventory.Quantity);
        _batchRepository.Received(1).Update(batch);
        _inventoryRepository.Received(1).Update(inventory);

        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t =>
                t.TransactionType == StockTransactionType.Dmg
                && t.Quantity == -3m
                && t.ReferenceNumber == "ADJ-20260505-ABCDEF12"
                && t.PerformedAt == performedAt),
            Arg.Any<CancellationToken>());

        await _adjustmentRepository.Received(1).AddAsync(
            Arg.Is<InventoryAdjustment>(a =>
                a.AdjustmentNumber == "ADJ-20260505-ABCDEF12"
                && a.Direction == InventoryAdjustmentDirection.Decrease
                && a.Reason == InventoryAdjustmentReason.Damaged
                && a.BatchQuantityBefore == 10m
                && a.BatchQuantityAfter == 7m
                && a.InventoryQuantityBefore == 15m
                && a.InventoryQuantityAfter == 12m
                && a.CostImpact == 240m),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNonUtcPerformedAt_NormalizesPersistedTimestampsToUtc()
    {
        var actor = User.CreateWithEmail("owner-offset@test.com", "hash", "Owner", "Offset");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-OFFSET", 20m, 30m, 60m, 50m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 20m, 2m, 50m, actor.Id).Value;
        var localPerformedAt = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.FromHours(5.5));
        var expectedUtc = localPerformedAt.ToUniversalTime();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new CreateAdjustmentCommand(
                batch.Id,
                actor.Id,
                shop.Id,
                InventoryAdjustmentDirection.Increase,
                InventoryAdjustmentReason.StockCountCorrection,
                4m,
                localPerformedAt,
                "Updated quantity"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(expectedUtc, result.Value.PerformedAt);
        Assert.Equal(TimeSpan.Zero, result.Value.PerformedAt.Offset);

        _numberGenerator.Received(1).Generate(expectedUtc);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t =>
                t.PerformedAt == expectedUtc
                && t.PerformedAt.Offset == TimeSpan.Zero),
            Arg.Any<CancellationToken>());
        await _adjustmentRepository.Received(1).AddAsync(
            Arg.Is<InventoryAdjustment>(a =>
                a.PerformedAt == expectedUtc
                && a.PerformedAt.Offset == TimeSpan.Zero),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DecreaseMoreThanBatchQuantity_ReturnsInsufficientBatchStock()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 2m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 10m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new CreateAdjustmentCommand(
                batch.Id,
                actor.Id,
                shop.Id,
                InventoryAdjustmentDirection.Decrease,
                InventoryAdjustmentReason.MissingLost,
                3m,
                null,
                "Count mismatch"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.AdjustmentQuantityExceedsBatchQuantity.Code, result.FirstError.Code);
        Assert.Equal(2m, batch.Quantity);
        Assert.Equal(10m, inventory.Quantity);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _adjustmentRepository.DidNotReceive().AddAsync(Arg.Any<InventoryAdjustment>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_IncreaseFoundStockOnZeroQuantityBatch_UpdatesBatchInventoryAndCreatesInboundTransaction()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-ZERO", 0m, 50m, 80m, 70m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 0m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new CreateAdjustmentCommand(
                batch.Id,
                actor.Id,
                shop.Id,
                InventoryAdjustmentDirection.Increase,
                InventoryAdjustmentReason.FoundStock,
                4m,
                null,
                "Found during stock count"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(0m, result.Value.BatchQuantityBefore);
        Assert.Equal(4m, result.Value.BatchQuantityAfter);
        Assert.Equal(0m, result.Value.InventoryQuantityBefore);
        Assert.Equal(4m, result.Value.InventoryQuantityAfter);
        Assert.Equal(200m, result.Value.CostImpact);

        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.In && t.Quantity == 4m),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsStaff_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new CreateAdjustmentCommand(
                Guid.NewGuid(),
                actor.Id,
                shop.Id,
                InventoryAdjustmentDirection.Increase,
                InventoryAdjustmentReason.FoundStock,
                1m,
                null,
                null),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Theory]
    [InlineData(InventoryAdjustmentDirection.Increase, InventoryAdjustmentReason.FoundStock)]
    [InlineData(InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged)]
    public async Task HandleAsync_WhenBatchIsVoided_ReturnsBatchAlreadyVoided(
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason)
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "B-VOID", 5m, 50m, 80m, 70m, 5m, false, null, null, null, actor.Id).Value;
        batch.Void(actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        var handler = CreateHandler();
        var result = await handler.HandleAsync(
            new CreateAdjustmentCommand(
                batch.Id,
                actor.Id,
                shop.Id,
                direction,
                reason,
                1m,
                null,
                "Voided batch"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchAlreadyVoided.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CreateAdjustmentCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _batchRepository,
            _inventoryRepository,
            _stockTransactionRepository,
            _adjustmentRepository,
            _numberGenerator,
            _unitOfWork);
}
