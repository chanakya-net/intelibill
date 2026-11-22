using ErrorOr;
using Intelibill.Domain.Interfaces;

namespace Intelibill.Application.Common.Posting;

/// <summary>
/// Runs a posting that has no transaction of its own inside one, so the share lock the
/// <see cref="IPostingPeriodPolicy"/> fence takes lasts until the posting commits.
/// </summary>
/// <remarks>
/// A keyed command does not use this: its <c>IFinancialOperationExecutor</c> already owns the
/// transaction and the action must neither begin nor commit. An error result or any exception rolls
/// back and clears the change tracker, so nothing staged survives.
/// </remarks>
public static class PostingTransaction
{
    public static Task<ErrorOr<T>> RunAsync<T>(
        IUnitOfWork unitOfWork,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken) =>
        RunAsync(unitOfWork, action, unitOfWork.CommitTransactionAsync, cancellationToken);

    /// <summary>Saves staged writes and commits before the unit of work publishes domain events.</summary>
    public static Task<ErrorOr<T>> RunAndSaveAsync<T>(
        IUnitOfWork unitOfWork,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken) =>
        RunAsync(unitOfWork, action, unitOfWork.SaveChangesAndCommitAsync, cancellationToken);

    private static async Task<ErrorOr<T>> RunAsync<T>(
        IUnitOfWork unitOfWork,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        Func<CancellationToken, Task> commit,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var result = await action(cancellationToken);
            if (result.IsError)
                return result;

            await commit(cancellationToken);
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
}
