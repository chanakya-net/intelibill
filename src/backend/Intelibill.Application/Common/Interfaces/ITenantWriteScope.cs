namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The one sanctioned way to touch a tenant table for a shop that is not the
/// request's active one.
/// </summary>
/// <remarks>
/// Enter a target that comes from an authorized live membership, a valid invitation, a shop just
/// created by the transaction, or a trusted realtime delivery target. Entry only narrows database
/// visibility. The caller still checks its own authority for the operation.
/// </remarks>
public interface ITenantWriteScope
{
    /// <summary>
    /// Narrows the database session to <paramref name="shopId"/> for the
    /// remainder of the current transaction. The change is transaction-local, so
    /// it reverts at commit or rollback and cannot reach the pooled connection.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No explicit transaction is open. Without one the scope would be set on
    /// the session and outlive the operation that asked for it.
    /// </exception>
    Task EnterAsync(Guid shopId, CancellationToken cancellationToken = default);
}
