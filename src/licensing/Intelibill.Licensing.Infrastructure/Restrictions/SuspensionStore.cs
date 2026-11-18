using Intelibill.Shared.Contracts.V1;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class SuspensionStore(LicensingDbContext db, EntitlementPublisher publisher) : ISuspensionStore
{
    public async Task<CustomerSuspensionV1?> ReadCustomerAsync(Guid account, Guid shop, DateTimeOffset at, CancellationToken ct) =>
        await ReadCurrentAsync(account, shop, null, at, ct) is { EffectiveAt: { } effectiveAt } restriction
            ? new(restriction.Mode, effectiveAt, restriction.CustomerExplanation) : null;

    private Task<SurvivingRestriction?> ReadCurrentAsync(Guid account, Guid shop, Guid? excluded, DateTimeOffset at, CancellationToken ct) =>
        db.Database.SqlQuery<SurvivingRestriction>($"""
            SELECT id, mode, effective_at, customer_explanation FROM public.active_operational_restrictions
            WHERE account_id = {account} AND (shop_id IS NULL OR shop_id = {shop}) AND id IS DISTINCT FROM {excluded}::uuid
            ORDER BY CASE WHEN effective_at > {at} THEN 2 WHEN mode = 'Security' THEN 0 ELSE 1 END,
                effective_at, id LIMIT 1
            """).SingleOrDefaultAsync(ct);

    public Task LockAccountAsync(Guid account, CancellationToken ct) => BillingAccountLock.TakeAsync(db, account, ct);

    public async Task<string?> ReadAccountNameAsync(Guid account, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.restriction_account_id', {account.ToString("D")}, true)
            """, ct);
        return await db.Database.SqlQuery<string>($"SELECT name AS \"Value\" FROM public.billing_accounts WHERE id = {account}")
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SuspensionShop>> ReadShopsAsync(Guid account, Guid? shop, CancellationToken ct) =>
        await db.Database.SqlQuery<SuspensionShop>($"""
            SELECT a.shop_id, a.shop_name, a.request_id AS affiliation_id, t.mode AS coverage_mode
            FROM public.shop_affiliations a
            LEFT JOIN public.subscription_coverage_terms t USING (account_id, term_id)
            WHERE a.account_id = {account} AND ({shop}::uuid IS NULL OR a.shop_id = {shop})
            ORDER BY a.shop_id
            """).ToListAsync(ct);

    public async Task<(long Version, bool Active)> ReadVersionAsync(Guid account, Guid? shop, CancellationToken ct)
    {
        var head = await db.Database.SqlQuery<ScopeVersion>($"""
            SELECT COALESCE(l.version, r.version) AS version, l.id IS NULL AS active
            FROM public.operational_restrictions r LEFT JOIN public.operational_restriction_releases l ON l.restriction_id = r.id
            WHERE r.account_id = {account} AND r.shop_id IS NOT DISTINCT FROM {shop}::uuid
            ORDER BY r.version DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (head is not null) return (head.Version, head.Active);
        if (shop is not null) return (0, false);

        var legacyRevision = await db.Database.SqlQuery<long?>($"""
            SELECT revision AS "Value" FROM public.entitlement_account_heads
            WHERE account_id = {account} AND restriction IN ('Trading', 'Security')
            """).SingleOrDefaultAsync(ct);
        return (legacyRevision ?? 0, legacyRevision is not null);
    }

    public Task<SavedSuspension?> ReadOperationAsync(Guid account, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<SavedSuspension>($"""
            SELECT * FROM public.operational_restrictions WHERE account_id = {account} AND operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    public Task<SavedSuspension?> ReadAsync(Guid account, Guid id, CancellationToken ct) =>
        db.Database.SqlQuery<SavedSuspension>($"""
            SELECT * FROM public.operational_restrictions WHERE account_id = {account} AND id = {id}
            """).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SavedSuspension>> ListAsync(Guid account, CancellationToken ct) =>
        await db.Database.SqlQuery<SavedSuspension>($"""
            SELECT * FROM public.operational_restrictions WHERE account_id = {account}
            ORDER BY recorded_at DESC, id
            """).ToListAsync(ct);

    public async Task SaveAsync(SavedSuspension saved, IReadOnlyList<SuspensionShop> shops, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_restrictions
                (id, account_id, shop_id, version, mode, effective_at, customer_explanation, internal_reason,
                 review_reference, actor_id, session_id, recorded_at, operation_key, payload_digest)
            VALUES ({saved.Id}, {saved.AccountId}, {saved.ShopId}, {saved.Version}, {saved.Mode}, {saved.EffectiveAt},
                {saved.CustomerExplanation}, {saved.InternalReason}, {saved.ReviewReference},
                {saved.ActorId}, {saved.SessionId}, {saved.RecordedAt}, {saved.OperationKey}, {saved.PayloadDigest})
            """, ct);
        if (saved.ShopId is null)
            await AdvanceAccountAsync(saved, ct);

        foreach (var shop in shops)
        {
            var snapshot = await publisher.PublishSuspensionAsync(saved.AccountId, shop.ShopId, saved.RecordedAt, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.operational_restriction_deliveries
                    (restriction_id, account_id, shop_id, shop_name, event_id)
                VALUES ({saved.Id}, {saved.AccountId}, {shop.ShopId}, {shop.ShopName}, {snapshot.EventId})
                """, ct);
        }
    }

    public async Task<IReadOnlyList<SuspensionDelivery>> ReadDeliveryAsync(Guid id, CancellationToken ct) =>
        await db.Database.SqlQuery<SuspensionDelivery>($"""
            SELECT a.shop_id, a.shop_name, delivery.account_revision, delivery.shop_revision,
                CASE delivery.status WHEN 'Applied' THEN 'Acknowledged'
                    WHEN 'NeedsReview' THEN 'NeedsReview' WHEN 'DeadLetter' THEN 'NeedsReview'
                    ELSE 'Pending' END AS status
            FROM public.operational_restrictions r
            JOIN public.shop_affiliations a ON a.account_id = r.account_id
                AND (r.shop_id IS NULL OR r.shop_id = a.shop_id)
            LEFT JOIN public.operational_restriction_releases released ON released.restriction_id = r.id
            LEFT JOIN public.operational_restriction_deliveries d ON d.restriction_id = r.id AND d.shop_id = a.shop_id
            LEFT JOIN public.entitlement_snapshots original ON original.event_id = d.event_id
            LEFT JOIN LATERAL (
                SELECT max(l.prior_revision) AS revision
                FROM public.operational_restriction_releases l JOIN public.operational_restrictions prior ON prior.id = l.restriction_id
                WHERE prior.account_id = r.account_id AND prior.shop_id IS NOT DISTINCT FROM r.shop_id AND prior.version < r.version
            ) previous ON true
            LEFT JOIN LATERAL (
                SELECT s.account_revision, s.shop_revision, o.status
                FROM public.entitlement_snapshots s
                JOIN public.entitlement_outbox o USING (event_id)
                JOIN public.entitlement_messages m USING (event_id)
                CROSS JOIN LATERAL (
                    SELECT convert_from(m.payload, 'UTF8')::jsonb
                        -> (CASE WHEN r.shop_id IS NULL THEN 'account' ELSE 'shop' END)
                        -> 'suspension' AS facts,
                        CASE WHEN r.shop_id IS NULL THEN s.account_revision ELSE s.shop_revision END AS revision
                ) proof
                WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id
                    AND s.affiliation_request_id = a.request_id
                    AND (original.affiliation_request_id IS DISTINCT FROM a.request_id OR
                        (s.account_revision >= original.account_revision AND s.shop_revision >= original.shop_revision))
                    AND (previous.revision IS NULL OR proof.revision > previous.revision + 1)
                    AND (released.prior_revision IS NULL OR proof.revision <= released.prior_revision)
                    AND proof.facts->>'mode' = r.mode
                    AND (proof.facts->>'effectiveAt')::timestamptz = r.effective_at
                    AND proof.facts->>'customerExplanation' = r.customer_explanation
                ORDER BY (o.status = 'Applied') DESC, s.shop_revision DESC
                LIMIT 1
            ) delivery ON true
            WHERE r.id = {id}
                AND (released.restriction_id IS NULL OR d.event_id IS NOT NULL OR EXISTS (
                    SELECT FROM public.entitlement_snapshots s
                    WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id
                        AND s.affiliation_request_id = a.request_id
                        AND (CASE WHEN r.shop_id IS NULL THEN s.account_revision ELSE s.shop_revision END)
                            <= released.prior_revision
                ))
            ORDER BY a.shop_id
            """).ToListAsync(ct);

    private sealed record ScopeVersion(long Version, bool Active);

    private Task<int> AdvanceAccountAsync(SavedSuspension saved, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.entitlement_account_heads (account_id, revision, upstream_verified_at, restriction)
        VALUES ({saved.AccountId}, 1, {saved.RecordedAt}, {saved.Mode})
        ON CONFLICT (account_id) DO UPDATE SET revision = entitlement_account_heads.revision + 1,
            restriction = EXCLUDED.restriction,
            previous_release_revision = coalesce(entitlement_account_heads.released_revision, entitlement_account_heads.previous_release_revision),
            released_revision = NULL
        """, ct);
}
