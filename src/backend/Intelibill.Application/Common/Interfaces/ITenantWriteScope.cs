namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The one sanctioned way to touch a tenant table for a shop that is not the
/// request's active one.
/// </summary>
/// <remarks>
/// <para>
/// Three operations need this and no others: creating a shop, which writes the
/// new shop's system supplier before the rescope that makes it current; reading
/// shop details; and writing shop bank details, both of which are authorized by
/// a live membership re-read and explicitly need not be the caller's active
/// shop.
/// </para>
/// <para>
/// The shop id is always server-derived — a shop the handler just created, or
/// one whose live membership it has just re-read. It is never taken from a
/// header, a claim or a request body, and there is no request-controlled bypass
/// flag: a caller cannot ask for this.
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
