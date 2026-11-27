using Intelibill.Shared.Web.Persistence;
using Intelibill.Licensing.Application.Entitlements;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Trials;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace Intelibill.Licensing.Infrastructure.Trials;

internal sealed partial class TrialStore(LicensingDbContext db, TimeProvider authClock, ILogger<TrialStore> logger, IEntitlementPublisher publisher) : ITrialStore
{
    public async Task<ErrorOr<ShopTrialDeliveryResultV1>> InDeliveryAsync(ShopTrialConsentV1 consent,
        TrialDeliveryProof proof, Func<TrialAffiliation?, CancellationToken, Task<ErrorOr<ShopTrialDeliveryResultV1>>> apply,
        CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SetLockTimeoutAsync(LockWait.Brief, ct);
            var affiliation = await UseShopAsync(consent.ShopId, ct);
            if (affiliation is { } bound)
            {
                await UseAccountAsync(bound.AccountId, ct);
                await BillingAccountLock.TakeAsync(db, bound.AccountId, ct);
                // Coverage committed while this delivery waited (a paid fulfilment) must decide eligibility.
                affiliation = await UseShopAsync(consent.ShopId, ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT pg_advisory_xact_lock(hashtextextended({'t' + consent.ShopId.ToString("D")}, 0))
                """, ct);
            if (authClock.GetUtcNow() >= proof.ExpiresAt || !await ConsumeAsync(consent, proof, ct))
                return BillingDelegationErrors.Replayed;

            var result = await apply(affiliation, ct);
            if (authClock.GetUtcNow() >= proof.ExpiresAt)
                return BillingDelegationErrors.Invalid;
            if (!result.IsError)
                await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            LogStorageFailure(logger, (error as PostgresException)?.SqlState ?? error.GetType().Name);
            return BillingDelegationErrors.StorageUnavailable;
        }
    }

    public async Task<TrialAffiliation?> UseShopAsync(Guid shop, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.status_shop_id', {shop.ToString("D")}, true)
            """, ct);
        return await db.Database.SqlQuery<TrialAffiliation>($"""
            SELECT a.account_id, coalesce(a.term_id, p.term_id) AS term_id, a.applied_at
            FROM public.current_shop_affiliations a
            LEFT JOIN LATERAL (SELECT p.term_id FROM public.payment_fulfilment_shops p
                WHERE p.shop_id = a.shop_id AND p.account_id = a.account_id LIMIT 1) p ON true
            WHERE a.shop_id = {shop}
            """).SingleOrDefaultAsync(ct);
    }

    public Task UseAccountAsync(Guid account, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        SELECT set_config('app.billing_account_id', {account.ToString("D")}, true)
        """, ct);

    private async Task<bool> ConsumeAsync(ShopTrialConsentV1 consent, TrialDeliveryProof proof, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.trial_delivery_id', {proof.ReplayId.ToString("D")}, true)
            """, ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_trial_delivery_uses (replay_id, event_id, shop_id, digest, expires_at)
            VALUES ({proof.ReplayId}, {consent.EventId}, {consent.ShopId}, {proof.Digest}, {proof.ExpiresAt})
            ON CONFLICT (replay_id) DO NOTHING
            """, ct);
        return inserted == 1 && authClock.GetUtcNow() < proof.ExpiresAt;
    }

    public Task<bool> HasHistoryAsync(Guid shop, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS (SELECT FROM public.trial_history WHERE shop_id = {shop}) AS "Value"
        """).SingleAsync(ct);

    public Task<TrialReceipt?> ReadReceiptAsync(ShopTrialConsentV1 consent, CancellationToken ct) =>
        db.Database.SqlQuery<TrialReceipt>($"""
            SELECT event_id, digest, status, reason FROM public.shop_trial_inbox
            WHERE shop_id = {consent.ShopId} AND actor_id = {consent.ActorId}
                AND operation_key = {consent.Acceptance.OperationKey}
            """).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TrialInterval>> ReadOverlapsAsync(Guid account, TrialInterval interval, CancellationToken ct) =>
        await db.Database.SqlQuery<TrialInterval>($"""
            SELECT h.starts_at, least(coalesce(e.ends_at, h.ends_at), x.paid_starts_at) AS ends_at FROM public.trial_history h
            LEFT JOIN LATERAL (SELECT ends_at FROM public.trial_extensions
                WHERE shop_id = h.shop_id AND account_id = h.account_id ORDER BY revision DESC LIMIT 1) e ON true
            LEFT JOIN public.payment_trial_conversions x ON x.account_id = h.account_id AND x.shop_id = h.shop_id
            WHERE h.account_id = {account} AND h.starts_at < {interval.EndsAt}
                AND least(coalesce(e.ends_at, h.ends_at), x.paid_starts_at) > {interval.StartsAt}
            """).ToListAsync(ct);

    public async Task SaveAsync(ShopTrialConsentV1 consent, TrialDeliveryProof proof, ShopTrialDeliveryResultV1 result,
        TrialAffiliation? affiliation, TrialHistory? history, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_trial_inbox
                (event_id, shop_id, actor_id, operation_key, account_id, term_id, replay_id, digest, status, reason)
            VALUES ({consent.EventId}, {consent.ShopId}, {consent.ActorId}, {consent.Acceptance.OperationKey},
                {affiliation?.AccountId}, {history?.TermId}, {proof.ReplayId}, {proof.Digest}, {result.Status}, {result.Reason})
            """, ct);
        if (history is not null)
        {
            await InsertTrialAsync(history, ct);
            await publisher.PublishAsync(history.AccountId, history.ShopId, ct);
        }
    }

    private async Task InsertTrialAsync(TrialHistory trial, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ({trial.TermId}, {trial.AccountId}, 'Independent', 1, {trial.StartsAt}, {trial.EndsAt})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.trial_history
                (shop_id, account_id, term_id, event_id, operation_key, actor_id, membership_id, authority_version,
                policy_id, policy_revision, terms_version, member_cap, account_trial_cap, active_members,
                starts_at, ends_at, applied_at, consent_digest, development_fixture, computation_version)
            VALUES ({trial.ShopId}, {trial.AccountId}, {trial.TermId}, {trial.EventId}, {trial.OperationKey},
                {trial.ActorId}, {trial.MembershipId}, {trial.AuthorityVersion}, {trial.PolicyId}, {trial.PolicyRevision},
                {trial.TermsVersion}, {trial.MemberCap}, {trial.AccountTrialCap}, {trial.ActiveMembers}, {trial.StartsAt},
                {trial.EndsAt}, {trial.AppliedAt}, {trial.ConsentDigest}, {trial.DevelopmentFixture}, {TrialHistory.ComputationVersion})
            """, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trial storage unavailable ({Cause}).")]
    private static partial void LogStorageFailure(ILogger logger, string cause);
}
