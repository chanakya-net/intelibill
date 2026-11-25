using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed partial class EntitlementPublisher
{
    private Task<ReplacementHead> ReadReplacementAsync(Guid account, Guid shop, Guid request, Guid? term,
        DateTimeOffset now, CancellationToken ct) => db.Database.SqlQuery<ReplacementHead>($"""
            SELECT (SELECT l.replacement_id FROM public.payment_fulfilment_shops l WHERE l.account_id={account}
                AND l.shop_id={shop} AND l.affiliation_request_id={request} AND l.term_id={term}::uuid) AS incoming,
                (SELECT r.replacement_id FROM public.coverage_replacements r WHERE r.account_id={account}
                    AND r.outgoing_shop_id={shop} AND r.outgoing_affiliation_id={request} AND r.state='Applied'
                    ORDER BY effective_at DESC LIMIT 1) AS released,
                (SELECT min(r.incoming_coverage_from) FROM public.coverage_replacements r WHERE r.account_id={account}
                    AND r.incoming_shop_id={shop} AND r.incoming_affiliation_id={request} AND r.state='Applied'
                    AND r.incoming_coverage_from>{now}) AS next_start,
                ex.exception_id, ex.exception_leg, ex.exception_generation, ex.exception_next
            FROM (
                SELECT
                    coalesce(cur.exception_id, NULL::uuid) AS exception_id,
                    cur.exception_leg,
                    cur.exception_generation,
                    (SELECT min(boundary) FROM (
                        SELECT h.courtesy_starts_at AS boundary FROM public.paid_slot_handoffs h
                        WHERE h.account_id = {account} AND h.incoming_shop_id = {shop}
                            AND h.incoming_affiliation_id = {request} AND h.courtesy_starts_at > {now}
                        UNION ALL
                        SELECT h.cutover_at AS boundary FROM public.paid_slot_handoffs h
                        WHERE h.account_id = {account}
                            AND ((h.outgoing_shop_id = {shop} AND h.outgoing_affiliation_id = {request})
                                OR (h.incoming_shop_id = {shop} AND h.incoming_affiliation_id = {request}))
                            AND h.cutover_at > {now}
                    ) b) AS exception_next
                FROM (SELECT 1) one
                LEFT JOIN LATERAL (
                    SELECT h.exception_id,
                        CASE
                            WHEN h.outgoing_shop_id = {shop} AND h.outgoing_affiliation_id = {request}
                                AND h.term_id = {term}::uuid THEN 'Outgoing'
                            WHEN h.incoming_shop_id = {shop} AND h.incoming_affiliation_id = {request}
                                AND h.courtesy_term_id = {term}::uuid THEN 'IncomingCourtesy'
                            WHEN h.incoming_shop_id = {shop} AND h.incoming_affiliation_id = {request}
                                AND h.term_id = {term}::uuid THEN 'IncomingPaid'
                        END AS exception_leg,
                        h.generation AS exception_generation
                    FROM public.paid_slot_handoffs h
                    WHERE h.account_id = {account}
                        AND ((h.outgoing_shop_id = {shop} AND h.outgoing_affiliation_id = {request} AND h.term_id = {term}::uuid)
                            OR (h.incoming_shop_id = {shop} AND h.incoming_affiliation_id = {request}
                                AND (h.courtesy_term_id = {term}::uuid OR h.term_id = {term}::uuid)))
                    ORDER BY h.recorded_at DESC, h.generation DESC LIMIT 1
                ) cur ON true
            ) ex
            """).SingleAsync(ct);

    private sealed record ReplacementHead(Guid? Incoming, Guid? Released, DateTimeOffset? NextStart,
        Guid? ExceptionId, string? ExceptionLeg, long? ExceptionGeneration, DateTimeOffset? ExceptionNext);
}
