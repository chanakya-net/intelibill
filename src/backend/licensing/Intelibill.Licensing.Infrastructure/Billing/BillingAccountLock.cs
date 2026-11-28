using Intelibill.Shared.Web.Persistence;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal static class BillingAccountLock
{
    internal static async Task TakeAsync(LicensingDbContext db, Guid account, CancellationToken ct)
    {
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended({'b' + account.ToString("D")}, 0))
            """, ct);
    }

    internal static async Task TakeSharedAsync(LicensingDbContext db, Guid account, CancellationToken ct)
    {
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock_shared(hashtextextended({'b' + account.ToString("D")}, 0))
            """, ct);
    }
}
