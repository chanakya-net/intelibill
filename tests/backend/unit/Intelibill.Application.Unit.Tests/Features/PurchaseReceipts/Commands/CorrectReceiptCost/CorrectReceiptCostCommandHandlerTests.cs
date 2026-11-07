using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Intelibill.Application.Unit.Tests.Common.Time;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseReceipts.Commands.CorrectReceiptCost;

public sealed class CorrectReceiptCostCommandHandlerTests
{
    private readonly IPaymentReceiptRepository _paymentReceiptRepository = Substitute.For<IPaymentReceiptRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseReceiptValueRepository _receiptRepository = Substitute.For<IPurchaseReceiptValueRepository>();
    private readonly IPurchaseReceiptCostAdjustmentRepository _adjustmentRepository = Substitute.For<IPurchaseReceiptCostAdjustmentRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFinancialOperationExecutor _operationExecutor = Substitute.For<IFinancialOperationExecutor>();

    private readonly User _actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
    private readonly Shop _shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly InventoryBatch _batch;
    private readonly PurchaseReceiptValue _receipt;

    public CorrectReceiptCostCommandHandlerTests()
    {
        _batch = InventoryBatch.Create(_shop.Id, Guid.NewGuid(), "B-1", 10m, 100m, 150m, 140m, 0m, false, null, null, _supplierId, _actor.Id).Value;
        _receipt = PurchaseReceiptValue.Create(_shop.Id, _batch.Id, _batch.ItemId, _supplierId, 10m, 1000m, null, DateTimeOffset.UtcNow, _actor.Id).Value;
        _batch.SubtractQuantity(4m, _actor.Id);
        _userRepository.GetByIdWithDetailsAsync(_actor.Id, Arg.Any<CancellationToken>()).Returns(_actor);
        _receiptRepository.GetByIdAsync(_receipt.Id, Arg.Any<CancellationToken>()).Returns(_receipt);
        _batchRepository.GetByIdAsync(_batch.Id, Arg.Any<CancellationToken>()).Returns(_batch);
    }

    [Fact]
    public async Task HandleAsync_PartlySoldReceipt_PostsOnlyTheDelta()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(1100m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(100m, result.Value.Delta);
        Assert.Equal(1000m, result.Value.PriorTotalCost);
        Assert.Equal(1100m, _receipt.EffectiveTotalCost);
        Assert.Equal(6m, _batch.Quantity);
        Assert.Equal(100m, _batch.CostPrice);
        await _ledgerRepository.Received(1).AddAsync(
            Arg.Is<SupplierLedgerEntry>(e => e.EntryType == SupplierLedgerEntryType.CostAdjusted && e.Amount == 100m && e.BatchId == _batch.Id && e.SupplierId == _supplierId),
            Arg.Any<CancellationToken>());
        await _adjustmentRepository.Received(1).AddAsync(
            Arg.Is<PurchaseReceiptCostAdjustment>(a => a.Delta == 100m && a.SupplierLedgerEntryId == result.Value.SupplierLedgerEntryId),
            Arg.Any<CancellationToken>());
        _receiptRepository.Received(1).Update(_receipt);
        _batchRepository.Received(1).Update(_batch);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ByManager_IsForbidden()
    {
        UseRole(ShopRole.Manager);

        var result = await CreateHandler().HandleAsync(Command(1100m), CancellationToken.None);

        Assert.Equal(Errors.Inventory.UserIsNotOwner.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StaleVersion_Conflicts()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(1100m, expectedVersion: _receipt.Version + 1), CancellationToken.None);

        Assert.Equal(Errors.PurchaseReceipt.VersionConflict.Code, result.FirstError.Code);
        Assert.Equal(1000m, _receipt.EffectiveTotalCost);
    }

    [Fact]
    public async Task HandleAsync_LosingTheSaveRace_Conflicts()
    {
        UseRole(ShopRole.Owner);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateConcurrencyException("conflict", new List<IUpdateEntry>()));

        var result = await CreateHandler().HandleAsync(Command(1100m), CancellationToken.None);

        Assert.Equal(Errors.PurchaseReceipt.VersionConflict.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_VoidedBatch_IsRefused()
    {
        UseRole(ShopRole.Owner);
        _batch.Void(_actor.Id);

        var result = await CreateHandler().HandleAsync(Command(1100m), CancellationToken.None);

        Assert.Equal(Errors.PurchaseReceipt.BatchVoided.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_UnchangedTotal_IsRefused()
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(1000m), CancellationToken.None);

        Assert.Equal(Errors.PurchaseReceipt.CostUnchanged.Code, result.FirstError.Code);
        await _ledgerRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", 1100)]
    [InlineData("Invoice", -1)]
    [InlineData("Invoice", 10.001)]
    public async Task HandleAsync_InvalidInput_IsRefusedBeforeLoading(string reason, decimal revised)
    {
        UseRole(ShopRole.Owner);

        var result = await CreateHandler().HandleAsync(Command(revised) with { Reason = reason }, CancellationToken.None);

        Assert.True(result.IsError);
        await _receiptRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_OtherShopsReceipt_IsNotFound()
    {
        UseRole(ShopRole.Owner);
        var foreign = PurchaseReceiptValue.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 1m, 10m, null, DateTimeOffset.UtcNow, _actor.Id).Value;
        _receiptRepository.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await CreateHandler().HandleAsync(Command(1100m) with { ReceiptValueId = foreign.Id }, CancellationToken.None);

        Assert.Equal(Errors.PurchaseReceipt.NotFound.Code, result.FirstError.Code);
    }

    private void UseRole(ShopRole role) =>
        _actor.AddShopMembership(ShopMembership.Create(_shop.Id, _actor.Id, role, true));

    private CorrectReceiptCostCommand Command(decimal revised, uint? expectedVersion = null) =>
        new(_receipt.Id, _actor.Id, _shop.Id, expectedVersion ?? _receipt.Version, revised, "Supplier invoice", null);

    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();
    private CorrectReceiptCostCommandHandler CreateHandler() =>
        new(
            _userRepository,
            _receiptRepository,
            _adjustmentRepository,
            _batchRepository,
            _ledgerRepository,
            _paymentReceiptRepository,
            _unitOfWork,
            _operationExecutor,
            BusinessTestDoubles.AnyShop(),
            _postingPeriods,
            BusinessTestDoubles.RealClock());

    [Fact]
    public async Task ClosedPeriod_RefusesCorrection_WithoutPostingTheDelta()
    {
        UseRole(ShopRole.Owner);
        _postingPeriods.AuthorizeForWriteAsync(_shop.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr.ErrorOr<PostingPeriodLease>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 7, 1))));

        var result = await CreateHandler().HandleAsync(Command(1100m), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        Assert.Equal(1000m, _receipt.EffectiveTotalCost);
        await _ledgerRepository.DidNotReceive().AddAsync(Arg.Any<SupplierLedgerEntry>(), Arg.Any<CancellationToken>());
        await _adjustmentRepository.DidNotReceive().AddAsync(Arg.Any<PurchaseReceiptCostAdjustment>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }
}
