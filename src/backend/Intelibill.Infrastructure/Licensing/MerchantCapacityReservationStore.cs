using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class MerchantCapacityReservationStore(ApplicationDbContext db, EntitlementProjectionStore projection,
    TimeProvider transportClock, [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
    : ICapacityReservationReceiver
{
    public async Task<ErrorOr<Success>> ReceiveAsync(CapacityReservationV1 c, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await projection.EnterAsync(c.AccountId, c.RoutingShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (!await projection.ConsumeAsync(proof, ct)) return Error.Unauthorized("Capacity.Replayed");
        var prior = await PriorAsync(c, c.Phase, ct);
        if (prior is not null && (prior.Digest != proof.Digest || prior.EventId != c.EventId))
            return Error.Conflict("Capacity.CommandMismatch");
        if (prior is null)
        {
            if (!await DecideAsync(c, body, proof, ct)) return Error.Conflict("Capacity.CommandMismatch");
        }
        else
            await db.Database.ExecuteSqlAsync($"""
                UPDATE public.entitlement_outbox SET status='Pending',attempts=0,next_attempt_at={transportClock.GetUtcNow()},
                    last_failure=NULL WHERE account_id={c.AccountId} AND shop_id={c.RoutingShopId} AND event_id={prior.EventId}
                    AND status='DeadLetter'
                """, ct);
        if (transportClock.GetUtcNow() >= proof.ExpiresAt) return Error.Unauthorized("Capacity.Replayed");
        await tx.CommitAsync(ct);
        return Result.Success;
    }

    private async Task<bool> DecideAsync(CapacityReservationV1 c, byte[] body, VerifiedServiceAssertion proof, CancellationToken ct)
    {
        var facts = await ReadFenceAsync(c, ct);
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var reason = await RefusalAsync(c, facts, now, ct);
        if (businessClock.GetUtcNow() >= c.ExpiresAt) reason = "ReservationExpired";
        var outcome = reason is not null ? "NeedsReview" : c.Phase == CapacityReservationV1.Reserve ? "Reserved" : "Applied";
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.capacity_reservation_receipts
                (account_id,term_id,order_id,event_id,phase,revision,expires_at,digest,fence_hash,outcome,command,assertion,public_key)
            VALUES ({c.AccountId},{c.TermId},{c.OrderId},{c.EventId},{c.Phase},{c.TargetRevision},{c.ExpiresAt},
                {proof.Digest},{facts.FenceHash},{outcome},{body},{proof.Assertion},{proof.PublicKey})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted != 1) return false;
        if (outcome == "Applied")
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.applied_shop_capacity(shop_id,account_id,term_id,affiliation_id,revision,applied_event,members,paid_through)
                SELECT shop_id,{c.AccountId},{c.TermId},affiliation_id,{c.TargetRevision},{c.EventId},{c.Members},{c.PaidThrough}
                FROM public.capacity_fence_shops({c.AccountId},{c.TermId})
                """, ct);
        var ack = new CapacityReceiptV1(1, c.EventId, c.OrderId, c.AccountId, c.TermId, c.RoutingShopId,
            c.Phase, c.TargetRevision, proof.Digest, outcome, reason);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ack, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(c.EventId, c.AccountId, c.RoutingShopId, CapacityReservationV1.Receipt,
            bytes, SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(transportClock), ct);
        return true;
    }

    private async Task<string?> RefusalAsync(CapacityReservationV1 c, FenceFacts f, DateTimeOffset now, CancellationToken ct)
    {
        if (now < c.StartsAt || now >= c.ExpiresAt) return "ReservationExpired";
        if (!f.Coherent || f.BindingHash != c.BindingHash || f.Shops != c.ShopCount || f.Revision != c.SourceRevision)
            return "SourceChanged";
        if (c.Phase == CapacityReservationV1.Consume)
        {
            var reserved = await PriorAsync(c, CapacityReservationV1.Reserve, ct);
            return reserved is { Outcome: "Reserved" } && reserved.Digest == c.ReservationDigest &&
                reserved.FenceHash == f.FenceHash && JsonSerializer.Deserialize<CapacityReservationV1>(reserved.Command,
                    EntitlementSnapshotsV1.Json) == (c with { EventId = reserved.EventId, Phase = CapacityReservationV1.Reserve,
                        ReservationDigest = null }) ? null : "ReservationChanged";
        }
        var busy = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.capacity_reservation_receipts r WHERE account_id={c.AccountId} AND term_id={c.TermId}
                AND phase='CapacityReserve' AND outcome='Reserved' AND expires_at>{now} AND NOT EXISTS
                (SELECT FROM public.capacity_reservation_receipts x WHERE x.order_id=r.order_id AND x.account_id=r.account_id
                    AND x.phase='CapacityConsume')) AS "Value"
            """).SingleAsync(ct);
        return busy ? "ReservationBusy" : null;
    }

    private Task<Prior?> PriorAsync(CapacityReservationV1 c, string phase, CancellationToken ct) =>
        db.Database.SqlQuery<Prior>($"""
            SELECT event_id, digest, fence_hash, outcome, command FROM public.capacity_reservation_receipts
            WHERE account_id={c.AccountId} AND term_id={c.TermId} AND order_id={c.OrderId} AND phase={phase}
            """).SingleOrDefaultAsync(ct);

    private Task<FenceFacts> ReadFenceAsync(CapacityReservationV1 c, CancellationToken ct) =>
        db.Database.SqlQuery<FenceFacts>($"""
            SELECT count(*)::int AS shops, encode(sha256(convert_to(string_agg(shop_id::text||'/'||affiliation_id::text,','
                ORDER BY shop_id),'UTF8')),'hex') AS binding_hash,
                encode(sha256(convert_to(string_agg(shop_revision::text||'/'||seat_hash,',' ORDER BY shop_id) ||
                    '/' || (SELECT revision FROM public.account_license_restrictions WHERE account_id={c.AccountId})::text,
                    'UTF8')),'hex') AS fence_hash,
                coalesce(bool_and(state='Active' AND starts_at={c.StartsAt} AND next_boundary={c.PaidThrough} AND access_ends_at>={c.PaidThrough}
                    AND capacity={c.PriorCapacity} AND members={c.PriorMembers} AND active_members<=members
                    AND active_members>0),false) AS coherent,
                coalesce((SELECT max(revision) FROM public.capacity_reservation_receipts WHERE account_id={c.AccountId}
                    AND term_id={c.TermId} AND outcome='Applied'),0) AS revision
            FROM public.capacity_fence_shops({c.AccountId},{c.TermId})
            """).SingleAsync(ct);

    private sealed record FenceFacts(int Shops, string? BindingHash, string? FenceHash, bool Coherent, long Revision);
    private sealed record Prior(Guid EventId, string Digest, string? FenceHash, string Outcome, byte[] Command);
}
