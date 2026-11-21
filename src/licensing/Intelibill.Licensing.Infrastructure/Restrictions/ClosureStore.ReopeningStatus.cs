using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public Task<ReopeningStamp?> ReadLatestReopeningAsync(Guid account, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<ReopeningStamp>($"""
            SELECT r.reopened_at, r.prior_revision FROM public.operational_closure_reopenings r
            JOIN public.shop_affiliations a ON a.account_id = r.account_id AND a.shop_id = r.shop_id
                AND a.request_id = r.affiliation_request_id
            WHERE r.account_id = {account} AND r.shop_id = {shop}
            ORDER BY r.version DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    public Task<ProviderReopeningV1?> ReadReopeningStatusAsync(Guid account, Guid closure, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<ProviderReopeningV1>($"""
            SELECT r.id, r.reopened_at,
                CASE WHEN delivery.applied THEN 'Acknowledged'
                    WHEN delivery.status IN ('NeedsReview', 'DeadLetter') THEN 'NeedsReview'
                    ELSE 'Pending' END AS propagation
            FROM public.operational_closure_reopenings r
            JOIN public.entitlement_snapshots original ON original.event_id = r.event_id
            LEFT JOIN LATERAL (
                SELECT sent.status, (sent.status = 'Applied' AND
                    coalesce(convert_from(ack.payload, 'UTF8')::jsonb->>'disposition' = 'Applied', false)) AS applied
                FROM public.entitlement_snapshots s
                JOIN public.entitlement_outbox sent USING (event_id)
                JOIN public.entitlement_messages m USING (event_id)
                LEFT JOIN public.entitlement_merchant_inbox ack ON ack.event_id = s.event_id
                    AND ack.account_id = s.account_id AND ack.kind = 'Acknowledgement'
                CROSS JOIN LATERAL (SELECT convert_from(m.payload, 'UTF8')::jsonb->'shop' AS head) wire
                WHERE s.account_id = r.account_id AND s.shop_id = r.shop_id AND s.affiliation_request_id = r.affiliation_request_id
                    AND s.shop_revision >= original.shop_revision AND s.account_revision >= original.account_revision
                    AND (wire.head->>'reopenedRevision')::bigint >= r.prior_revision
                ORDER BY applied DESC, s.shop_revision DESC LIMIT 1
            ) delivery ON true
            WHERE r.account_id = {account} AND r.closure_id = {closure} AND r.shop_id = {shop}
            """).SingleOrDefaultAsync(ct);
}
