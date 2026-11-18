using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    public async Task<PaymentRestorationEvidence> ReadRestorationEvidenceAsync(Guid account, DateTimeOffset at, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<AccountRestrictionSnapshotV1>($"""
            SELECT revision, upstream_verified_at, restriction, released_revision
            FROM public.entitlement_account_heads WHERE account_id = {account} FOR UPDATE
            """).ToListAsync(ct);
        return new(at, rows.SingleOrDefault(), transport.HasPublicationRoute());
    }

    public Task<PaymentRestorationHold?> FindRestorationHoldAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<PaymentRestorationHold>($"""
            SELECT attempt_id, manual_receipt_id, effective_at, held_at, account_revision, restriction
            FROM public.payment_restoration_holds WHERE account_id = {account} AND order_id = {order}
            """).SingleOrDefaultAsync(ct);

    public Task RecordRestorationHoldAsync(Guid account, Guid order, Guid? attempt, Guid? receipt,
        DateTimeOffset effectiveAt, PaymentRestorationEvidence evidence, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.payment_restoration_holds (account_id, order_id, attempt_id, manual_receipt_id,
                effective_at, held_at, account_revision, restriction, upstream_verified_at)
            VALUES ({account}, {order}, {attempt}, {receipt}, {effectiveAt}, {evidence.At},
                {evidence.Head?.Revision}, {evidence.Head?.Restriction}, {evidence.Head?.UpstreamVerifiedAt})
            """, ct);

    private Task<ServiceRestorationAdjustmentV1?> ReadAdjustmentAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<ServiceRestorationAdjustmentV1>($"""
            SELECT count(*) OVER ()::int AS count, g.grant_id, g.starts_at, g.ends_at
            FROM public.service_restoration_adjustments a
            JOIN public.complimentary_grants g USING (account_id, grant_id)
            WHERE a.account_id = {account} AND a.order_id = {order}
            ORDER BY g.granted_version DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    private Task<List<PaymentShopDelivery>> ReadRequiredDeliveriesAsync(Guid account, Guid order, DateTimeOffset? startsAt,
        CancellationToken ct) => db.Database.SqlQuery<PaymentShopDelivery>($"""
            SELECT d.shop_id, CASE WHEN f.ready_at < {startsAt} THEN post_start.revision
                ELSE d.shop_revision END AS shop_revision
            FROM public.payment_fulfilment_deliveries d
            LEFT JOIN public.paid_fresh_starts f USING (account_id, order_id)
            LEFT JOIN LATERAL (
                SELECT s.shop_revision AS revision
                FROM public.entitlement_snapshots s JOIN public.entitlement_messages m USING (event_id)
                WHERE f.ready_at < {startsAt} AND s.account_id = d.account_id AND s.shop_id = d.shop_id
                    AND s.shop_revision >= d.shop_revision
                    AND (convert_from(m.payload, 'UTF8')::jsonb->'shop'->>'upstreamVerifiedAt')::timestamptz >= {startsAt}
                ORDER BY s.shop_revision LIMIT 1
            ) post_start ON true
            WHERE d.account_id = {account} AND d.order_id = {order} ORDER BY d.shop_id
            """).ToListAsync(ct);

    private Task<int> RecordFreshStartAsync(PaymentFulfilment fulfilment, CancellationToken ct)
    {
        var fresh = fulfilment.Fresh!;
        var head = fresh.Evidence.Head!;
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.paid_fresh_starts (account_id, order_id, term_id, kind, effective_at, ready_at,
                account_revision, released_revision, upstream_verified_at)
            VALUES ({fulfilment.AccountId}, {fulfilment.OrderId}, {fulfilment.Term.TermId}, {fresh.Kind}, {fresh.EffectiveAt},
                {fresh.Evidence.At}, {head.Revision}, {head.ReleasedRevision}, {head.UpstreamVerifiedAt})
            """, ct);
    }

    private Task<int> RecordTrialConversionsAsync(PaymentFulfilment fulfilment, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.payment_trial_conversions (account_id, order_id, shop_id, trial_term_id, paid_starts_at)
            SELECT account_id, order_id, shop_id, trial_term_id, {fulfilment.Term.StartsAt}
            FROM public.checkout_trial_sources WHERE account_id = {fulfilment.AccountId} AND order_id = {fulfilment.OrderId}
            """, ct);

    private Task<int> RefreshRestorationHeadAsync(PaymentFulfilment fulfilment, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE public.entitlement_account_heads
            SET upstream_verified_at = {fulfilment.At}, revision = revision + 1
            WHERE account_id = {fulfilment.AccountId} AND upstream_verified_at < {fulfilment.At}
            """, ct);
}
