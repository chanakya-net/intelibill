using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>
/// An assigned slot may trade only after this shop's own live member check succeeded under the licence fence.
/// The assignment is named by the signed head itself, so a head without one never reads the slot tables.
/// </summary>
internal static class SlotActivationGate
{
    /// <summary>True while the saved paid head names an accepted assignment that has not passed its seat check.</summary>
    internal static async Task<bool> IsPendingAsync(ApplicationDbContext db, ShopLicenseState? shop, CancellationToken ct) =>
        shop is { Basis: "Paid", State: "Active" or "Grace", TermId: not null }
        && await ReadHeadAssignmentAsync(db, shop.EvidenceId, ct) is { } assignment
        && await FindPendingAsync(db, shop.ShopId, shop.AccountId, shop.AffiliationRequestId, assignment, ct) is not null;

    internal static async Task<Guid?> ReadHeadAssignmentAsync(ApplicationDbContext db, Guid evidenceId, CancellationToken ct)
    {
        var raw = await db.Database.SqlQuery<string?>($"""
            SELECT convert_from(payload, 'UTF8')::jsonb->'shop'->>'slotAssignmentId' AS "Value"
            FROM public.license_grant_evidence WHERE event_id = {evidenceId}
            """).SingleOrDefaultAsync(ct);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    internal static Task<Guid?> FindPendingAsync(ApplicationDbContext db, Guid shop, Guid account, Guid affiliationRequest,
        Guid assignment, CancellationToken ct) =>
        db.Database.SqlQuery<Guid?>($"""
            SELECT e.assignment_id AS "Value" FROM public.coverage_slot_consent_events e
            WHERE e.shop_id = {shop} AND e.account_id = {account} AND e.affiliation_request_id = {affiliationRequest}
              AND e.assignment_id = {assignment} AND e.kind = 'Accepted'
              AND NOT EXISTS (SELECT FROM public.coverage_slot_activations a WHERE a.assignment_id = e.assignment_id)
            LIMIT 1
            """).SingleOrDefaultAsync(ct);

    internal static Task<int> RecordAsync(ApplicationDbContext db, Guid assignment, Guid shop, Guid account, Guid eventId,
        DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.coverage_slot_activations (assignment_id, shop_id, account_id, event_id, activated_at)
            VALUES ({assignment}, {shop}, {account}, {eventId}, {at})
            ON CONFLICT (assignment_id) DO NOTHING
            """, ct);
}
