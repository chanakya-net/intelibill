using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed partial class EntitlementPublisher
{
    private Task<ClosureFacts> ReadClosureAsync(Guid account, Guid shop, Guid affiliation, Guid? eventId,
        long revision, CancellationToken ct) => db.Database.SqlQuery<ClosureFacts>($"""
            WITH targets AS (
                SELECT closure_id, event_id FROM public.operational_closure_targets
                WHERE account_id = {account} AND shop_id = {shop} AND affiliation_request_id = {affiliation}
            ), reviews AS (
                SELECT closure_id, event_id, prior_revision, opens_shop FROM public.operational_closure_reopenings
                WHERE account_id = {account} AND shop_id = {shop} AND affiliation_request_id = {affiliation}
            ), transitions AS (
                SELECT event_id FROM targets UNION ALL SELECT event_id FROM reviews
            )
            SELECT EXISTS (SELECT FROM targets t WHERE NOT EXISTS (
                SELECT FROM reviews r WHERE r.closure_id = t.closure_id)) AS closed,
                (SELECT max(prior_revision) FROM reviews WHERE opens_shop) AS opening_cutoff,
                CASE WHEN EXISTS (SELECT FROM reviews) THEN (
                    SELECT max(CASE WHEN t.event_id = {eventId}::uuid THEN {revision} ELSE s.shop_revision END)
                    FROM transitions t LEFT JOIN public.entitlement_snapshots s ON s.event_id = t.event_id
                ) END AS revision
            """).SingleAsync(ct);

    private sealed record ClosureFacts(bool Closed, long? OpeningCutoff, long? Revision);
}
