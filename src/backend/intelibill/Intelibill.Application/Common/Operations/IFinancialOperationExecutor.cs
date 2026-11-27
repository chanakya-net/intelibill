using ErrorOr;

namespace Intelibill.Application.Common.Operations;

/// <summary>
/// Runs a financial mutation at most once per <see cref="FinancialOperationScope"/>.
/// </summary>
/// <remarks>
/// <para>
/// The executor owns the transaction. It reserves the receipt, invokes <c>action</c> — which must
/// save through the same <c>IUnitOfWork</c> and must not begin or commit a transaction of its own —
/// then stores the action's result on the receipt and commits both together. An action error or an
/// exception rolls back the reservation with the mutation, so a failed attempt leaves no trace.
/// </para>
/// <para>
/// A key already committed in the scope replays the stored result without invoking the action, or
/// answers <c>Operation.PayloadMismatch</c> when <c>requestHash</c> differs. Callers authorize the
/// actor and resolve the target before calling, so no replay reaches an unauthorized caller.
/// </para>
/// </remarks>
public interface IFinancialOperationExecutor
{
    Task<ErrorOr<T>> ExecuteAsync<T>(
        FinancialOperationScope scope,
        Guid actorUserId,
        Guid targetId,
        string requestHash,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken);
}
