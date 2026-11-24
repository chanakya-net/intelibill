using System.Text.Json;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Subscriptions;

internal sealed partial class ScheduledCapacityReductionStore
{
    public Task<StoredReductionResolution?> FindResolutionByOperationAsync(Guid account, Guid operationKey, CancellationToken ct) =>
        db.Database.SqlQuery<StoredReductionResolution>($"""
            SELECT schedule_id, actor_id, request_hash, generation
            FROM public.scheduled_capacity_reduction_resolutions
            WHERE account_id = {account} AND operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);

    public async Task<bool> ResolveAndQueueAsync(ScheduledCapacityReductionV1 s, StoredReductionShop[] shops,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, Guid actor, Guid key, string requestHash,
        Guid replay, DateTimeOffset now, CancellationToken ct)
    {
        var nextGen = s.ResolutionGeneration + 1;
        var digest = ReductionTransportV1.ObservationDigest(shops.Select(shop =>
            (shop.ShopId, shop.AffiliationRequestId, observed[shop.ShopId].ActiveMembers, observed[shop.ShopId].SeatHash)));
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.scheduled_capacity_reduction_resolutions
                (account_id, resolution_id, schedule_id, actor_id, operation_key, request_hash, replay_id,
                 generation, observation_digest, recorded_at)
            VALUES ({s.AccountId}, {Guid.NewGuid()}, {s.ScheduleId}, {actor}, {key}, {requestHash}, {replay},
                {nextGen}, {digest}, {now})
            """, ct);
        var stored = await FindScheduleAsync(s.AccountId, s.TermId, s.ScheduleId, ct);
        if (stored is null || stored.Schedule.OrderId is null) return false;
        var expectedShops = shops.Select(shop => new ReductionManifestShopV1(
            shop.ShopId, shop.AffiliationRequestId, shop.Retained,
            observed[shop.ShopId].ActiveMembers, observed[shop.ShopId].SeatHash)).ToArray();
        return await QueueAttemptAsync(stored, expectedShops, nextGen, now, fromState: "NeedsResolution", ct);
    }

    internal async Task<bool> QueueDueAttemptAsync(Guid account, Guid scheduleId, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.reduction_due_scan', '1', true)", ct);
        var stored = ToStored(await Schedules(account).Where(r => r.ScheduleId == scheduleId).SingleOrDefaultAsync(ct));
        if (stored is null || stored.Schedule.State != "Funded" || stored.Schedule.EffectiveAt > now || stored.Schedule.OrderId is null)
            return false;
        var s = stored.Schedule;
        var source = await quotes.SourceAsync(account, s.TermId, now, ct);
        if (source is not { CompleteEvidence: true, Superseded: false, Closed: false, CancellationRevision: var rev } ||
            rev % 2 != 0 || source.Hash() != s.SourceHash || source.Suspended != s.SuspensionDisclosed ||
            now >= s.RenewalEndsAt)
        {
            await MarkDueReviewAsync(stored, "SourceChanged", now, ct);
            return true;
        }
        var shops = await ScheduleShopsAsync(account, scheduleId, ct);
        var manifestShops = shops.Select(shop => new ReductionManifestShopV1(
            shop.ShopId, shop.AffiliationRequestId, shop.Retained, shop.ActiveMembers, shop.SeatHash)).ToArray();
        return await QueueAttemptAsync(stored, manifestShops, s.ResolutionGeneration + 1, now, fromState: "Funded", ct);
    }

    internal Task AppendMessageAsync<T>(Guid eventId, Guid account, Guid routingShop, string kind, T payload,
        DateTimeOffset nextAttemptAt, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EntitlementSnapshotsV1.Json);
        return new LeasedHttpOutbox(db).AppendAsync(eventId, account, routingShop, kind, bytes,
            SignedServiceAssertion.Digest(bytes), nextAttemptAt, ct);
    }

    private async Task<bool> QueueAttemptAsync(StoredScheduledReduction stored, ReductionManifestShopV1[] shops,
        long generation, DateTimeOffset now, string fromState, CancellationToken ct)
    {
        var s = stored.Schedule;
        var ordered = shops.OrderBy(x => x.ShopId).ToArray();
        var retained = ordered.Where(x => x.Retained).ToArray();
        if (retained.Length == 0 || s.OrderId is not { } orderId) return false;
        var routingShop = retained[0].ShopId;
        var sourceDigest = ReductionTransportV1.BindingDigest(ordered.Select(x => (x.ShopId, x.AffiliationRequestId)));
        var retainedDigest = ReductionTransportV1.BindingDigest(retained.Select(x => (x.ShopId, x.AffiliationRequestId)));
        var expiresAt = now.AddMinutes(5) < s.RenewalEndsAt ? now.AddMinutes(5) : s.RenewalEndsAt;
        var pageCount = (ordered.Length + ReductionTransportV1.ManifestPageSize - 1) / ReductionTransportV1.ManifestPageSize;
        var deliveryNow = EntitlementSnapshotsV1.Instant(clock);

        for (var i = 0; i < pageCount; i++)
        {
            var slice = ordered.Skip(i * ReductionTransportV1.ManifestPageSize)
                .Take(ReductionTransportV1.ManifestPageSize).ToArray();
            var page = new ReductionManifestPageV1(1, Guid.NewGuid(), s.ScheduleId, orderId, s.AccountId, s.TermId,
                routingShop, generation, i, pageCount, ordered.Length, retained.Length, sourceDigest, retainedDigest,
                expiresAt, slice);
            await AppendMessageAsync(page.EventId, s.AccountId, routingShop, ReductionTransportV1.ManifestKind,
                page, deliveryNow.AddTicks(-10L * (pageCount - i)), ct);
        }

        var reserveId = Guid.NewGuid();
        var successorTermId = Guid.NewGuid();
        var reserve = new ReductionReservationV1(1, reserveId, ReductionTransportV1.ReserveKind, s.ScheduleId, orderId,
            s.AccountId, s.TermId, routingShop, generation, pageCount, ordered.Length, retained.Length, sourceDigest,
            retainedDigest, s.SourceHash, s.PriorCapacity, s.PriorMemberAllowance, s.TargetCapacity,
            s.TargetMemberAllowance, s.CurrentStartsAt, s.CurrentPaidThrough, s.RenewalEndsAt, expiresAt,
            stored.ExpectedSourceRevision, successorTermId, s.SuspensionDisclosed, null);
        // Manifest pages are stamped slightly earlier so outbox claims deliver manifests before Reserve immediately.
        await AppendMessageAsync(reserveId, s.AccountId, routingShop, ReductionTransportV1.ReserveKind,
            reserve, deliveryNow, ct);

        return await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions
            SET state = 'Applying', reason = NULL, conflicts = NULL, resolution_generation = {generation},
                reserve_event = {reserveId}, consume_event = NULL, reserve_expires_at = {expiresAt},
                successor_term_id = {successorTermId}
            WHERE account_id = {s.AccountId} AND schedule_id = {s.ScheduleId} AND state = {fromState}
            """, ct) == 1;
    }

    private async Task MarkDueReviewAsync(StoredScheduledReduction stored, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var s = stored.Schedule;
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions
            SET state = 'NeedsReview', reason = {reason}, conflicts = NULL
            WHERE account_id = {s.AccountId} AND schedule_id = {s.ScheduleId} AND state = 'Funded'
            """, ct);
        await RecordReviewRowAsync(db, s.AccountId, s.ScheduleId, reason, now, ct);
    }

    internal static async Task RecordReviewRowAsync(LicensingDbContext db, Guid account, Guid scheduleId,
        string reason, DateTimeOffset now, CancellationToken ct)
    {
        var cases = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.unmatched_payments (case_id, account_id, order_id, classification, attempt_id, manual_receipt_id,
                expected_amount_paise, expected_currency, observed_amount_paise, observed_currency, provider_id,
                receiving_account_id, environment, configuration_version, provider_order_reference, provider_payment_reference,
                observed_at, captured_at)
            SELECT {Guid.NewGuid()}, s.account_id, s.order_id, {reason}, s.attempt_id, s.receipt_id,
                coalesce(v.amount_paise, r.amount_paise), coalesce(v.currency, r.currency),
                coalesce(v.amount_paise, r.amount_paise), coalesce(v.currency, r.currency),
                coalesce(v.provider_id, 'manual'), coalesce(v.provider_account_id, r.receiving_account_id),
                coalesce(v.environment, r.environment), coalesce(v.configuration_version, r.configuration_version),
                ps.provider_order_reference, v.provider_payment_reference, greatest(v.verified_at, v.captured_at, r.verified_at),
                CASE WHEN v.capture_time_basis = 'ProviderCapture' THEN v.captured_at ELSE r.received_at END
            FROM public.scheduled_capacity_reductions s
            LEFT JOIN public.verified_payments v USING (account_id, order_id, attempt_id)
            LEFT JOIN public.payment_attempt_sessions ps ON ps.account_id = s.account_id AND ps.attempt_id = s.attempt_id
            LEFT JOIN public.manual_payment_receipts r ON r.account_id = s.account_id AND r.receipt_id = s.receipt_id
            WHERE s.account_id = {account} AND s.schedule_id = {scheduleId} AND s.state = 'NeedsReview'
            ON CONFLICT DO NOTHING
            """, ct);
        if (cases == 1)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.payment_events (event_id, account_id, order_id, attempt_id, kind, recorded_at)
                SELECT {Guid.NewGuid()}, account_id, order_id, attempt_id, 'PaidNeedsReview', {now}
                FROM public.scheduled_capacity_reductions
                WHERE account_id = {account} AND schedule_id = {scheduleId} AND attempt_id IS NOT NULL
                """, ct);
    }
}
