namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The one sanctioned way to touch a tenant table for a shop that is not the
/// request's active one.
/// </summary>
/// <remarks>
/// <para>
/// Four operations need this and no others: creating a shop, which writes the
/// new shop's system supplier before the rescope that makes it current; reading
/// shop details; updating a shop, whose response carries that shop's bank
/// details; and writing shop bank details. The last three are authorized by a
/// live membership re-read and explicitly need not be the caller's active shop.
/// </para>
/// <para>
/// The shop id is either one the handler has just created, or one the caller
/// named in the route — and in that second case it is only ever entered after
/// the handler has re-read a live membership of that shop for the authenticated
/// account, Owner where the operation requires Owner. That re-read is the gate,
/// not the origin of the value: a new call site that skips it would admit a
/// shop the caller has no claim to. There is no request-controlled bypass flag,
/// so a caller cannot ask for this directly.
/// </para>
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
