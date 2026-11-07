using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.VoidBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Intelibill.Application.Unit.Tests.Common.Time;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Wolverine;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.VoidBatch;

public class VoidBatchCommandHandlerTests
{
    private readonly IPaymentReceiptRepository _paymentReceiptRepository = Substitute.For<IPaymentReceiptRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _inventoryBatchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ISupplierLedgerEntryRepository _supplierLedgerEntryRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IPurchaseReceiptValueRepository _purchaseReceiptValueRepository = Substitute.For<IPurchaseReceiptValueRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly IInventoryAdjustmentRepository _inventoryAdjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();

    public VoidBatchCommandHandlerTests()
    {
        _stockTransactionRepository.GetByBatchAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<StockTransaction>());
        _inventoryAdjustmentRepository.GetByBatchAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InventoryAdjustment>());
    }

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
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchVoidedDomainEvent>());
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
    public async Task HandleAsync_WhenFreeReceipt_VoidsWithoutZeroReversal()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var supplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        var batch = InventoryBatch.Create(shop.Id, itemId, "FREE-1", 5m, 0m, 120m, 110m, 5m, false, null, null, supplier.Id, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 5m, 0m, 0m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SupplierLedgerEntry>());

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.LedgerReversalAmount);
        Assert.True(batch.IsVoided);
        await _supplierLedgerEntryRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPayableRoundsToZeroReversal_RefusesInsteadOfLeavingItOwed()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var supplier = Supplier.CreateUnknownSystemSupplier(shop.Id);
        // ₹0.01 for three units: a unit cost of ₹0.00, but a ₹0.01 payable.
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 3m, 0m, 120m, 110m, 5m, false, null, null, supplier.Id, actor.Id).Value;
        var payable = SupplierLedgerEntry.Create(shop.Id, supplier.Id, batch.Id, SupplierLedgerEntryType.GoodsReceived, 0.01m, DateOnly.FromDateTime(DateTime.UtcNow), null, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _supplierLedgerEntryRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(new[] { payable });
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>())
            .Returns(DomainInventory.Create(shop.Id, itemId, 3m, 0m, 0m, actor.Id).Value);

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.SupplierLedgerEntryInvalid.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
    public async Task HandleAsync_WhenPartlySold_RefusesWithoutTouchingStockOrPayable()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, Guid.NewGuid(), actor.Id).Value;
        batch.SubtractQuantity(4m, actor.Id);
        var inventory = DomainInventory.Create(shop.Id, itemId, 6m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchConsumed.Code, result.FirstError.Code);
        Assert.False(batch.IsVoided);
        Assert.Equal(6m, inventory.Quantity);
        await _stockTransactionRepository.DidNotReceive().AddAsync(Arg.Any<StockTransaction>(), Arg.Any<CancellationToken>());
        await _supplierLedgerEntryRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSoldAndReturned_RefusesEvenThoughQuantityIsWhole()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var sale = StockTransaction.Create(shop.Id, itemId, batch.Id, StockTransactionType.Out, -2m, "INV-1", null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
        var returned = StockTransaction.Create(shop.Id, itemId, batch.Id, StockTransactionType.Ret, 2m, "RET-1", null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _stockTransactionRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns([sale, returned]);

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchConsumed.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenUnconsumedWithSupplier_ReversesWholeReceiptInOneSave()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var supplierId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, supplierId, actor.Id).Value;
        var receipt = StockTransaction.Create(shop.Id, itemId, batch.Id, StockTransactionType.In, 10m, "GRN-1", null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 30m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);
        _stockTransactionRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns([receipt]);

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.OriginalQuantity);
        Assert.Equal(10m, result.Value.RemainingQuantity);
        Assert.Equal(-800m, result.Value.LedgerReversalAmount);
        Assert.True(batch.IsVoided);
        Assert.Equal(20m, inventory.Quantity);

        await _stockTransactionRepository.Received(1).AddAsync(Arg.Is<StockTransaction>(t => t.TransactionType == StockTransactionType.Reversal && t.Quantity == -10m), Arg.Any<CancellationToken>());
        await _supplierLedgerEntryRepository.Received(1).AddAsync(Arg.Is<SupplierLedgerEntry>(e => e.EntryType == SupplierLedgerEntryType.Reversal && e.Amount == -800m), Arg.Any<CancellationToken>());
        _inventoryRepository.Received(1).Update(inventory);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<InventoryBatchVoidedDomainEvent>(@event =>
                @event.ShopId == shop.Id &&
                @event.ItemId == itemId &&
                @event.BatchId == batch.Id));
    }

    [Fact]
    public async Task HandleAsync_WhenInventoryGoesNegative_ReturnsInsufficientStockError()
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
        var result = await handler.HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Inventory.InsufficientStock", result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveLosesRowVersionRace_ReturnsConflictWithoutRetrying()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 30m, 2m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.ConcurrentChange.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchVoidedDomainEvent>());
    }

    [Fact]
    public async Task HandleAsync_WhenReceiptWasCorrected_ReversesEffectiveTotalToReceiptSupplierAndWritesReceipt()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var itemId = Guid.NewGuid();
        var receiptSupplierId = Guid.NewGuid();
        // The batch carries no supplier (legacy), but the receipt is owed to one.
        var batch = InventoryBatch.Create(shop.Id, itemId, "B-1", 3m, 33.33m, 120m, 110m, 0m, false, null, null, null, actor.Id).Value;
        var receiptValue = PurchaseReceiptValue.Create(shop.Id, batch.Id, itemId, receiptSupplierId, 3m, 100m, null, DateTimeOffset.UtcNow, actor.Id).Value;
        receiptValue.ApplyCostCorrection(110m);
        var movement = StockTransaction.Create(shop.Id, itemId, batch.Id, StockTransactionType.In, 3m, "GRN-1", null, DateTimeOffset.UtcNow, actor.Id, actor.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 3m, 0m, 50m, actor.Id).Value;

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>()).Returns(inventory);
        _stockTransactionRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns([movement]);
        _purchaseReceiptValueRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>()).Returns(receiptValue);

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(-110m, result.Value.LedgerReversalAmount);
        await _supplierLedgerEntryRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e => e.EntryType == SupplierLedgerEntryType.Reversal && e.Amount == -110m && e.SupplierId == receiptSupplierId),
            Arg.Any<CancellationToken>());
        _purchaseReceiptValueRepository.Received(1).Update(receiptValue);
    }

    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();
    private VoidBatchCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _inventoryBatchRepository,
            _stockTransactionRepository,
            _inventoryAdjustmentRepository,
            _supplierLedgerEntryRepository,
            _purchaseReceiptValueRepository,
            _paymentReceiptRepository,
            _inventoryRepository,
            _unitOfWork,
            _messageBus,
            BusinessTestDoubles.AnyShop(),
            _postingPeriods,
            BusinessTestDoubles.RealClock());

    [Fact]
    public async Task ClosedPeriod_RefusesVoid_AndPublishesNothing()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "B-1", 10m, 80m, 120m, 110m, 5m, false, null, null, null, actor.Id).Value;
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _inventoryBatchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _postingPeriods.AuthorizeForWriteAsync(shop.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<PostingPeriodLease>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 7, 1))));

        var result = await CreateHandler().HandleAsync(new VoidBatchCommand(batch.Id, actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        Assert.False(batch.IsVoided);
        await _inventoryBatchRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchVoidedDomainEvent>());
    }
}
