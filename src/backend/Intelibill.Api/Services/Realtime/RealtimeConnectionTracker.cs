using System.Collections.Concurrent;
using Intelibill.Application.Common.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Services.Realtime;

/// <summary>
/// The handles this process needs to close its own sockets.
/// </summary>
/// <remarks>
/// <para>
/// Aborting a transport requires the <see cref="HubCallerContext"/> that owns it, and that object
/// exists only inside the process holding the connection — <c>IHubContext</c> has no equivalent. So
/// this map is process-local by necessity, not by design.
/// </para>
/// <para>
/// It is never consulted to decide whether a payload may be delivered. That decision reads the
/// durable lease registry and re-checks live authority, which is why nothing here claims to be
/// correct across replicas.
/// </para>
/// </remarks>
public sealed class RealtimeConnectionTracker
{
    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    public void Track(RealtimeConnection connection, HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connections[connection.ConnectionId] = new Entry(connection, context);
    }

    public void Forget(string connectionId) => _connections.TryRemove(connectionId, out _);

    public bool TryGet(string connectionId, out RealtimeConnection? connection)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            connection = entry.Connection;
            return true;
        }

        connection = null;
        return false;
    }

    /// <summary>
    /// Closes the transport if this process still holds it, and forgets it either way. Returns
    /// whether a socket was actually closed, so a caller can tell "closed it" from "it was already
    /// gone" without guessing.
    /// </summary>
    public bool Abort(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var entry))
            return false;

        entry.Context.Abort();
        return true;
    }

    private sealed record Entry(RealtimeConnection Connection, HubCallerContext Context);
}
