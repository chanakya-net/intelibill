using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class CheckoutOrderStore
{
    public async Task<CoverageReplacementOrderV1?> ReplacementAsync(Guid account, Guid term, int capacity, int allowance,
        DateTimeOffset boundary, DateTimeOffset now, CancellationToken ct)
    {
        var candidate = await db.Database.SqlQuery<Candidate>($"""
            SELECT replacement_id,outgoing_shop_id,incoming_shop_id,incoming_affiliation_id,consent_event,source_hash
            FROM public.coverage_replacements r WHERE account_id={account} AND term_id={term} AND state='Scheduled'
                AND consent_event IS NOT NULL AND revoke_event IS NULL AND capacity={capacity} AND member_allowance={allowance}
                AND effective_at={boundary} AND public.replacement_term_available({account},{term})
                AND public.replacement_owner_current({account},replacement_id)
                AND public.slot_shop_eligible({account},incoming_shop_id,{now},'infinity')
                AND EXISTS(SELECT FROM public.shop_affiliations a WHERE a.account_id=r.account_id AND a.shop_id=r.incoming_shop_id
                    AND a.request_id=r.incoming_affiliation_id)
            """).SingleOrDefaultAsync(ct);
        if (candidate is null) return null;
        var source = await changes.SourceAsync(account,term,now,ct);
        return source is { Closed: false, Superseded: false } && source.Hash() == candidate.SourceHash
            ? new(candidate.ReplacementId, candidate.OutgoingShopId, candidate.IncomingShopId,
                candidate.IncomingAffiliationId, candidate.ConsentEvent) : null;
    }

    private sealed record Candidate(Guid ReplacementId, Guid OutgoingShopId, Guid IncomingShopId,
        Guid IncomingAffiliationId, Guid ConsentEvent, string SourceHash);
}
