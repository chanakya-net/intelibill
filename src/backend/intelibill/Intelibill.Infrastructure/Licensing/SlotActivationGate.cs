using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>
/// An assigned slot may trade only after this shop's own live member check succeeded under the licence fence.
/// The assignment is named by the signed head itself, so a head without one never reads the slot tables.
/// </summary>
internal static class SlotActivationGate
{
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
