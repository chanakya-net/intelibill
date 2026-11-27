using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>Paid and exception-bridge access gates share one read of the current signed head while the shop fence is held.</summary>
internal sealed record CoverageActivationEvidence(
    Guid? SlotAssignment, Guid? Replacement, Guid? ReleasedReplacement,
    Guid? ReplacementException, string? ReplacementExceptionLeg, long? ReplacementExceptionGeneration)
{
    internal static Task<CoverageActivationEvidence?> ReadAsync(ApplicationDbContext db, Guid evidence, CancellationToken ct) =>
        db.Database.SqlQuery<CoverageActivationEvidence>($"""
            SELECT (s->>'slotAssignmentId')::uuid AS slot_assignment, (s->>'replacementId')::uuid AS replacement,
                (s->>'releasedReplacementId')::uuid AS released_replacement,
                (s->>'replacementExceptionId')::uuid AS replacement_exception,
                s->>'replacementExceptionLeg' AS replacement_exception_leg,
                (s->>'replacementExceptionGeneration')::bigint AS replacement_exception_generation
            FROM public.license_grant_evidence CROSS JOIN LATERAL
                (SELECT convert_from(payload,'UTF8')::jsonb->'shop' AS s) head WHERE event_id={evidence}
            """).SingleOrDefaultAsync(ct);

    internal static async Task<bool> IsPendingAsync(ApplicationDbContext db, ShopLicenseState? shop, CancellationToken ct)
    {
        if (shop is not { State: "Active" or "Grace", TermId: not null }) return false;
        var head = await ReadAsync(db, shop.EvidenceId, ct);
        if (head is { ReplacementException: { } exceptionId, ReplacementExceptionLeg: CoverageReplacementExceptionsV1.IncomingPaidLeg or CoverageReplacementExceptionsV1.IncomingCourtesyLeg }
            && !await CoverageExceptionActivationGate.ActivatedAsync(db, exceptionId, head.ReplacementExceptionLeg, shop.AccountId, shop.ShopId, ct))
            return true;
        if (shop.Basis != "Paid") return false;
        if (head?.SlotAssignment is { } slot && await SlotActivationGate.FindPendingAsync(db, shop.ShopId, shop.AccountId,
                shop.AffiliationRequestId, slot, ct) is not null) return true;
        return head?.Replacement is { } change && !await ReplacementActivationGate.ActivatedAsync(db, change, shop.AccountId, shop.ShopId, ct);
    }
}
