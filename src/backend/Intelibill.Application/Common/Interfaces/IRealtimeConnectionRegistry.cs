using Intelibill.Application.Common.Realtime;

namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Where live notification transports are recorded so that any replica can decide who a payload may
/// reach. A process-local map cannot answer that: the connection being authorized is usually held by
/// a different process than the one handling the mutation.
/// </summary>
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
