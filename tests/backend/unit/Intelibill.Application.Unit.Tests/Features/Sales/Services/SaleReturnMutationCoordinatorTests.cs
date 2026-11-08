using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Unit.Tests.Common;
using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Unit.Tests.Common.Time;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public sealed class SaleReturnMutationCoordinatorTests
{
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Guid _shopId = Guid.NewGuid();
    private readonly Guid _saleId = Guid.NewGuid();
    private int _mutationRuns;

    private readonly IPostingPeriodPolicy _postingPeriods = Substitute.For<IPostingPeriodPolicy>();

    private SaleReturnMutationCoordinator CreateCoordinator() =>
        new(_saleRepository, BusinessTestDoubles.AnyShop(), _postingPeriods, BusinessTestDoubles.RealClock(), _unitOfWork, Substitute.For<IExpiryPolicyFence>());

    private void LockReturns(RowLockOutcome outcome) =>
        _saleRepository.LockForReturnMutationAsync(_shopId, _saleId, Arg.Any<CancellationToken>()).Returns(outcome);

    private Task<ErrorOr<Success>> RunAsync(Func<ErrorOr<Success>> mutation) =>
        CreateCoordinator().ExecuteAsync(
            _shopId,
            _saleId,
            _ =>
            {
                _mutationRuns++;
                return Task.FromResult(mutation());
            },
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WhenThePeriodIsClosed_RefusesBeforeTheSaleLockAndMutation()
    {
        LockReturns(RowLockOutcome.Acquired);
        _postingPeriods.AuthorizeForWriteAsync(_shopId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PostingPeriodLease>>(Errors.PostingPeriod.Closed(new DateOnly(2026, 6, 1))));

        var result = await RunAsync(() => Result.Success);

        Assert.True(result.IsError);
        Assert.Equal("PostingPeriod.Closed", result.FirstError.Code);
        Assert.Equal(0, _mutationRuns);
        await _saleRepository.DidNotReceive().LockForReturnMutationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutationSucceeds_LocksInsideTransactionThenCommits()
    {
        LockReturns(RowLockOutcome.Acquired);

        var result = await RunAsync(() => Result.Success);

        Assert.False(result.IsError);
        Assert.Equal(1, _mutationRuns);
        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _saleRepository.LockForReturnMutationAsync(_shopId, _saleId, Arg.Any<CancellationToken>());
            _unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>());
        });
        await _unitOfWork.DidNotReceive().RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_LockIsScopedToCallersShop()
    {
        LockReturns(RowLockOutcome.Acquired);

        await RunAsync(() => Result.Success);

        await _saleRepository.Received(1).LockForReturnMutationAsync(_shopId, _saleId, Arg.Any<CancellationToken>());
        await _saleRepository.DidNotReceive().LockForReturnMutationAsync(
            Arg.Is<Guid>(id => id != _shopId), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenSaleNotInShop_ReturnsNotFoundWithoutRunningMutation()
    {
        LockReturns(RowLockOutcome.NotFound);

        var result = await RunAsync(() => Result.Success);

        Assert.Equal(Errors.Sale.NotFound(_saleId).Code, result.FirstError.Code);
        Assert.Equal(0, _mutationRuns);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockTimesOut_ReturnsRetryableConflictWithoutRunningMutation()
    {
        LockReturns(RowLockOutcome.TimedOut);

        var result = await RunAsync(() => Result.Success);

        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(Errors.Sale.ReturnSaleBusy.Code, result.FirstError.Code);
        Assert.Equal(0, _mutationRuns);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutationRefuses_RollsBackAndDiscardsChanges()
    {
        LockReturns(RowLockOutcome.Acquired);

        var result = await RunAsync(() => Errors.Sale.ReturnAlreadyVoided);

        Assert.Equal(Errors.Sale.ReturnAlreadyVoided.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        _unitOfWork.Received(1).ClearChanges();
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenStockRowChangedUnderneath_ReturnsRetryableConflict()
    {
        LockReturns(RowLockOutcome.Acquired);

        var result = await RunAsync(() => throw new DbUpdateConcurrencyException());

        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(Errors.Sale.ReturnConcurrentChange.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMutationThrows_RollsBackAndRethrows()
    {
        LockReturns(RowLockOutcome.Acquired);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(() => throw new InvalidOperationException("boom")));

        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommitFails_RollsBack()
    {
        LockReturns(RowLockOutcome.Acquired);
        _unitOfWork.CommitTransactionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("connection lost"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(() => Result.Success));

        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenLockOutcomeUnknown_Throws()
    {
        LockReturns(default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(() => Result.Success));

        Assert.Equal(0, _mutationRuns);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task ExecuteAsync_WhenALaterLockTimesOut_ReturnsBusy()
    {
        LockReturns(RowLockOutcome.Acquired);

        var result = await RunAsync(() => throw new InvalidOperationException("transient", new FakeDbException("55P03")));

        Assert.Equal(Errors.Sale.ReturnSaleBusy.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("40P01")]
    [InlineData("40001")]
    public async Task ExecuteAsync_WhenDeadlockOrSerializationFailure_ReturnsRetryableConflict(string sqlState)
    {
        LockReturns(RowLockOutcome.Acquired);

        var result = await RunAsync(() => throw new DbUpdateException("save failed", new FakeDbException(sqlState)));

        Assert.Equal(Errors.Sale.ReturnConcurrentChange.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenRollbackFailsAfterAnError_KeepsTheOriginalError()
    {
        LockReturns(RowLockOutcome.Acquired);
        _unitOfWork.RollbackTransactionAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("connection broken"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(() => throw new InvalidOperationException("original")));

        Assert.Equal("original", thrown.Message);
    }

    private sealed class FakeDbException(string sqlState) : DbException("database error")
    {
        public override string SqlState { get; } = sqlState;
    }
}
