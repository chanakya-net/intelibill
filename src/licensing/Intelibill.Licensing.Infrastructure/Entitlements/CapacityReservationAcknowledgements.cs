using System.Text.Json;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Subscriptions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed class CapacityReservationAcknowledgements(LicensingDbContext db, CapacityIncreaseStore increases,
    ICapacityChangeQuoteStore quotes, EntitlementPublisher publisher,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock)
{
    internal async Task<bool> ApplyAsync(CapacityReceiptV1 ack, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SourceCommand>($"""
            SELECT m.payload, m.digest, q.snapshot->>'sourceHash' AS source_hash,
                (q.snapshot->>'suspensionDisclosed')::bool AS suspended
            FROM public.capacity_increases i JOIN public.capacity_change_quotes q ON q.account_id=i.account_id AND q.quote_id=i.order_id
            JOIN public.entitlement_messages m ON m.account_id=i.account_id AND m.event_id=coalesce(i.consume_event,i.reserve_event)
            WHERE i.account_id={ack.AccountId} AND i.order_id={ack.OrderId} AND i.term_id={ack.TermId}
                AND i.target_revision={ack.TargetRevision} AND i.state='Pending' AND m.kind={ack.Phase}
                AND m.shop_id={ack.RoutingShopId} AND m.event_id={ack.EventId}
            """).SingleOrDefaultAsync(ct);
        if (row is null || row.Digest != ack.CommandDigest) return false;
        var c = JsonSerializer.Deserialize<CapacityReservationV1>(row.Payload, EntitlementSnapshotsV1.Json)!;
        var now = EntitlementSnapshotsV1.Instant(clock);
        var source = await quotes.SourceAsync(c.AccountId, c.TermId, now, ct);
        if (ack.Outcome == "NeedsReview")
            await SetDecisionAsync(c, "NeedsReview", ack.Reason, ct);
        else if (ack.Outcome == "Applied")
        {
            // The merchant's exact consume receipt is authoritative, even when acknowledgement arrives after expiry.
            await SetDecisionAsync(c, "Applied", null, ct);
            if (source is not null)
                foreach (var shop in source.Shops)
                    await publisher.PublishAsync(c.AccountId, shop.ShopId, ct);
        }
        else if (source is not { CompleteEvidence: true, Closed: false, Superseded: false } || source.Hash() != row.SourceHash ||
            source.Suspended != row.Suspended || now >= c.ExpiresAt)
            await SetDecisionAsync(c, "NeedsReview", now >= c.ExpiresAt ? "ReservationExpired" : "SourceChanged", ct);
        else
            await QueueConsumptionAsync(c, row.Digest, ct);
        return await db.Database.ExecuteSqlAsync($"""
            UPDATE public.entitlement_outbox SET status={(ack.Outcome == "NeedsReview" ? "NeedsReview" : "Applied")},
                lease_token=NULL,lease_expires_at=NULL,last_failure=NULL WHERE event_id={ack.EventId} AND account_id={ack.AccountId}
            """, ct) == 1;
    }

    private async Task QueueConsumptionAsync(CapacityReservationV1 c, string digest, CancellationToken ct)
    {
        var next = c with { Phase = CapacityReservationV1.Consume, EventId = Guid.NewGuid(), ReservationDigest = digest };
        await increases.AppendAsync(next, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.capacity_increases SET consume_event={next.EventId}
            WHERE account_id={c.AccountId} AND order_id={c.OrderId} AND consume_event IS NULL
            """, ct);
    }

    private Task<int> SetDecisionAsync(CapacityReservationV1 c, string state, string? reason, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE public.capacity_increases SET state={state},reason={reason}
                WHERE account_id={c.AccountId} AND order_id={c.OrderId} AND state='Pending'
            """, ct);

    private sealed record SourceCommand(byte[] Payload, string Digest, string SourceHash, bool Suspended);
}
