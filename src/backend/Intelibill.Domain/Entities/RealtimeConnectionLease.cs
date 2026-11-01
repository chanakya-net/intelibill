using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One live notification transport, recorded so that any replica can decide who a shop event may
/// reach and so that the process holding the socket can find it again and close it.
/// </summary>
/// <remarks>
/// <para>
/// The row holds no credential — no access token, no refresh secret. It holds a connection id, the
/// process that owns it, and the identifiers a live authority check needs: the session, the account
/// and the shop membership the transport was admitted under.
/// </para>
/// <para>
/// The authority recorded here is the authority at admission, never the authority now. Every
/// outbound payload re-asks, because a membership can be withdrawn a moment after a socket opens.
/// <see cref="ExpiresAt"/> is the one fact that needs no lookup: a lease past it is never a
/// recipient, and the sweep deletes it.
/// </para>
/// </remarks>
public sealed class RealtimeConnectionLease : BaseEntity
{
    public string ConnectionId { get; private set; } = string.Empty;

    /// <summary>The process holding the transport. Only it can close the socket.</summary>
    public string InstanceId { get; private set; } = string.Empty;

    public string HubName { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ShopId { get; private set; }

    /// <summary>The role the presented token claimed. Re-checked live, never trusted from here.</summary>
    public string? ShopRole { get; private set; }

    public long SecurityVersion { get; private set; }
    public long ScopeVersion { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private RealtimeConnectionLease() { }

    public static RealtimeConnectionLease Open(
        string connectionId,
        string instanceId,
        string hubName,
        Guid userId,
        Guid sessionId,
        Guid shopId,
        string? shopRole,
        long securityVersion,
        long scopeVersion,
        DateTimeOffset connectedAt,
        DateTimeOffset expiresAt) =>
        new()
        {
            ConnectionId = connectionId,
            InstanceId = instanceId,
            HubName = hubName,
            UserId = userId,
            SessionId = sessionId,
            ShopId = shopId,
            ShopRole = shopRole,
            SecurityVersion = securityVersion,
            ScopeVersion = scopeVersion,
            ConnectedAt = connectedAt,
            ExpiresAt = expiresAt,
        };

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// Re-points an existing row at a reconnect that reused the connection id. The authority is
    /// restated rather than merged: what was admitted last is what the dispatcher must check.
    /// </summary>
    public void Rebind(
        string instanceId,
        string hubName,
        Guid userId,
        Guid sessionId,
        Guid shopId,
        string? shopRole,
        long securityVersion,
        long scopeVersion,
        DateTimeOffset connectedAt,
        DateTimeOffset expiresAt)
    {
        InstanceId = instanceId;
        HubName = hubName;
        UserId = userId;
        SessionId = sessionId;
        ShopId = shopId;
        ShopRole = shopRole;
        SecurityVersion = securityVersion;
        ScopeVersion = scopeVersion;
        ConnectedAt = connectedAt;
        ExpiresAt = expiresAt;
    }
}
