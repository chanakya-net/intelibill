using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.CorrectStockCount;

public sealed class CorrectStockCountCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly IInventoryAdjustmentRepository _adjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();
    private readonly IStockCountRepository _stockCountRepository = Substitute.For<IStockCountRepository>();
    private readonly IInventoryAdjustmentNumberGenerator _numberGenerator = Substitute.For<IInventoryAdjustmentNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFinancialOperationExecutor _operationExecutor = Substitute.For<IFinancialOperationExecutor>();

    private readonly User _actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly InventoryBatch _batch;
    private readonly DomainInventory _inventory;

    public CorrectStockCountCommandHandlerTests()
    {
        _numberGenerator.Generate(Arg.Any<DateTimeOffset?>()).Returns("ADJ-20261101-ABCDEF12");
        _batch = InventoryBatch.Create(_shop.Id, _itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, _actor.Id).Value;
        _inventory = DomainInventory.Create(_shop.Id, _itemId, 15m, 2m, 50m, _actor.Id).Value;
        _batchRepository.GetByIdAsync(_batch.Id, Arg.Any<CancellationToken>()).Returns(_batch);
        _inventoryRepository.GetByItemAsync(_shop.Id, _itemId, Arg.Any<CancellationToken>()).Returns(_inventory);
        _userRepository.GetByIdWithDetailsAsync(_actor.Id, Arg.Any<CancellationToken>()).Returns(_actor);
    }

    [Fact]
    public async Task HandleAsync_WhenCountIsLower_PostsSignedOutMovementAndLinkedAdjustment()
    {
        UseRole(ShopRole.Manager);

        var result = await CreateHandler().HandleAsync(Command(8m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(-2m, result.Value.Delta);
        Assert.Equal(8m, _batch.Quantity);
        Assert.Equal(13m, _inventory.Quantity);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.Out && t.Quantity == -2m && t.PerformedBy == _actor.Id),
            Arg.Any<CancellationToken>());
        await _adjustmentRepository.Received(1).AddAsync(
            Arg.Is<InventoryAdjustment>(a => a.Direction == InventoryAdjustmentDirection.Decrease
                && a.Reason == InventoryAdjustmentReason.StockCountCorrection
                && a.Quantity == 2m
                && a.Notes == "Shelf count"),
            Arg.Any<CancellationToken>());
        await _stockCountRepository.Received(1).AddAsync(
            Arg.Is<StockCount>(c => c.Delta == -2m && c.InventoryAdjustmentId == result.Value.AdjustmentId && c.StockTransactionId == result.Value.StockTransactionId),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCountIsHigher_PostsSignedInMovement()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(12m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2m, result.Value.Delta);
        await _stockTransactionRepository.Received(1).AddAsync(
            Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.In && t.Quantity == 2m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCountMatches_RecordsConfirmationWithoutMovement()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(10m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(0m, result.Value.Delta);
        Assert.Null(result.Value.StockTransactionId);
        Assert.Null(result.Value.AdjustmentId);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _adjustmentRepository.DidNotReceive().AddAsync(Arg.Any<InventoryAdjustment>(), Arg.Any<CancellationToken>());
        _inventoryRepository.DidNotReceive().Update(Arg.Any<DomainInventory>());
        // The batch row is still written so the save is checked against the counted version.
        _batchRepository.Received(1).Update(_batch);
        await _stockCountRepository.Received(1).AddAsync(Arg.Is<StockCount>(c => c.Delta == 0m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExpectedVersionIsStale_RefusesWithoutChangingAnything()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(8m, expectedVersion: 99u), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchVersionConflict.Code, result.FirstError.Code);
        Assert.Equal(10m, _batch.Quantity);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveLosesRowVersionRace_ReturnsVersionConflict()
    {
        UseRole(ShopRole.Owner);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        var result = await CreateHandler().HandleAsync(Command(8m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchVersionConflict.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenCountExceedsAggregate_RefusesBeforeSaving()
    {
        UseRole(ShopRole.Owner);
        var shortInventory = DomainInventory.Create(_shop.Id, _itemId, 1m, 0m, 0m, _actor.Id).Value;
        _inventoryRepository.GetByItemAsync(_shop.Id, _itemId, Arg.Any<CancellationToken>()).Returns(shortInventory);

        var result = await CreateHandler().HandleAsync(Command(5m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.InsufficientStock", result.FirstError.Code);
        await _stockCountRepository.DidNotReceive().AddAsync(Arg.Any<StockCount>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AsStaff_IsForbidden()
    {
        UseRole(ShopRole.Staff);

        var result = await CreateHandler().HandleAsync(Command(8m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithoutReason_IsRefused(string reason)
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(8m) with { Reason = reason }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.StockCountReasonRequired.Code, result.FirstError.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.2345)]
    public async Task HandleAsync_WithInvalidQuantity_IsRefused(double counted)
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command((decimal)counted), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.StockCountQuantityInvalid.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WithOperationKey_RunsThroughExecutorScopedToStockCount()
    {
        UseRole(ShopRole.Owner);
        _operationExecutor
            .ExecuteAsync(
                Arg.Any<FinancialOperationScope>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<ErrorOr<StockCountResultDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<ErrorOr<StockCountResultDto>>>>(4)(CancellationToken.None));

        var result = await CreateHandler().HandleAsync(Command(8m) with { OperationKey = " count-1 " }, CancellationToken.None);

        Assert.False(result.IsError);
        await _operationExecutor.Received(1).ExecuteAsync(
            Arg.Is<FinancialOperationScope>(s => s.OperationType == FinancialOperationType.StockCount && s.Key == "count-1" && s.ShopId == _shop.Id),
            _actor.Id,
            _batch.Id,
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<ErrorOr<StockCountResultDto>>>>(),
            Arg.Any<CancellationToken>());
    }

    private void UseRole(ShopRole role) =>
        _actor.AddShopMembership(ShopMembership.Create(_shop.Id, _actor.Id, role, true));

    private CorrectStockCountCommand Command(decimal counted, uint? expectedVersion = null) =>
        new(_batch.Id, _actor.Id, _shop.Id, expectedVersion ?? _batch.Version, counted, "Shelf count", null);

    private CorrectStockCountCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _batchRepository,
            _inventoryRepository,
            _stockTransactionRepository,
            _adjustmentRepository,
            _stockCountRepository,
            _numberGenerator,
            _unitOfWork,
            _operationExecutor);
}
