using ErrorOr;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using Wolverine;
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.UpdateInventoryBatch;

public class UpdateInventoryBatchCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly UpdateInventoryBatchCommandHandler _handler;

    public UpdateInventoryBatchCommandHandlerTests()
    {
        _handler = new UpdateInventoryBatchCommandHandler(
            _userRepository, _shopRepository, _supplierRepository,
            _batchRepository, _inventoryRepository, _stockTransactionRepository,
            _ledgerRepository, _unitOfWork, _messageBus);
    }

    private static UpdateInventoryBatchCommand ValidCommand(Guid userId, Guid shopId, Guid batchId) =>
        new(batchId, userId, shopId, "BATCH-CORR", 10m, 100m, 200m, 180m, 18m, false, null, null, null, null, null);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.Handle(ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var result = await _handler.Handle(ValidCommand(userId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await _handler.Handle(ValidCommand(userId, shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenMembershipRoleIsStaff_ReturnsForbiddenError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Staff, false));

        var result = await _handler.Handle(ValidCommand(userId, shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenBatchNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var result = await _handler.Handle(ValidCommand(userId, shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenBatchAlreadyVoided_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var batchResult = InventoryBatch.Create(shopId, itemId, "BATCH-001", 10m, 100m, 200m, 180m, 18m, false, null, null, null, userId);
        var batch = batchResult.Value;
        batch.Void(userId);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        var result = await _handler.Handle(ValidCommand(userId, shopId, batch.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenPricingFieldsChange_PublishesPricingChangedEvent()
    {
        var user = User.CreateWithEmail("owner@test.com", "pass", "Test", "Owner");
        var shop = Shop.Create("S", "A", "C", "S", "560001", null, null, null);
        var itemId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var batch = InventoryBatch.Create(
            shop.Id,
            itemId,
            "BATCH-001",
            10m,
            100m,
            200m,
            180m,
            18m,
            false,
            null,
            null,
            supplierId,
            user.Id).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _batchRepository.GetByBatchNumberAsync(shop.Id, itemId, "BATCH-CORR", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);
        _ledgerRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>())
            .Returns([]);

        var command = ValidCommand(user.Id, shop.Id, batch.Id) with
        {
            SalesPrice = 170m,
            SupplierId = supplierId
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<InventoryBatchPricingChangedDomainEvent>(@event =>
                @event.ShopId == shop.Id &&
                @event.ItemId == itemId &&
                @event.BatchId != Guid.Empty));
    }

    [Fact]
    public async Task Handle_WhenOnlyQuantityChanges_DoesNotPublishPricingChangedEvent()
    {
        var user = User.CreateWithEmail("owner@test.com", "pass", "Test", "Owner");
        var shop = Shop.Create("S", "A", "C", "S", "560001", null, null, null);
        var itemId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var batch = InventoryBatch.Create(
            shop.Id,
            itemId,
            "BATCH-001",
            10m,
            100m,
            200m,
            180m,
            18m,
            false,
            null,
            null,
            supplierId,
            user.Id).Value;
        var inventory = DomainInventory.Create(shop.Id, itemId, 10m, 2m, 50m, user.Id).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _batchRepository.GetByBatchNumberAsync(shop.Id, itemId, "BATCH-CORR", Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);
        _inventoryRepository.GetByItemAsync(shop.Id, itemId, Arg.Any<CancellationToken>())
            .Returns(inventory);
        _ledgerRepository.GetByBatchAsync(shop.Id, batch.Id, Arg.Any<CancellationToken>())
            .Returns([]);

        var command = ValidCommand(user.Id, shop.Id, batch.Id) with
        {
            Quantity = 12m,
            SupplierId = supplierId
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        await _messageBus.DidNotReceive().PublishAsync(
            Arg.Any<InventoryBatchPricingChangedDomainEvent>());
    }
}
