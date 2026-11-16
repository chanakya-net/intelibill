using System.Data;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Status;

/// <summary>
/// Transaction-local status scope. Settings are set with <c>is_local</c>, so commit or rollback clears them
/// before the pooled connection is reused. Storage failures become an explicit unavailable outcome.
/// </summary>
internal sealed partial class SubscriptionStatusStore(LicensingDbContext db, ILogger<SubscriptionStatusStore> logger)
    : ISubscriptionStatusStore
{
    public Task UseShopAsync(Guid shop, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT set_config('app.status_shop_id', {shop.ToString("D")}, true)", ct);

    public Task UseAccountAsync(Guid account, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT set_config('app.billing_account_id', {account.ToString("D")}, true)", ct);

    public async Task<bool> HoldsCommercialRoleAsync(Guid account, Guid actor, CancellationToken ct)
    {
        var roles = await db.Database.SqlQuery<int>($"""
            SELECT role AS "Value" FROM public.billing_account_members
            WHERE account_id = {account} AND merchant_user_id = {actor}
              AND status = {(int)BillingAccountMemberStatus.Active}
            FOR SHARE
            """).ToListAsync(ct);
        return roles is [(int)BillingAccountRole.Owner or (int)BillingAccountRole.Manager];
    }

    public async Task<ErrorOr<T>> InProviderTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation,
        CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            var result = await operation(ct);
            if (!result.IsError) await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException
            { InnerException: NpgsqlException })
        {
            ct.ThrowIfCancellationRequested();
            LogUnavailable(logger, exception.GetType().Name);
            return SubscriptionStatusErrors.Unavailable;
        }
    }

    public async Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller,
        CancellationToken ct)
    {
        var actors = await db.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR SHARE
            """).AsNoTracking().ToListAsync(ct);
        var session = await AdminAuthorityLocks.LockSessionAsync(db, caller, ct);
        return (actors.SingleOrDefault(), session);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider status read failed (StorageUnavailable); cause={Cause}.")]
    private static partial void LogUnavailable(ILogger logger, string cause);
}
