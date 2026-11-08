using Intelibill.Application.Unit.Tests.Common.Time;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Application.Features.Inventory.Services;
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
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IBatchPriceRevisionRepository _revisionRepository = Substitute.For<IBatchPriceRevisionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly UpdateInventoryBatchCommandHandler _handler;

    public UpdateInventoryBatchCommandHandlerTests()
    {
        _handler = new UpdateInventoryBatchCommandHandler(
            _userRepository, _shopRepository, _batchRepository,
            new BatchRetailTermsReviser(_batchRepository, _revisionRepository, ExpiryTestDoubles.Dates()),
            _unitOfWork, _messageBus, ExpiryTestDoubles.Fence());
    }

    private static UpdateInventoryBatchCommand ValidCommand(Guid userId, Guid shopId, Guid batchId) =>
        new(batchId, userId, shopId, null, 10m, 100m, 200m, 180m, 18m, false, null, null, null, null, null);

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
    public async Task Handle_WhenPricingFieldsChange_RevisesSameBatchAndPublishesEvent()
    {
        var (user, shop, batch) = ArrangeBatch();
        var command = ValidCommand(user.Id, shop.Id, batch.Id) with { SalesPrice = 170m, Notes = "Festival price" };

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(batch.IsVoided);
        Assert.Equal(170m, batch.SalesPrice);
        Assert.Equal(10m, batch.Quantity);
        Assert.Equal(100m, batch.CostPrice);
        await _batchRepository.DidNotReceive().AddAsync(Arg.Any<InventoryBatch>(), Arg.Any<CancellationToken>());
        await _revisionRepository.Received(1).AddAsync(
            Arg.Is<BatchPriceRevision>(r =>
                r.InventoryBatchId == batch.Id &&
                r.PreviousSalesPrice == 180m &&
                r.NewSalesPrice == 170m &&
                r.Reason == "Festival price"),
            Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<InventoryBatchPricingChangedDomainEvent>(@event =>
                @event.ShopId == shop.Id &&
                @event.ItemId == batch.ItemId &&
                @event.BatchId == batch.Id));
    }

    [Fact]
    public async Task Handle_WhenOnlyDatesChange_RequiresVersionAndWritesNoPriceRevision()
    {
        var (user, shop, batch) = ArrangeBatch();
        var expiry = new DateOnly(2027, 1, 31);
        typeof(InventoryBatch).GetProperty(nameof(InventoryBatch.Version))!.SetValue(batch, 7u);
        var command = ValidCommand(user.Id, shop.Id, batch.Id) with { ExpiryDate = expiry, ExpectedVersion = 7, Notes = "Correct label" };

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(expiry, batch.ExpiryDate);
        _batchRepository.Received(1).Update(batch);
        await _revisionRepository.DidNotReceive().AddAsync(Arg.Any<BatchPriceRevision>(), Arg.Any<CancellationToken>());
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchPricingChangedDomainEvent>());
    }

    [Fact]
    public async Task Handle_WhenCommitFails_DoesNotPublishUncommittedPrice()
    {
        var (user, shop, batch) = ArrangeBatch();
        _unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("commit refused")));
        var command = ValidCommand(user.Id, shop.Id, batch.Id) with { SalesPrice = 170m, Notes = "Price correction" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, default));
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchPricingChangedDomainEvent>());
    }

    [Fact]
    public async Task Handle_WhenQuantityChanges_RefusesAndPointsToStockCount()
    {
        var (user, shop, batch) = ArrangeBatch();

        var result = await _handler.Handle(ValidCommand(user.Id, shop.Id, batch.Id) with { Quantity = 6m }, CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchQuantityEditMoved.Code, result.FirstError.Code);
        Assert.Equal(10m, batch.Quantity);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCostChanges_RefusesAndPointsToReceiptCorrection()
    {
        var (user, shop, batch) = ArrangeBatch();

        var result = await _handler.Handle(ValidCommand(user.Id, shop.Id, batch.Id) with { CostPrice = 110m }, CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchCostEditMoved.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSupplierChanges_RefusesAndPointsToReassignment()
    {
        var (user, shop, batch) = ArrangeBatch();

        var result = await _handler.Handle(ValidCommand(user.Id, shop.Id, batch.Id) with { SupplierId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchSupplierEditMoved.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenVersionIsStale_ReturnsVersionConflict()
    {
        var (user, shop, batch) = ArrangeBatch();

        var result = await _handler.Handle(ValidCommand(user.Id, shop.Id, batch.Id) with { ExpectedVersion = batch.Version + 1 }, CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchVersionConflict.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRenamedToActiveBatchNumber_ReturnsConflict()
    {
        var (user, shop, batch) = ArrangeBatch();
        var other = InventoryBatch.Create(shop.Id, batch.ItemId, "TAKEN", 1m, 1m, 2m, 2m, 0m, false, null, null, null, user.Id).Value;
        _batchRepository.GetByBatchNumberAsync(shop.Id, batch.ItemId, "TAKEN", Arg.Any<CancellationToken>()).Returns(other);

        var result = await _handler.Handle(ValidCommand(user.Id, shop.Id, batch.Id) with { NewBatchNumber = "TAKEN" }, CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchNumberAlreadyExists.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private (User User, Shop Shop, InventoryBatch Batch) ArrangeBatch()
    {
        var user = User.CreateWithEmail("owner@test.com", "pass", "Test", "Owner");
        var shop = Shop.Create("S", "A", "C", "S", "560001", null, null, null);
        var batch = InventoryBatch.Create(shop.Id, Guid.NewGuid(), "BATCH-001", 10m, 100m, 200m, 180m, 18m, false, null, null, null, user.Id).Value;

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        return (user, shop, batch);
    }
}
