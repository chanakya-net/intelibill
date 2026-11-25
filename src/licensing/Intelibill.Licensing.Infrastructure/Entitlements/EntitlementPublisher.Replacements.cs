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
                    AND r.incoming_coverage_from>{now}) AS next_start
            """).SingleAsync(ct);

    private sealed record ReplacementHead(Guid? Incoming, Guid? Released, DateTimeOffset? NextStart);
}
