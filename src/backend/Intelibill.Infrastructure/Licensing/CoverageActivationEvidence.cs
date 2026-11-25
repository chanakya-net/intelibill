using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>Paid access gates share one read of the current signed head while the shop fence is held.</summary>
internal sealed record CoverageActivationEvidence(Guid? SlotAssignment, Guid? Replacement, Guid? ReleasedReplacement)
{
    internal static Task<CoverageActivationEvidence?> ReadAsync(ApplicationDbContext db, Guid evidence, CancellationToken ct) =>
        db.Database.SqlQuery<CoverageActivationEvidence>($"""
            SELECT (s->>'slotAssignmentId')::uuid AS slot_assignment, (s->>'replacementId')::uuid AS replacement,
                (s->>'releasedReplacementId')::uuid AS released_replacement
            FROM public.license_grant_evidence CROSS JOIN LATERAL
                (SELECT convert_from(payload,'UTF8')::jsonb->'shop' AS s) head WHERE event_id={evidence}
            """).SingleOrDefaultAsync(ct);

    internal static async Task<bool> IsPendingAsync(ApplicationDbContext db, ShopLicenseState? shop, CancellationToken ct)
    {
        if (shop is not { Basis: "Paid", State: "Active" or "Grace", TermId: not null }) return false;
        var head = await ReadAsync(db, shop.EvidenceId, ct);
        if (head?.SlotAssignment is { } slot && await SlotActivationGate.FindPendingAsync(db, shop.ShopId, shop.AccountId,
                shop.AffiliationRequestId, slot, ct) is not null) return true;
        return head?.Replacement is { } change && !await ReplacementActivationGate.ActivatedAsync(db, change, shop.AccountId, shop.ShopId, ct);
    }
}
