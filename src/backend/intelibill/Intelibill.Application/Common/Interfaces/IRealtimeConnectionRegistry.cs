using Intelibill.Application.Common.Realtime;

namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Where live notification transports are recorded so that any replica can decide who a payload may
/// reach, and so that the process holding a socket can find it again and close it.
/// </summary>
/// <remarks>
/// Shared storage rather than a process-local map because a revocation committed on one replica must
/// stop delivery on another. Note that without a SignalR backplane the <em>send</em> still only
/// reaches sockets the dispatching process holds; what this table makes correct cluster-wide is the
/// refusal, not the reach.
/// </remarks>
public interface IRealtimeConnectionRegistry
{
    /// <summary>
    /// Records an admitted transport, replacing any earlier row for the same connection id.
    /// </summary>
    Task RegisterAsync(RealtimeConnection connection, CancellationToken cancellationToken = default);

    Task RemoveAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The candidate recipients for a shop event on one hub. Candidates only: each is still
    /// re-authorized before anything is sent, because a lease records the authority a connection was
    /// admitted under, not the authority it has now.
    /// </summary>
    Task<IReadOnlyList<RealtimeConnection>> ListShopConnectionsAsync(
        string hubName,
        Guid shopId,
        CancellationToken cancellationToken = default);

    /// <summary>The transports this process holds, and so the only ones it can close.</summary>
    Task<IReadOnlyList<RealtimeConnection>> ListInstanceConnectionsAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes leases whose presented token has expired. Safe to rerun, and it touches no session or
    /// business record.
    /// </summary>
    Task<int> RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
