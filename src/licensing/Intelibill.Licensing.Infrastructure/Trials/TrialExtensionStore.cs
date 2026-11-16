using System.Data;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Grants;
using Intelibill.Licensing.Application.Trials;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Trials;

internal sealed partial class TrialExtensionStore(LicensingDbContext db, ILogger<TrialExtensionStore> logger)
    : ITrialExtensionStore
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
            return TrialExtensionErrors.Busy;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return TrialExtensionErrors.VersionConflict;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or InvalidOperationException { InnerException: NpgsqlException })
        {
            ct.ThrowIfCancellationRequested();
            LogUnavailable(logger, SqlState(error) ?? error.GetType().Name);
            return TrialExtensionErrors.Unavailable;
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
        SELECT set_config('app.trial_extension_admin_id', {adminId.ToString("D")}, true),
            set_config('app.billing_account_id', {account.ToString("D")}, true)
        """, ct);

    public Task LockAccountAsync(Guid account, CancellationToken ct) => BillingAccountLock.TakeAsync(db, account, ct);

    public async Task<long> ReadVersionAsync(Guid account, Guid shop, CancellationToken ct) =>
        await db.Database.SqlQuery<long?>($"""
            SELECT max(revision) AS "Value" FROM public.trial_extensions WHERE account_id = {account} AND shop_id = {shop}
            """).SingleAsync(ct) ?? 0;

    public Task<TrialExtensionShop?> ReadShopAsync(Guid account, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<TrialExtensionShop>($"""
            SELECT shop_name, request_id AS affiliation_request_id FROM public.shop_affiliations
            WHERE account_id = {account} AND shop_id = {shop}
            """).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TrialExtensionOverlap>> ReadOverlapsAsync(Guid account, Guid shop, TrialInterval interval,
        CancellationToken ct) => await db.Database.SqlQuery<TrialExtensionOverlap>($"""
            SELECT h.shop_id, h.starts_at, coalesce(e.ends_at, h.ends_at) AS ends_at
            FROM public.trial_history h
            LEFT JOIN LATERAL (SELECT ends_at FROM public.trial_extensions
                WHERE shop_id = h.shop_id AND account_id = h.account_id ORDER BY revision DESC LIMIT 1) e ON true
            WHERE h.account_id = {account} AND h.shop_id <> {shop}
                AND h.starts_at < {interval.EndsAt} AND coalesce(e.ends_at, h.ends_at) > {interval.StartsAt}
            ORDER BY h.shop_id
            """).ToListAsync(ct);

    public async Task<TrialExtensionReceipt?> ReadAsync(Guid account, Guid key, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ReceiptRow>($"""
            SELECT e.extension_id, e.account_id, e.shop_id, e.term_id, h.starts_at, h.ends_at AS original_ends_at,
                e.previous_ends_at, e.ends_at, e.revision, e.recorded_at, h.account_trial_cap, r.account_trial_cap_reached,
                r.shop_name, r.operation_key, r.admin_id, r.session_id, r.reason, r.review_reference, r.source_hash, r.payload_digest
            FROM public.trial_extension_receipts r JOIN public.trial_extensions e USING (extension_id, account_id, shop_id)
            JOIN public.trial_history h USING (shop_id, account_id, term_id)
            WHERE r.account_id = {account} AND r.operation_key = {key}
            """).SingleOrDefaultAsync(ct);
        return row?.ToReceipt();
    }

    public async Task SaveAsync(TrialExtensionReceipt receipt, DateTimeOffset previousEnd, DateTimeOffset newEnd, CancellationToken ct)
    {
        var item = receipt.Result;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.trial_extensions (extension_id, account_id, shop_id, term_id, revision,
                previous_ends_at, ends_at, recorded_at, policy_version)
            VALUES ({item.ExtensionId}, {item.AccountId}, {item.ShopId}, {item.TermId}, {item.ExtendedVersion},
                {previousEnd}, {newEnd}, {item.RecordedAt}, {item.PolicyVersion})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.trial_extension_receipts (extension_id, account_id, shop_id, operation_key, admin_id,
                session_id, shop_name, reason, review_reference, payload_digest, source_hash, expected_version, account_trial_cap_reached)
            VALUES ({item.ExtensionId}, {item.AccountId}, {item.ShopId}, {item.OperationKey}, {receipt.AdminId}, {receipt.SessionId},
                {item.ShopName}, {item.Reason}, {item.ReviewReference}, {receipt.PayloadDigest}, {item.SourceHash},
                {item.CurrentVersion}, {item.AccountTrialCapReached})
            """, ct);
    }

    private sealed record ReceiptRow(Guid ExtensionId, Guid AccountId, Guid ShopId, Guid TermId, DateTimeOffset StartsAt,
        DateTimeOffset OriginalEndsAt, DateTimeOffset PreviousEndsAt, DateTimeOffset EndsAt, long Revision,
        DateTimeOffset RecordedAt, int AccountTrialCap, bool AccountTrialCapReached, string ShopName, Guid OperationKey,
        Guid AdminId, Guid SessionId, string Reason, string ReviewReference, string SourceHash, string PayloadDigest)
    {
        internal TrialExtensionReceipt ToReceipt() => new(
            new TrialExtensionResultV1(AccountId, ShopId, ShopName, TermId, ComplimentaryGrantFacts.Format(StartsAt),
                ComplimentaryGrantFacts.Format(OriginalEndsAt), ComplimentaryGrantFacts.Format(PreviousEndsAt),
                ComplimentaryGrantFacts.Format(EndsAt), Reason, ReviewReference, Revision - 1, Revision, SourceHash,
                RecordedAt.ToUniversalTime(), AccountTrialCap, AccountTrialCapReached, OperationKey, ExtensionId),
            AdminId, SessionId, PayloadDigest);
    }

    private static string? SqlState(Exception error) =>
        (error as PostgresException ?? error.InnerException as PostgresException)?.SqlState;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trial extension storage unavailable ({Cause}).")]
    private static partial void LogUnavailable(ILogger logger, string cause);
}
