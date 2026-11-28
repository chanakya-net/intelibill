using Intelibill.Shared.Web.Persistence;
using Npgsql;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal static class BillingAccountLock
{
    internal static async Task TakeAsync(LicensingDbContext db, Guid account, CancellationToken ct)
    {
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended({Key(account)}, 0))
            """, ct);
    }

    internal static async Task TakeSharedAsync(LicensingDbContext db, Guid account, CancellationToken ct)
    {
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock_shared(hashtextextended({Key(account)}, 0))
            """, ct);
    }

    // SMTP needs a durable Sending commit before DATA. Pin the same physical session across that commit.
    internal static async Task<IAsyncDisposable?> TryPinSharedAsync(LicensingDbContext db, Guid account, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var taken = await db.Database.SqlQuery<bool>($"""
                SELECT pg_try_advisory_lock_shared(hashtextextended({Key(account)}, 0)) AS "Value"
                """).SingleAsync(ct);
            if (taken)
                return new PinnedLock(db, account);
        }
        catch
        {
            // Cancellation can race the server reply after acquisition.
            NpgsqlConnection.ClearPool((NpgsqlConnection)db.Database.GetDbConnection());
            await db.Database.CloseConnectionAsync();
            throw;
        }
        await db.Database.CloseConnectionAsync();
        return null;
    }

    private static string Key(Guid account) => "b" + account.ToString("D");

    private sealed class PinnedLock(LicensingDbContext db, Guid account) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var released = await db.Database.SqlQuery<bool>($"""
                    SELECT pg_advisory_unlock_shared(hashtextextended({Key(account)}, 0)) AS "Value"
                    """).SingleAsync(cleanup.Token);
                if (!released)
                    throw new InvalidOperationException("Notification account lock was lost.");
            }
            catch
            {
                NpgsqlConnection.ClearPool((NpgsqlConnection)db.Database.GetDbConnection());
                throw;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
