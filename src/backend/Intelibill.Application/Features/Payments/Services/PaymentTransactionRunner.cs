using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Domain.Interfaces;

namespace Intelibill.Application.Features.Payments.Services;

/// <summary>
/// Runs a payment mutation in one transaction: through <see cref="IFinancialOperationExecutor"/>
/// when the caller sent an operation key (replay serialization comes before any business lock), or
/// in a plain transaction for a legacy unkeyed request. A lock not granted in its budget, or a
/// deadlock, ends with <c>Payment.Busy</c> and nothing kept.
/// </summary>
public interface IPaymentTransactionRunner
{
    Task<ErrorOr<T>> RunAsync<T>(
        FinancialOperationScope? scope,
        Guid actorUserId,
        Guid targetId,
        string? requestHash,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken);
}

internal sealed class PaymentTransactionRunner(
    IFinancialOperationExecutor operationExecutor,
    IUnitOfWork unitOfWork)
    : IPaymentTransactionRunner
{
    private const string LockNotAvailable = "55P03";
    private const string DeadlockDetected = "40P01";
    private const string UniqueViolation = "23505";

    public async Task<ErrorOr<T>> RunAsync<T>(
        FinancialOperationScope? scope,
        Guid actorUserId,
        Guid targetId,
        string? requestHash,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            if (scope is not null)
            {
                return await operationExecutor.ExecuteAsync(
                    scope,
                    actorUserId,
                    targetId,
                    requestHash ?? throw new ArgumentNullException(nameof(requestHash)),
                    action,
                    cancellationToken);
            }

            return await RunUnkeyedAsync(action, cancellationToken);
        }
        // A unique index (one reversal per receipt, one receipt per ledger entry) refusing a write
        // that slipped past the locks is the same answer: someone else changed it; refresh and retry.
        catch (Exception ex) when (SqlStateOf(ex) is LockNotAvailable or DeadlockDetected or UniqueViolation)
        {
            unitOfWork.ClearChanges();
            return Errors.Payment.Busy;
        }
    }

    private async Task<ErrorOr<T>> RunUnkeyedAsync<T>(
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var result = await action(cancellationToken);
            if (result.IsError)
                return result;

            await unitOfWork.CommitTransactionAsync(cancellationToken);
            committed = true;
            return result;
        }
        finally
        {
            if (!committed)
            {
                // Not the request's token: an abandoned request must still give its locks back.
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                unitOfWork.ClearChanges();
            }
        }
    }

    private static string? SqlStateOf(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } sqlState })
                return sqlState;
        }

        return null;
    }
}
