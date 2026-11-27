using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal static class RowLocks
{
    /// <summary>
    /// Runs a <c>SELECT ... FOR UPDATE</c> and returns the row tracked, with the values read under
    /// the lock. If the entity was already tracked, EF hands back that instance with whatever it held
    /// before the lock — exactly the stale read the lock exists to prevent — so it is reloaded.
    /// </summary>
    public static async Task<TEntity?> LockTrackedAsync<TEntity>(
        this ApplicationDbContext db,
        IQueryable<TEntity> lockingQuery,
        Guid id,
        CancellationToken ct)
        where TEntity : BaseEntity
    {
        var wasTracked = db.ChangeTracker.Entries<TEntity>().Any(x => x.Entity.Id == id);

        // Materialised without further composition so the locking clause is sent as written rather
        // than wrapped in a subquery.
        var row = (await lockingQuery.ToListAsync(ct)).SingleOrDefault();
        if (row is not null && wasTracked)
            await db.Entry(row).ReloadAsync(ct);

        return row;
    }

    /// <summary>
    /// Set-based revocation of every credential the query selects that is not already revoked. The
    /// first reason recorded on a credential is kept, matching <see cref="RefreshToken.Revoke"/>.
    /// </summary>
    public static Task<int> RevokeAsync(
        this IQueryable<RefreshToken> credentials,
        string reason,
        DateTimeOffset now,
        CancellationToken ct) =>
        credentials
            .Where(x => !x.IsRevoked)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.IsRevoked, true)
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevocationReason, reason),
                ct);
}
