using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.UpdateBatchPrices;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Events;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Wolverine;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.UpdateBatchPrices;

public sealed class UpdateBatchPricesCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IBatchPriceRevisionRepository _revisionRepository = Substitute.For<IBatchPriceRevisionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();

    private readonly User _actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
    private readonly InventoryBatch _batch;

    public UpdateBatchPricesCommandHandlerTests()
    {
        _batch = InventoryBatch.Create(_shop.Id, Guid.NewGuid(), "B-1", 10m, 100m, 150m, 140m, 0m, false, null, null, Guid.NewGuid(), _actor.Id).Value;
        _userRepository.GetByIdWithDetailsAsync(_actor.Id, Arg.Any<CancellationToken>()).Returns(_actor);
        _batchRepository.GetByIdAsync(_batch.Id, Arg.Any<CancellationToken>()).Returns(_batch);
    }

    [Fact]
    public async Task HandleAsync_PriceChange_RevisesSameBatchAndRecordsRevision()
    {
        UseRole(ShopRole.Manager);

        var result = await CreateHandler().HandleAsync(Command(salesPrice: 130m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(_batch.Id, result.Value.BatchId);
        Assert.NotNull(result.Value.PriceRevisionId);
        Assert.Equal(130m, _batch.SalesPrice);
        Assert.Equal(10m, _batch.Quantity);
        Assert.Equal(100m, _batch.CostPrice);
        await _revisionRepository.Received(1).AddAsync(Arg.Is<BatchPriceRevision>(r => r.NewSalesPrice == 130m), Arg.Any<CancellationToken>());
        await _messageBus.Received(1).PublishAsync(Arg.Is<InventoryBatchPricingChangedDomainEvent>(e => e.BatchId == _batch.Id));
    }

    [Fact]
    public async Task HandleAsync_NothingChanged_StillWritesBatchForVersionCheck()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(salesPrice: 140m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.PriceRevisionId);
        _batchRepository.Received(1).Update(_batch);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchPricingChangedDomainEvent>());
    }

    [Fact]
    public async Task HandleAsync_StaleVersion_Conflicts()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(salesPrice: 130m) with { ExpectedVersion = _batch.Version + 1 }, CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchVersionConflict.Code, result.FirstError.Code);
        Assert.Equal(140m, _batch.SalesPrice);
    }

    [Fact]
    public async Task HandleAsync_LosingTheSaveRace_Conflicts()
    {
        UseRole(ShopRole.Owner);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        var result = await CreateHandler().HandleAsync(Command(salesPrice: 130m), CancellationToken.None);

        Assert.Equal(Errors.Inventory.BatchVersionConflict.Code, result.FirstError.Code);
        await _messageBus.DidNotReceive().PublishAsync(Arg.Any<InventoryBatchPricingChangedDomainEvent>());
    }

    [Fact]
    public async Task HandleAsync_ByStaff_IsForbidden()
    {
        UseRole(ShopRole.Staff);

        var result = await CreateHandler().HandleAsync(Command(salesPrice: 130m), CancellationToken.None);

        Assert.Equal(Errors.Inventory.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_SaleAboveMrp_IsRefused()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(salesPrice: 160m), CancellationToken.None);

        Assert.True(result.IsError);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void UseRole(ShopRole role) =>
        _actor.AddShopMembership(ShopMembership.Create(_shop.Id, _actor.Id, role, true));

    private UpdateBatchPricesCommand Command(decimal salesPrice) =>
        new(_batch.Id, _actor.Id, _shop.Id, _batch.Version, 150m, salesPrice, 0m, false, null, null, null, "Price change");

    private UpdateBatchPricesCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _batchRepository,
            new BatchRetailTermsReviser(_batchRepository, _revisionRepository),
            _unitOfWork,
            _messageBus);
}
