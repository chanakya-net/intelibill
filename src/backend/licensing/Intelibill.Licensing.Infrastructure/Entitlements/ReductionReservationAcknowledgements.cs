using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Subscriptions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

/// <summary>
/// Processes signed merchant receipts for renewal-boundary reduction reservations. On a matching consume receipt,
/// writes the reduced paid successor at the original anchor, links only retained shops, and publishes entitlements
/// to both retained and omitted source shops in one transaction.
/// </summary>
internal sealed class ReductionReservationAcknowledgements(
    LicensingDbContext db, ScheduledCapacityReductionStore reductions, ICapacityChangeQuoteStore quotes,
    EntitlementPublisher publisher, [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal async Task<bool> ApplyAsync(ReductionReceiptV1 ack, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SourceRow>($"""
            SELECT m.payload, m.digest, s.source_hash, (s.snapshot->>'suspensionDisclosed')::bool AS suspended,
                s.snapshot::text AS snapshot, s.attempt_id, s.receipt_id, s.payment_effective_at, s.grace_days_included,
                s.target_mode, s.target_capacity, s.target_member_allowance, s.amount_paise, s.target_plan_version_id
            FROM public.scheduled_capacity_reductions s
            JOIN public.entitlement_messages m
                ON m.account_id = s.account_id AND m.event_id = coalesce(s.consume_event, s.reserve_event)
            WHERE s.account_id = {ack.AccountId} AND s.schedule_id = {ack.ScheduleId} AND s.order_id = {ack.OrderId}
                AND s.term_id = {ack.TermId} AND s.resolution_generation = {ack.Generation} AND s.state = 'Applying'
                AND m.kind = {ack.Phase} AND m.shop_id = {ack.RoutingShopId} AND m.event_id = {ack.EventId}
            """).SingleOrDefaultAsync(ct);
        if (row is null || row.Digest != ack.CommandDigest) return false;

        var c = JsonSerializer.Deserialize<ReductionReservationV1>(row.Payload, EntitlementSnapshotsV1.Json)!;
        var now = EntitlementSnapshotsV1.Instant(clock);
        var source = await quotes.SourceAsync(c.AccountId, c.TermId, now, ct);

        if (ack.Outcome == "NeedsReview")
            await SetReviewAsync(c, ack.Reason ?? "SourceChanged", now, ct);
        else if (ack.Outcome == "NeedsResolution")
        {
            if (ack.Reason == "SourceChanged" || !SourceStillMatches(source, row, c))
                await SetReviewAsync(c, "SourceChanged", now, ct);
            else
                await SetResolutionAsync(c, ack.Reason ?? "MemberCapacityExceeded", ack.Conflicts, ct);
        }
        else if (ack.Outcome == "Applied")
        {
            // Authoritative merchant consume receipt, even if acknowledgement arrives after reserve expiry.
            await ApplySuccessorAsync(c, row, now, ct);
        }
        else if (now >= c.SuccessorEndsAt)
            await SetReviewAsync(c, "ReservationExpired", now, ct);
        else if (now >= c.ExpiresAt)
            await SetResolutionAsync(c, "ReservationExpired", null, ct);
        else if (!SourceStillMatches(source, row, c))
            await SetReviewAsync(c, "SourceChanged", now, ct);
        else
            await QueueConsumeAsync(c, row.Digest, ct);

        var outboxStatus = ack.Outcome is "NeedsReview" or "NeedsResolution" ? "NeedsReview" : "Applied";
        return await db.Database.ExecuteSqlAsync($"""
            UPDATE public.entitlement_outbox
            SET status = {outboxStatus}, lease_token = NULL, lease_expires_at = NULL, last_failure = NULL
            WHERE event_id = {ack.EventId} AND account_id = {ack.AccountId}
            """, ct) == 1;
    }

    private static bool SourceStillMatches(CapacityChangeSource? source, SourceRow row, ReductionReservationV1 c) =>
        source is { CompleteEvidence: true, Closed: false, Superseded: false, CancellationRevision: var rev } &&
        rev % 2 == 0 && source.Hash() == row.SourceHash && source.Suspended == row.Suspended &&
        source.EndsAt == c.PriorPaidThrough;

    private async Task QueueConsumeAsync(ReductionReservationV1 c, string digest, CancellationToken ct)
    {
        var next = c with
        {
            Phase = ReductionTransportV1.ConsumeKind, EventId = Guid.NewGuid(), ReservationDigest = digest,
        };
        await reductions.AppendMessageAsync(next.EventId, c.AccountId, c.RoutingShopId,
            ReductionTransportV1.ConsumeKind, next, reductions.TransportNow, ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions SET consume_event = {next.EventId}
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND consume_event IS NULL
            """, ct);
    }

    private async Task SetResolutionAsync(ReductionReservationV1 c, string reason,
        ReductionReceiptConflictV1[]? conflicts, CancellationToken ct)
    {
        string? conflictsJson = null;
        if (conflicts is { Length: > 0 })
        {
            var shops = await reductions.ScheduleShopsAsync(c.AccountId, c.ScheduleId, ct);
            var names = shops.ToDictionary(s => s.ShopId, s => s.Name);
            var named = conflicts.Take(CapacityReductionsV1.MaxInlineConflicts).Select(item =>
                new ReductionConflictV1(item.ShopId, names.GetValueOrDefault(item.ShopId, item.ShopId.ToString("D")),
                    item.Reason, item.ActiveMembers, item.TargetAllowance)).ToArray();
            conflictsJson = JsonSerializer.Serialize(named, Json);
        }
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions
            SET state = 'NeedsResolution', reason = {reason}, conflicts = {conflictsJson}::jsonb
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND state = 'Applying'
            """, ct);
    }

    private async Task SetReviewAsync(ReductionReservationV1 c, string reason, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions
            SET state = 'NeedsReview', reason = {reason}, conflicts = NULL
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND state = 'Applying'
            """, ct);
        await ScheduledCapacityReductionStore.RecordReviewRowAsync(db, c.AccountId, c.ScheduleId, reason, now, ct);
    }

    private async Task ApplySuccessorAsync(ReductionReservationV1 c, SourceRow row, DateTimeOffset now, CancellationToken ct)
    {
        var snap = JsonSerializer.Deserialize<ScheduledCapacityReductionV1>(row.Snapshot, Json)!;
        var updated = await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions
            SET state = 'Applied', reason = NULL, conflicts = NULL, applied_at = {now}
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND state = 'Applying'
            """, ct);
        if (updated != 1) throw new InvalidOperationException("Reduction schedule could not transition to Applied.");

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_fulfilments
                (account_id, order_id, attempt_id, manual_receipt_id, term_id, policy_version, fulfilled_at,
                 predecessor_term_id, payment_effective_at, grace_days_included, renewal_policy_version)
            VALUES ({c.AccountId}, {c.OrderId}, {row.AttemptId}, {row.ReceiptId}, {c.SuccessorTermId},
                {PaymentFulfilmentPolicy.Version}, {now}, {c.TermId}, {row.PaymentEffectiveAt},
                {row.GraceDaysIncluded}, {RenewalPolicy.Version})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ({c.SuccessorTermId}, {c.AccountId}, {row.TargetMode}, {row.TargetCapacity},
                {c.PriorPaidThrough}, {c.SuccessorEndsAt})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.subscription_term_facts
                (term_id, account_id, basis, billing_period, anchor_local, time_zone, period_ordinal,
                 calendar_version, access_policy_version, member_allowance, price_paise, currency,
                 plan_version_id, source_reference, development_fixture, recorded_at)
            VALUES ({c.SuccessorTermId}, {c.AccountId}, 'Paid', {snap.Period}, {snap.AnchorLocal}::timestamp,
                {BillingCalendar.Zone}, {snap.Ordinal}, {BillingCalendar.Version}, {GracePolicy.Version},
                {row.TargetMemberAllowance}, {row.AmountPaise}, 'INR', {row.TargetPlanVersionId},
                {PaymentFulfilmentPolicy.Version}, {snap.DevelopmentFixture}, {now})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_fulfilment_shops (account_id, order_id, term_id, shop_id, affiliation_request_id)
            SELECT account_id, {c.OrderId}, {c.SuccessorTermId}, shop_id, affiliation_request_id
            FROM public.scheduled_capacity_reduction_shops
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND retained
            """, ct);
        if (row.AttemptId is { } attemptId)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.payment_events (event_id, account_id, attempt_id, order_id, kind, recorded_at)
                VALUES ({Guid.NewGuid()}, {c.AccountId}, {attemptId}, {c.OrderId}, {PaymentEventKinds.OrderFulfilled}, {now})
                """, ct);
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", ct);

        var allShops = await reductions.ScheduleShopsAsync(c.AccountId, c.ScheduleId, ct);
        foreach (var shop in allShops.OrderBy(s => s.ShopId))
            await publisher.PublishAsync(c.AccountId, shop.ShopId, ct, evaluatedAt: now);

        var retainedIds = allShops.Where(s => s.Retained).Select(s => s.ShopId).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_fulfilment_deliveries (account_id, order_id, shop_id, shop_revision)
            SELECT account_id, {c.OrderId}, shop_id, revision FROM public.entitlement_shop_heads
            WHERE account_id = {c.AccountId} AND shop_id = ANY({retainedIds})
            """, ct);
    }

    private sealed record SourceRow(byte[] Payload, string Digest, string SourceHash, bool Suspended, string Snapshot,
        Guid? AttemptId, Guid? ReceiptId, DateTimeOffset? PaymentEffectiveAt, int? GraceDaysIncluded,
        string TargetMode, int TargetCapacity, int TargetMemberAllowance, long AmountPaise, Guid TargetPlanVersionId);
}
