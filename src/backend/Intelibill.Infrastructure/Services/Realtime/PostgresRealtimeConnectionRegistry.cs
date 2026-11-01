using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Realtime;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Realtime;

/// <summary>
/// The lease registry on PostgreSQL. Shared storage rather than process memory, because a revocation
/// committed on one replica has to stop delivery on every other.
/// </summary>
internal sealed class PostgresRealtimeConnectionRegistry(ApplicationDbContext db, TimeProvider clock)
    : IRealtimeConnectionRegistry
{
    public async Task RegisterAsync(RealtimeConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var now = clock.GetUtcNow();
        var existing = await db.RealtimeConnectionLeases
            .FirstOrDefaultAsync(lease => lease.ConnectionId == connection.ConnectionId, cancellationToken);

        if (existing is null)
        {
            db.RealtimeConnectionLeases.Add(RealtimeConnectionLease.Open(
                connection.ConnectionId,
                connection.InstanceId,
                connection.HubName,
                connection.UserId,
                connection.SessionId,
                connection.ShopId,
                connection.ShopRole,
                connection.SecurityVersion,
                connection.ScopeVersion,
                now,
                connection.ExpiresAt));
        }
        else
        {
            existing.Rebind(
                connection.InstanceId,
                connection.HubName,
                connection.UserId,
                connection.SessionId,
                connection.ShopId,
                connection.ShopRole,
                connection.SecurityVersion,
                connection.ScopeVersion,
                now,
                connection.ExpiresAt);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        await db.RealtimeConnectionLeases
            .Where(lease => lease.ConnectionId == connectionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RealtimeConnection>> ListShopConnectionsAsync(
        string hubName,
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        return await db.RealtimeConnectionLeases
            .AsNoTracking()
            .Where(lease => lease.HubName == hubName && lease.ShopId == shopId && lease.ExpiresAt > now)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RealtimeConnection>> ListInstanceConnectionsAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await db.RealtimeConnectionLeases
            .AsNoTracking()
            .Where(lease => lease.InstanceId == instanceId)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RemoveExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        return await db.RealtimeConnectionLeases
            .Where(lease => lease.ExpiresAt <= asOf)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static readonly System.Linq.Expressions.Expression<Func<RealtimeConnectionLease, RealtimeConnection>> Projection =
        lease => new RealtimeConnection(
            lease.ConnectionId,
            lease.InstanceId,
            lease.HubName,
            lease.UserId,
            lease.SessionId,
            lease.ShopId,
            lease.ShopRole,
            lease.SecurityVersion,
            lease.ScopeVersion,
            lease.ExpiresAt);
}
