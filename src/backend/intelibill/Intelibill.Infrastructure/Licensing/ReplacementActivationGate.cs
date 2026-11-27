using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

internal static class ReplacementActivationGate
{
    internal static Task<bool> ActivatedAsync(ApplicationDbContext db, Guid id, Guid account, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM public.coverage_replacement_activations
                WHERE replacement_id={id} AND account_id={account} AND shop_id={shop}) AS "Value"
            """).SingleAsync(ct);

    internal static async Task<string> CheckAsync(ApplicationDbContext db, EntitlementSnapshotV1 snapshot, Guid id,
        DateTimeOffset now, CancellationToken ct)
    {
        if (await ActivatedAsync(db, id, snapshot.AccountId, snapshot.ShopId, ct)) return "Activated";
        var consent = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM public.coverage_replacement_consent_events e
                JOIN public.shop_memberships m ON m.shop_id=e.shop_id AND m.id=e.membership_id AND m.user_id=e.actor_id
                WHERE e.replacement_id={id} AND e.shop_id={snapshot.ShopId} AND e.account_id={snapshot.AccountId}
                    AND e.affiliation_request_id={snapshot.AffiliationRequestId} AND e.kind='Accepted'
                    AND m.authority_version=e.authority_version AND m.role='Owner' AND m.status='Active'
                    AND e.member_allowance={snapshot.Shop!.MemberAllowance}
                    AND NOT EXISTS(SELECT FROM public.coverage_replacement_consent_events v WHERE v.replacement_id=e.replacement_id
                        AND v.kind='Revoked') AND (SELECT count(*) FROM public.shop_memberships
                        WHERE shop_id=e.shop_id AND status='Active')<=e.member_allowance) AS "Value"
            """).SingleAsync(ct);
        if (!consent) return "Invalid";
        var released = await db.Database.SqlQuery<bool>($"""
            SELECT public.replacement_outgoing_released({id},{now}) AS "Value"
            """).SingleAsync(ct);
        return released ? "Ready" : "Waiting";
    }

    internal static Task<int> RecordAsync(ApplicationDbContext db, EntitlementSnapshotV1 s, Guid id,
        DateTimeOffset now, CancellationToken ct) => db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_replacement_activations(replacement_id,shop_id,account_id,event_id,activated_at)
            VALUES({id},{s.ShopId},{s.AccountId},{s.EventId},{now})
            """, ct);
}
