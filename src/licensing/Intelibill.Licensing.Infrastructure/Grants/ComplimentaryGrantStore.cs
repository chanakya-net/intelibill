using Intelibill.Licensing.Application.Entitlements;
using System.Data;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Grants;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Grants;

/// <summary>
/// SQL-owned complimentary grant persistence under forced row-level security. Context settings are
/// transaction-local, so commit or rollback clears them before a pooled connection is reused. One transaction
/// writes the revision, grant, coverage term, receipt and named shops; deferred checks require all of them.
/// </summary>
internal sealed partial class ComplimentaryGrantStore(LicensingDbContext db, ILogger<ComplimentaryGrantStore> logger, IEntitlementPublisher publisher)
    : IComplimentaryGrantStore
{
    public async Task<ErrorOr<T>> InTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            var result = await operation(ct);
            if (!result.IsError) await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected)
        {
            return ComplimentaryGrantErrors.Busy;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            // A competing confirmation committed first; the caller previews again (or replays its own key).
            return ComplimentaryGrantErrors.VersionConflict;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or InvalidOperationException { InnerException: NpgsqlException })
        {
            ct.ThrowIfCancellationRequested();
            LogUnavailable(logger, SqlState(error) ?? error.GetType().Name);
            return ComplimentaryGrantErrors.Unavailable;
        }
    }

    public async Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller, CancellationToken ct)
    {
        var actors = await db.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR SHARE
            """).AsNoTracking().ToListAsync(ct);
        var session = await AdminAuthorityLocks.LockSessionAsync(db, caller, ct);
        return (actors.SingleOrDefault(), session);
    }

    public Task EnterAsync(Guid adminId, Guid account, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        SELECT set_config('app.grant_admin_id', {adminId.ToString("D")}, true),
            set_config('app.billing_account_id', {account.ToString("D")}, true)
        """, ct);

    public Task LockAccountAsync(Guid account, CancellationToken ct) => BillingAccountLock.TakeAsync(db, account, ct);

    public async Task<long> ReadRevisionAsync(Guid account, CancellationToken ct) =>
        await db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM public.complimentary_grant_scopes WHERE account_id = {account}
            """).SingleOrDefaultAsync(ct) ?? 0;

    public async Task<IReadOnlyList<GrantShopSource>> ReadShopsAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        await db.Database.SqlQuery<GrantShopSource>($"""
            SELECT shop_id, shop_name, request_id AS affiliation_request_id FROM public.shop_affiliations
            WHERE account_id = {account} AND shop_id = ANY({shops}) ORDER BY shop_id
            """).ToListAsync(ct);

    public async Task<IReadOnlyList<GrantWindow>> ReadWindowsAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        await db.Database.SqlQuery<GrantWindow>($"""
            SELECT shop_id, grant_id, starts_at, ends_at FROM public.complimentary_grant_shops
            WHERE account_id = {account} AND shop_id = ANY({shops}) ORDER BY shop_id, grant_id
            """).ToListAsync(ct);

    public async Task<StoredComplimentaryGrant?> ReadAsync(Guid account, Guid operationKey, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT g.grant_id, g.term_id, g.account_id, r.operation_key, r.admin_id, r.session_id, g.starts_at, g.ends_at,
                g.member_allowance, r.reason, r.expected_version, r.source_hash, r.payload_digest, g.recorded_at
            FROM public.complimentary_grant_receipts r
            JOIN public.complimentary_grants g ON g.grant_id = r.grant_id AND g.account_id = r.account_id
            WHERE r.account_id = {account} AND r.operation_key = {operationKey}
            """).ToListAsync(ct);
        if (rows is not [var row]) return null;
        var shops = await db.Database.SqlQuery<GrantShopSource>($"""
            SELECT l.shop_id, a.shop_name, l.affiliation_request_id
            FROM public.complimentary_grant_shops l JOIN public.shop_affiliations a ON a.request_id = l.affiliation_request_id
            WHERE l.grant_id = {row.GrantId} ORDER BY l.shop_id
            """).ToListAsync(ct);
        var facts = new ComplimentaryGrantFacts([.. shops.Select(shop => shop.ShopId)], row.StartsAt.ToUniversalTime(),
            row.EndsAt.ToUniversalTime(), row.MemberAllowance, row.Reason);
        return new StoredComplimentaryGrant(row.GrantId, row.TermId, row.AccountId, row.OperationKey, row.AdminId, row.SessionId,
            facts, row.ExpectedVersion, row.SourceHash, row.PayloadDigest, row.RecordedAt.ToUniversalTime(), shops);
    }

    public async Task SaveAsync(StoredComplimentaryGrant grant, CancellationToken ct)
    {
        var facts = grant.Facts;
        var version = grant.ExpectedVersion + 1;
        if (grant.ExpectedVersion == 0)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO public.complimentary_grant_scopes (account_id, revision) VALUES ({grant.AccountId}, 1)", ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public.complimentary_grant_scopes SET revision = {version}
                WHERE account_id = {grant.AccountId} AND revision = {grant.ExpectedVersion}
                """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.complimentary_grants (grant_id, account_id, term_id, basis, starts_at, ends_at, coverage_mode,
                shop_capacity, member_allowance, policy_version, granted_version, recorded_at)
            VALUES ({grant.GrantId}, {grant.AccountId}, {grant.TermId}, 'Complimentary', {facts.StartsAt}, {facts.EndsAt},
                {facts.CoverageMode}, {facts.ShopIds.Length}, {facts.MemberAllowance},
                {Domain.Policies.ComplimentaryAccessPolicy.Version}, {version}, {grant.RecordedAt})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ({grant.TermId}, {grant.AccountId}, {facts.CoverageMode}, {facts.ShopIds.Length}, {facts.StartsAt}, {facts.EndsAt})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.complimentary_grant_receipts (grant_id, account_id, operation_key, admin_id, session_id, reason,
                payload_digest, source_hash, expected_version, recorded_at)
            VALUES ({grant.GrantId}, {grant.AccountId}, {grant.OperationKey}, {grant.AdminId}, {grant.SessionId}, {facts.Reason},
                {grant.PayloadDigest}, {grant.SourceHash}, {grant.ExpectedVersion}, {grant.RecordedAt})
            """, ct);
        foreach (var shop in grant.Shops)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.complimentary_grant_shops (grant_id, account_id, term_id, shop_id, affiliation_request_id,
                    starts_at, ends_at)
                VALUES ({grant.GrantId}, {grant.AccountId}, {grant.TermId}, {shop.ShopId}, {shop.AffiliationRequestId},
                    {facts.StartsAt}, {facts.EndsAt})
                """, ct);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);
        foreach (var shop in grant.Shops.OrderBy(shop => shop.ShopId))
            await publisher.PublishAsync(grant.AccountId, shop.ShopId, ct);
    }

    private sealed record ReceiptRow(Guid GrantId, Guid TermId, Guid AccountId, Guid OperationKey, Guid AdminId, Guid SessionId,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt, int MemberAllowance, string Reason, long ExpectedVersion,
        string SourceHash, string PayloadDigest, DateTimeOffset RecordedAt);

    private static string? SqlState(Exception error) =>
        (error as PostgresException ?? error.InnerException as PostgresException)?.SqlState;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Complimentary grant storage unavailable ({Cause}).")]
    private static partial void LogUnavailable(ILogger logger, string cause);
}
