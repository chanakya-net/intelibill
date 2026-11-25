using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed class CoverageSlotConsentAcknowledgements(LicensingDbContext db, CoverageSlotAssignmentStore slots,
    EntitlementPublisher publisher, [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock)
{
    internal async Task<bool> ApplyAsync(CoverageSlotConsentV1 consent, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.slot_assignment_id',{consent.AssignmentId.ToString("D")},true),
                pg_advisory_xact_lock(hashtextextended('g'||{consent.ShopId}::text,0))
            """, ct);
        var row = await db.Database.SqlQuery<RequestRow>($"""
            SELECT requester_id,requester_revision,created_at,expires_at,capacity,cancellation_revision,state
            FROM public.coverage_slot_assignments WHERE assignment_id={consent.AssignmentId} AND account_id={consent.AccountId}
                AND shop_id={consent.ShopId} AND affiliation_request_id={consent.AffiliationRequestId} AND term_id={consent.TermId}
                AND coverage_ends_at={consent.CoverageEndsAt} AND member_allowance={consent.MemberAllowance}
            """).SingleOrDefaultAsync(ct);
        if (row is null || consent.AcceptedAt < row.CreatedAt || consent.AcceptedAt >= row.ExpiresAt || consent.AcceptedAt > now)
            return false;
        if (row.State == "Expired") return true;
        if (row.State != "AwaitingConsent") return false;
        var reason = await RefusalAsync(consent, row, now, ct);
        var updated = await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_slot_assignments SET state={(reason is null ? "Applied" : "Refused")},
                consent_event={consent.EventId},reason={reason}::text,applied_at={(reason is null ? (DateTimeOffset?)now : null)}::timestamptz
            WHERE account_id={consent.AccountId} AND assignment_id={consent.AssignmentId} AND state='AwaitingConsent'
            """, ct);
        if (updated != 1) return false;
        if (reason is null) await PublishAsync(consent, now, ct);
        return true;
    }

    private async Task<string?> RefusalAsync(CoverageSlotConsentV1 c, RequestRow row, DateTimeOffset now, CancellationToken ct)
    {
        var source = await slots.SourceAsync(c.AccountId, c.TermId, now, ct);
        if (now >= c.CoverageEndsAt || source is not null && now >= source.EndsAt) return "TermEnded";
        if (now >= row.ExpiresAt) return "RequestExpired";
        if (c.ActiveMembers > c.MemberAllowance) return "MemberCapacityExceeded";
        if (await ClosureStore.AccountClosedAsync(db, c.AccountId, ct)) return "AccountClosed";
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.billing_actor_id',{row.RequesterId.ToString("D")},true)", ct);
        var authorized = await db.Database.SqlQuery<int>($"""
            SELECT revision AS "Value" FROM public.billing_account_members WHERE account_id={c.AccountId}
                AND merchant_user_id={row.RequesterId} AND status=1 AND role IN (1,2) FOR SHARE
            """).ToListAsync(ct);
        if (authorized is not [var revision] || revision != row.RequesterRevision) return "RequesterChanged";
        if (source is null || source.Capacity != row.Capacity || source.Members != c.MemberAllowance
            || source.EndsAt != c.CoverageEndsAt || source.CancellationRevision != row.CancellationRevision
            || source.CancellationRevision % 2 != 0)
            return "SourceChanged";
        if (!source.Available) return "SlotUnavailable";
        var eligible = await db.Database.SqlQuery<bool>($"""
            SELECT public.slot_shop_eligible({c.AccountId},{c.ShopId},{now},{source.EndsAt}) AS "Value"
            """).SingleAsync(ct);
        return eligible ? null : "ShopCovered";
    }

    private async Task PublishAsync(CoverageSlotConsentV1 c, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.payment_fulfilment_shops(account_id,order_id,term_id,shop_id,affiliation_request_id)
                SELECT account_id,order_id,term_id,shop_id,affiliation_request_id FROM public.coverage_slot_assignments
                WHERE assignment_id={c.AssignmentId} AND account_id={c.AccountId}
            """, ct);
        var snapshot = await publisher.PublishShopChangeAsync(c.AccountId, c.ShopId, now, ct);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_slot_assignment_deliveries(assignment_id,account_id,shop_id,event_id)
            VALUES({c.AssignmentId},{c.AccountId},{c.ShopId},{snapshot.EventId})
            """, ct);
    }

    private sealed record RequestRow(Guid RequesterId, int RequesterRevision, DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt, int Capacity, long CancellationRevision, string State);
}
