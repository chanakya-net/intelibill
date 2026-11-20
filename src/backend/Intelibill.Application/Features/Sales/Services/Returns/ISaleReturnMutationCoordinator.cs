using ErrorOr;
using Intelibill.Application.Common.Licensing;

namespace Intelibill.Application.Features.Sales.Services.Returns;

/// <summary>
/// Runs a return or void in a single transaction. Each decision serializes on the original sale
/// and validates against what the previous transaction committed.
/// </summary>
/// <remarks>
/// Lock order: optional operation receipt, account/shop licence, posting period, expiry policy,
/// optional till/session, customer, original sale, credit notes, then stock rows. Credit-note
/// redemption takes credit notes and batches without waiting on the parent sale.
/// </remarks>
public interface ISaleReturnMutationCoordinator
{
    /// <summary>One captured server instant for the current transaction attempt and all source facts.</summary>
    DateTimeOffset? PostingInstant { get; }
    /// <summary>
    /// Locks the sale and runs <paramref name="mutation"/>, which reads everything it decides on and
    /// saves its changes. Commits only when the mutation succeeds; otherwise nothing is kept.
    /// </summary>
    Task<ErrorOr<TResult>> ExecuteAsync<TResult>(
        Guid shopId,
        Guid saleId,
        Func<CancellationToken, Task<ErrorOr<TResult>>> mutation,
        CancellationToken cancellationToken);
    /// <summary>
    /// Runs the hook after the period fence and before customer/sale locks. When ownsTransaction is
    /// false the financial-operation executor must own rollback/commit; this path never nests one.
    /// Resolve the original sale after taking the licence fence, before taking any business locks.
    /// </summary>
    Task<ErrorOr<TResult>> ExecuteAsync<TResult>(Guid shopId,
        Func<CancellationToken, Task<ErrorOr<Guid>>> resolveSale,
        Func<CancellationToken, Task<ErrorOr<Success>>> beforeBusinessLocks,
        Func<ShopLicenceLease, CancellationToken, Task<ErrorOr<TResult>>> mutation,
        bool ownsTransaction, CancellationToken cancellationToken);
}
