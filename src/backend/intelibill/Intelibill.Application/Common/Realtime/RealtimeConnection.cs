using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Common.Realtime;

/// <summary>
/// One live notification transport, described by the authority it was admitted under rather than by
/// the token it presented. Nothing here is a credential: a connection id, the process holding it,
/// and the identifiers needed to re-ask whether that authority still stands.
/// </summary>
/// <param name="InstanceId">
/// The process that owns the transport. Only that process can close it, so a sweep running
/// elsewhere skips the row instead of pretending it can act on it.
/// </param>
/// <param name="ExpiresAt">
/// The presented token's expiry. A lease past it is never a recipient, whether or not the transport
/// has already been closed.
/// </param>
public sealed record RealtimeConnection(
    string ConnectionId,
    string InstanceId,
    string HubName,
    Guid UserId,
    Guid SessionId,
    Guid ShopId,
    string? ShopRole,
    long SecurityVersion,
    long ScopeVersion,
    DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Rebuilds the question the authority answers, so a recipient is re-checked against exactly
    /// what an HTTP request from the same device would be checked against.
    /// </summary>
    public SessionClaims ToSessionClaims() =>
        new(UserId, SessionId, SecurityVersion, ScopeVersion, ShopId, ShopRole);
}
