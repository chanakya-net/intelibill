using Intelibill.Licensing.Application.Restrictions;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class SuspensionStore
{
    public async Task<SurvivingRestriction?> ReadSurvivingAsync(Guid account, Guid shop, Guid excluded, DateTimeOffset at,
        CancellationToken ct)
    {
        var remaining = await ReadCurrentAsync(account, shop, excluded, at, ct);
        if (remaining is not null) return remaining;
        // Older account restrictions have no reasoned application to preview or release.
        return await db.Database.SqlQuery<SurvivingRestriction>($"""
            SELECT NULL::uuid AS id, h.restriction AS mode, NULL::timestamptz AS effective_at,
                'Contact support about shop access.' AS customer_explanation
            FROM public.entitlement_account_heads h
            WHERE h.account_id = {account} AND h.restriction IN ('Trading', 'Security') AND NOT EXISTS
                (SELECT FROM public.active_operational_restrictions r WHERE r.account_id = {account} AND r.shop_id IS NULL)
            """).SingleOrDefaultAsync(ct);
    }

    public Task<SavedRelease?> ReadReleaseAsync(Guid account, Guid restriction, CancellationToken ct) =>
        db.Database.SqlQuery<SavedRelease>($"""
            SELECT * FROM public.operational_restriction_releases WHERE account_id = {account} AND restriction_id = {restriction}
            """).SingleOrDefaultAsync(ct);

    public Task<SavedRelease?> ReadReleaseOperationAsync(Guid account, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<SavedRelease>($"""
            SELECT * FROM public.operational_restriction_releases WHERE account_id = {account} AND operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    public async Task ReleaseAsync(SavedSuspension saved, SavedRelease release, IReadOnlyList<ReleaseShopImpact> shops,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_restriction_releases
                (id, restriction_id, account_id, version, prior_revision, released_at, released_by, session_id,
                 mfa_verified_at, reason, operation_key, payload_digest)
            VALUES ({release.Id}, {saved.Id}, {saved.AccountId}, {release.Version},
                CASE WHEN {saved.ShopId}::uuid IS NULL THEN
                    (SELECT revision FROM public.entitlement_account_heads WHERE account_id = {saved.AccountId})
                ELSE (SELECT revision FROM public.entitlement_shop_heads
                    WHERE account_id = {saved.AccountId} AND shop_id = {saved.ShopId}) END,
                {release.ReleasedAt}, {release.ReleasedBy}, {release.SessionId}, {release.MfaVerifiedAt},
                {release.Reason}, {release.OperationKey}, {release.PayloadDigest})
            """, ct);

        if (saved.ShopId is null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public.entitlement_account_heads SET revision = revision + 1, released_revision = revision,
                    previous_release_revision = NULL, restriction = 'Unrestricted', upstream_verified_at = greatest(upstream_verified_at, {release.ReleasedAt})
                WHERE account_id = {saved.AccountId}
                """, ct);

        foreach (var shop in shops)
        {
            var snapshot = await publisher.PublishSuspensionAsync(saved.AccountId, shop.ShopId, release.ReleasedAt, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.operational_restriction_release_deliveries (release_id, account_id, shop_id, event_id)
                VALUES ({release.Id}, {saved.AccountId}, {shop.ShopId}, {snapshot.EventId})
                """, ct);
        }
    }

    public async Task<IReadOnlyList<SuspensionDelivery>> ReadReleaseDeliveryAsync(Guid release, CancellationToken ct) =>
        await db.Database.SqlQuery<SuspensionDelivery>($"""
            SELECT a.shop_id, a.shop_name, delivery.account_revision, delivery.shop_revision,
                CASE delivery.status WHEN 'Applied' THEN 'Acknowledged'
                    WHEN 'NeedsReview' THEN 'NeedsReview' WHEN 'DeadLetter' THEN 'NeedsReview'
                    ELSE 'Pending' END AS status
            FROM public.operational_restriction_releases l
            JOIN public.operational_restrictions r ON r.id = l.restriction_id
            JOIN public.shop_affiliations a ON a.account_id = l.account_id AND (r.shop_id IS NULL OR r.shop_id = a.shop_id)
            LEFT JOIN public.operational_restriction_release_deliveries d ON d.release_id = l.id AND d.shop_id = a.shop_id
            LEFT JOIN public.entitlement_snapshots original ON original.event_id = d.event_id
            LEFT JOIN LATERAL (
                SELECT s.account_revision, s.shop_revision, o.status
                FROM public.entitlement_snapshots s
                JOIN public.entitlement_outbox o USING (event_id)
                JOIN public.entitlement_messages m USING (event_id)
                CROSS JOIN LATERAL (SELECT convert_from(m.payload, 'UTF8')::jsonb AS payload) wire
                WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id AND s.affiliation_request_id = a.request_id
                    AND (original.affiliation_request_id IS DISTINCT FROM a.request_id OR
                        (s.account_revision >= original.account_revision AND s.shop_revision >= original.shop_revision))
                    AND CASE WHEN r.shop_id IS NULL THEN coalesce((wire.payload->'account'->>'releasedRevision')::bigint,
                        (wire.payload->'account'->>'previousReleaseRevision')::bigint)
                        ELSE (wire.payload->'shop'->>'releasedRevision')::bigint END >= l.prior_revision
                ORDER BY (o.status = 'Applied') DESC, s.shop_revision DESC
                LIMIT 1
            ) delivery ON true
            WHERE l.id = {release}
            ORDER BY a.shop_id
            """).ToListAsync(ct);
}
