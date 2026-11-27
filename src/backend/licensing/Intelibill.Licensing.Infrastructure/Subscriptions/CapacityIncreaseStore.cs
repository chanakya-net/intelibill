using System.Text.Json;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Subscriptions;

internal sealed class CapacityIncreaseStore(LicensingDbContext db, TimeProvider clock) : ICapacityIncreaseStore
{
    public async Task<(CapacityChangeQuoteV1 Quote, long Revision)?> ReadAsync(CheckoutQuoteV1 order, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SourceRow>($"""
            SELECT snapshot::text, coalesce((SELECT max(i.target_revision) FROM public.capacity_increases i
                WHERE i.account_id = q.account_id AND i.term_id = q.term_id AND i.state = 'Applied'),0) AS revision
            FROM public.capacity_change_quotes q WHERE q.account_id = {order.AccountId} AND q.quote_id = {order.OrderId}
                AND q.term_id = {order.TermId} AND q.outcome = 'Payable'
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : (JsonSerializer.Deserialize<CapacityChangeQuoteV1>(row.Snapshot, EntitlementSnapshotsV1.Json)!, row.Revision);
    }

    public Task<bool> InFlightAsync(Guid account, Guid term, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.capacity_increases WHERE account_id = {account} AND term_id = {term}
                AND state = 'Pending') AS "Value"
            """).SingleAsync(ct);

    public async Task FundAsync(CapacityReservationV1 c, Guid? attempt, Guid? receipt, CancellationToken ct)
    {
        await AppendAsync(c, ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.capacity_increases (account_id, term_id, order_id, target_revision, attempt_id, receipt_id,
                reserve_event, state, capacity,members,gross,plan_version)
            SELECT account_id, term_id, quote_id, {c.TargetRevision}, {attempt}, {receipt}, {c.EventId}, 'Pending',
                target_capacity, target_member_allowance,target_gross_paise,target_plan_version_id
            FROM public.capacity_change_quotes WHERE account_id={c.AccountId} AND quote_id={c.OrderId}
            """, ct);
        if (inserted != 1) throw new InvalidOperationException("Verified capacity order has no retained quote.");
    }

    internal Task AppendAsync(CapacityReservationV1 c, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(c, EntitlementSnapshotsV1.Json);
        return new LeasedHttpOutbox(db).AppendAsync(c.EventId, c.AccountId, c.RoutingShopId, c.Phase, bytes,
            SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(clock), ct);
    }

    public Task<string?> StateAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<string>($"""
            SELECT CASE WHEN i.state = 'Pending' AND o.status = 'DeadLetter' THEN 'NeedsReview'
                ELSE i.state END AS "Value"
            FROM public.capacity_increases i JOIN public.entitlement_outbox o ON o.event_id = coalesce(i.consume_event,i.reserve_event)
            WHERE i.account_id = {account} AND i.order_id = {order}
            """).SingleOrDefaultAsync(ct);

    private sealed record SourceRow(string Snapshot, long Revision);
}
