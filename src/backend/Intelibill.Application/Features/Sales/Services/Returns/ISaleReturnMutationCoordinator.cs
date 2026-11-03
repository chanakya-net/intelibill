using ErrorOr;

namespace Intelibill.Application.Features.Sales.Services.Returns;

/// <summary>
/// Runs one return, return void or credit-note decision on a sale as a single transaction that
/// holds the sale's row lock throughout. Every such decision takes the same lock first, so they
/// queue behind one another and each one validates against what the previous one committed.
/// </summary>
/// <remarks>
/// Lock order, which every caller follows and nothing reverses: the sale's customer, if any; the
/// sale; then the credit notes of
/// its returns; then inventory batches and aggregates, locked by their updates at save. Credit-note
/// redemption takes credit notes and then batches and never waits on the parent sale.
/// </remarks>
public interface ISaleReturnMutationCoordinator
{
    /// <summary>
    /// Locks the sale and runs <paramref name="mutation"/>, which reads everything it decides on and
    /// saves its changes. Commits only when the mutation succeeds; otherwise nothing is kept.
    /// </summary>
    Task<ErrorOr<TResult>> ExecuteAsync<TResult>(
        Guid shopId,
        Guid saleId,
        Func<CancellationToken, Task<ErrorOr<TResult>>> mutation,
        CancellationToken cancellationToken);
}
