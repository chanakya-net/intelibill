using Intelibill.Shared.Web.Persistence;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal sealed class PostgresAdminSignInBudget(LicensingDbContext context) : IAdminSignInBudget
{
    public async Task<DateTimeOffset?> TakeAsync(string accountSubject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.SetLockTimeoutAsync(LockWait.Brief, cancellationToken);
        var reset = await TakeBucketAsync("global", 60, now, now.AddMinutes(5), cancellationToken);
        if (reset is null)
        {
            await context.Database.ExecuteSqlAsync($"""
                DELETE FROM public.admin_sign_in_buckets WHERE window_ends_at <= {now} AND subject <> 'global'
                """, cancellationToken);
            reset = await TakeBucketAsync(accountSubject, 5, now, now.AddMinutes(15), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return reset;
    }

    private async Task<DateTimeOffset?> TakeBucketAsync(string subject, int limit, DateTimeOffset now, DateTimeOffset next,
        CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQuery<DateTimeOffset?>($"""
            INSERT INTO public.admin_sign_in_buckets (subject, window_ends_at, attempts)
            VALUES ({subject}, {next}, 1)
            ON CONFLICT (subject) DO UPDATE SET
                window_ends_at = CASE WHEN admin_sign_in_buckets.window_ends_at <= {now}
                    THEN {next} ELSE admin_sign_in_buckets.window_ends_at END,
                attempts = CASE WHEN admin_sign_in_buckets.window_ends_at <= {now}
                    THEN 1 ELSE LEAST(admin_sign_in_buckets.attempts + 1, {limit + 1}) END
            RETURNING CASE WHEN attempts > {limit} THEN window_ends_at END AS "Value"
            """).ToListAsync(cancellationToken);
        return rows.Single();
    }
}
