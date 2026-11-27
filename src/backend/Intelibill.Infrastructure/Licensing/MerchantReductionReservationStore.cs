using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class MerchantReductionReservationStore(
    ApplicationDbContext db, EntitlementProjectionStore projection, ShopLicenceFence fence,
    TimeProvider transportClock, [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
    : IReductionReservationReceiver
{
    private static readonly string EmptyHash = Convert.ToHexStringLower(SHA256.HashData([]));

    public async Task<ErrorOr<ReductionObservationResponseV1>> ObserveAsync(
        ReductionObservationRequestV1 request, VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (request.ExpiresAt <= transportClock.GetUtcNow() || request.ExpiresAt > proof.ExpiresAt)
            return Error.Unauthorized("Reduction.Replayed");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.entitlement_account_id', {request.AccountId.ToString("D")}, true)", ct);
        var shopIds = request.Shops.Select(s => s.ShopId).ToArray();
        var entered = await fence.EnterObservationAsync(request.AccountId, shopIds, ct);
        if (entered.IsError) return entered.Errors;
        if (!await projection.ConsumeAsync(proof, ct)) return Error.Unauthorized("Reduction.Replayed");

        var rows = await db.Database.SqlQuery<ObservedRow>($"""
            SELECT shop_id, affiliation_id, active_members, seat_hash, has_pending_affiliation
            FROM public.reduction_observe_shops({request.AccountId}, {shopIds})
            """).ToDictionaryAsync(r => r.ShopId, ct);

        if (transportClock.GetUtcNow() >= proof.ExpiresAt || transportClock.GetUtcNow() >= request.ExpiresAt)
            return Error.Unauthorized("Reduction.Replayed");
        await tx.CommitAsync(ct);

        var items = request.Shops.Select(expected =>
        {
            var found = rows.TryGetValue(expected.ShopId, out var row);
            var matched = found && !row!.HasPendingAffiliation && row.AffiliationId == expected.AffiliationRequestId;
            return new ReductionObservedShopV1(expected.ShopId, found ? row!.AffiliationId ?? Guid.Empty : Guid.Empty,
                found ? row!.ActiveMembers : 0, found ? row!.SeatHash : EmptyHash, matched);
        }).ToArray();
        return new ReductionObservationResponseV1(1, request.AccountId, request.TermId, request.Challenge,
            request.PageIndex, request.PageCount, EntitlementSnapshotsV1.Instant(businessClock), items);
    }

    public async Task<ErrorOr<Success>> ReceiveManifestAsync(
        ReductionManifestPageV1 page, byte[] body, VerifiedServiceAssertion proof, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await projection.EnterAsync(page.AccountId, page.RoutingShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (!await projection.ConsumeAsync(proof, ct)) return Error.Unauthorized("Reduction.Replayed");

        var prior = await db.Database.SqlQuery<ManifestPrior>($"""
            SELECT event_id, digest FROM public.reduction_manifest_pages
            WHERE account_id = {page.AccountId} AND schedule_id = {page.ScheduleId}
                AND generation = {page.Generation} AND page_index = {page.PageIndex}
            """).SingleOrDefaultAsync(ct);
        if (prior is not null)
        {
            if (prior.EventId != page.EventId || prior.Digest != proof.Digest)
                return Error.Conflict("Reduction.CommandMismatch");
            if (transportClock.GetUtcNow() >= proof.ExpiresAt) return Error.Unauthorized("Reduction.Replayed");
            await tx.CommitAsync(ct);
            return Result.Success;
        }

        if (!await SaveManifestPageAsync(page, body, proof, ct))
            return Error.Conflict("Reduction.CommandMismatch");
        if (transportClock.GetUtcNow() >= proof.ExpiresAt) return Error.Unauthorized("Reduction.Replayed");
        await tx.CommitAsync(ct);
        return Result.Success;
    }

    public async Task<ErrorOr<Success>> ReceiveReservationAsync(
        ReductionReservationV1 c, byte[] body, VerifiedServiceAssertion proof, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entered = await projection.EnterAsync(c.AccountId, c.RoutingShopId, ct);
        if (entered.IsError) return entered.Errors;
        if (!await projection.ConsumeAsync(proof, ct)) return Error.Unauthorized("Reduction.Replayed");

        var prior = await PriorReceiptAsync(c, c.Phase, ct);
        if (prior is not null)
        {
            if (prior.Digest != proof.Digest || prior.EventId != c.EventId)
                return Error.Conflict("Reduction.CommandMismatch");
            await db.Database.ExecuteSqlAsync($"""
                UPDATE public.entitlement_outbox SET status = 'Pending', attempts = 0,
                    next_attempt_at = {transportClock.GetUtcNow()}, last_failure = NULL
                WHERE account_id = {c.AccountId} AND shop_id = {c.RoutingShopId}
                    AND event_id = {prior.EventId} AND status = 'DeadLetter'
                """, ct);
        }
        else
        {
            var decided = await DecideAsync(c, body, proof, ct);
            if (decided.IsError) return decided.Errors;
        }
        if (transportClock.GetUtcNow() >= proof.ExpiresAt) return Error.Unauthorized("Reduction.Replayed");
        await tx.CommitAsync(ct);
        return Result.Success;
    }

    private async Task<bool> SaveManifestPageAsync(ReductionManifestPageV1 p, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        var mismatch = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.reduction_manifest_pages
                WHERE account_id = {p.AccountId} AND schedule_id = {p.ScheduleId} AND generation = {p.Generation}
                    AND (order_id <> {p.OrderId} OR term_id <> {p.TermId} OR page_count <> {p.PageCount}
                        OR total_source_shops <> {p.TotalSourceShops} OR retained_shop_count <> {p.RetainedShopCount}
                        OR source_binding_digest <> {p.SourceBindingDigest} OR retained_binding_digest <> {p.RetainedBindingDigest})) AS "Value"
            """).SingleAsync(ct);
        if (mismatch) return false;

        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.reduction_manifest_pages
                (account_id, schedule_id, generation, page_index, page_count, event_id, order_id, term_id,
                 total_source_shops, retained_shop_count, source_binding_digest, retained_binding_digest,
                 expires_at, digest, payload, assertion, public_key)
            VALUES ({p.AccountId}, {p.ScheduleId}, {p.Generation}, {p.PageIndex}, {p.PageCount}, {p.EventId},
                {p.OrderId}, {p.TermId}, {p.TotalSourceShops}, {p.RetainedShopCount}, {p.SourceBindingDigest},
                {p.RetainedBindingDigest}, {p.ExpiresAt}, {proof.Digest}, {body}, {proof.Assertion}, {proof.PublicKey})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted != 1) return false;

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.reduction_manifest_shops
                (account_id, schedule_id, generation, page_index, shop_id, affiliation_request_id, retained,
                 expected_active_members, expected_seat_hash)
            SELECT {p.AccountId}, {p.ScheduleId}, {p.Generation}, {p.PageIndex}, shop_id, affiliation_id, retained,
                active_members, seat_hash
            FROM unnest(
                {p.Shops.Select(s => s.ShopId).ToArray()},
                {p.Shops.Select(s => s.AffiliationRequestId).ToArray()},
                {p.Shops.Select(s => s.Retained).ToArray()},
                {p.Shops.Select(s => s.ExpectedActiveMembers).ToArray()},
                {p.Shops.Select(s => s.ExpectedSeatHash).ToArray()})
                AS s(shop_id, affiliation_id, retained, active_members, seat_hash)
            """, ct);
        return true;
    }

    private async Task<ErrorOr<Success>> DecideAsync(ReductionReservationV1 c, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        var pages = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM public.reduction_manifest_pages
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND generation = {c.Generation}
            """).SingleAsync(ct);
        if (pages < c.PageCount) return LicenceErrors.Busy;

        var manifest = await db.Database.SqlQuery<ManifestShopRow>($"""
            SELECT shop_id, affiliation_request_id, retained, expected_active_members, expected_seat_hash
            FROM public.reduction_manifest_shops
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId} AND generation = {c.Generation}
            ORDER BY shop_id
            """).ToArrayAsync(ct);
        var live = await db.Database.SqlQuery<FenceShopRow>($"""
            SELECT shop_id, affiliation_id, state, starts_at, access_ends_at, capacity, members, active_members,
                seat_hash, suspension_mode, closed, account_restriction
            FROM public.reduction_fence_shops({c.AccountId}, {c.TermId})
            """).ToArrayAsync(ct);
        var sourceRevision = await db.Database.SqlQuery<long>($"""
            SELECT coalesce(max(revision), 0) AS "Value" FROM public.capacity_reservation_receipts
            WHERE account_id = {c.AccountId} AND term_id = {c.TermId} AND outcome = 'Applied'
            """).SingleAsync(ct);

        var fenceHash = ComputeSemanticFenceHash(live);
        var now = EntitlementSnapshotsV1.Instant(businessClock);
        var (outcome, reason, conflicts) = await EvaluateAsync(c, manifest, live, sourceRevision, fenceHash, now, ct);

        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.reduction_reservation_receipts
                (account_id, term_id, schedule_id, order_id, generation, event_id, phase, expires_at,
                 digest, fence_hash, outcome, reason, command, assertion, public_key)
            VALUES ({c.AccountId}, {c.TermId}, {c.ScheduleId}, {c.OrderId}, {c.Generation}, {c.EventId}, {c.Phase},
                {c.ExpiresAt}, {proof.Digest}, {fenceHash}, {outcome}, {reason}, {body}, {proof.Assertion}, {proof.PublicKey})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted != 1) return Error.Conflict("Reduction.CommandMismatch");

        if (outcome == "Applied")
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.applied_shop_reductions
                    (shop_id, account_id, predecessor_term_id, successor_term_id, affiliation_id, schedule_id,
                     generation, applied_event, members, starts_at, paid_through)
                SELECT shop_id, {c.AccountId}, {c.TermId}, {c.SuccessorTermId}, affiliation_request_id, {c.ScheduleId},
                    {c.Generation}, {c.EventId}, {c.TargetMembers}, {c.PriorPaidThrough}, {c.SuccessorEndsAt}
                FROM public.reduction_manifest_shops
                WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId}
                    AND generation = {c.Generation} AND retained
                """, ct);

        var ack = new ReductionReceiptV1(1, c.EventId, c.ScheduleId, c.OrderId, c.AccountId, c.TermId, c.RoutingShopId,
            c.Phase, c.Generation, proof.Digest, outcome, reason, conflicts);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ack, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(c.EventId, c.AccountId, c.RoutingShopId,
            ReductionTransportV1.ReceiptKind, bytes, SignedServiceAssertion.Digest(bytes),
            EntitlementSnapshotsV1.Instant(transportClock), ct);
        return Result.Success;
    }

    private async Task<(string Outcome, string? Reason, ReductionReceiptConflictV1[]? Conflicts)> EvaluateAsync(
        ReductionReservationV1 c, ManifestShopRow[] manifest, FenceShopRow[] live, long sourceRevision,
        string? fenceHash, DateTimeOffset now, CancellationToken ct)
    {
        if (now < c.PriorPaidThrough || now >= c.SuccessorEndsAt)
            return ("NeedsReview", "ReservationExpired", null);
        if (now >= c.ExpiresAt || businessClock.GetUtcNow() >= c.ExpiresAt)
            return ("NeedsResolution", "ReservationExpired", null);
        if (await AlreadyAppliedAsync(c, ct) || !SourceMatches(c, manifest, live, sourceRevision))
            return ("NeedsReview", "SourceChanged", null);

        var liveByShop = live.ToDictionary(r => r.ShopId);
        var conflicts = manifest.Where(m => m.Retained)
            .Select(m => (Shop: m, Live: liveByShop[m.ShopId]))
            .Where(pair => pair.Live.ActiveMembers <= 0 || pair.Live.ActiveMembers > c.TargetMembers)
            .Take(CapacityReductionsV1.MaxInlineConflicts)
            .Select(pair => new ReductionReceiptConflictV1(pair.Shop.ShopId,
                pair.Live.ActiveMembers > c.TargetMembers ? "MemberCapacityExceeded" : "NoActiveMembers",
                pair.Live.ActiveMembers, c.TargetMembers))
            .ToArray();
        if (conflicts.Length > 0)
            return ("NeedsResolution", "MemberCapacityExceeded", conflicts);

        if (c.Phase == ReductionTransportV1.ReserveKind)
            return await HasActiveConflictAsync(c, now, ct)
                ? ("NeedsResolution", "ReservationBusy", null)
                : ("Reserved", null, null);

        var reserved = await PriorReceiptAsync(c, ReductionTransportV1.ReserveKind, ct);
        if (reserved is not { Outcome: "Reserved" } || reserved.Digest != c.ReservationDigest ||
            JsonSerializer.Deserialize<ReductionReservationV1>(reserved.Command, EntitlementSnapshotsV1.Json) !=
                (c with { EventId = reserved.EventId, Phase = ReductionTransportV1.ReserveKind, ReservationDigest = null }))
            return ("NeedsReview", "ReservationChanged", null);
        return reserved.FenceHash == fenceHash
            ? ("Applied", null, null)
            : ("NeedsResolution", "ReservationChanged", null);
    }

    private static bool SourceMatches(ReductionReservationV1 c, ManifestShopRow[] manifest, FenceShopRow[] live, long sourceRevision)
    {
        var retained = manifest.Where(m => m.Retained).ToArray();
        if (manifest.Length != c.TotalSourceShops || retained.Length != c.RetainedShopCount ||
            live.Length != c.TotalSourceShops || sourceRevision != c.SourceRevision)
            return false;
        if (ReductionTransportV1.BindingDigest(manifest.Select(m => (m.ShopId, m.AffiliationRequestId))) != c.SourceBindingDigest ||
            ReductionTransportV1.BindingDigest(retained.Select(m => (m.ShopId, m.AffiliationRequestId))) != c.RetainedBindingDigest ||
            ReductionTransportV1.BindingDigest(live.Select(l => (l.ShopId, l.AffiliationId))) != c.SourceBindingDigest)
            return false;

        var liveSuspended = live.Any(r => r.State == "Suspended") || live[0].AccountRestriction != "Unrestricted";
        if (liveSuspended != c.SuspensionDisclosed) return false;

        foreach (var row in live)
        {
            var validState = row.State is "Active" or "Grace" or "Expired" ||
                (c.SuspensionDisclosed && row.State == "Suspended");
            if (!validState || row.Closed || row.StartsAt != c.PriorStartsAt || row.AccessEndsAt < c.PriorPaidThrough ||
                row.Capacity != c.PriorCapacity || row.Members != c.PriorMembers)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Semantic fence hash covering coverage bindings, effective allowances, restrictions and seat versions.
    /// Intentionally excludes raw projection revisions and state ticks so heartbeats or Active->Grace never drift.
    /// </summary>
    private static string? ComputeSemanticFenceHash(FenceShopRow[] live)
    {
        if (live.Length == 0) return null;
        var canonical = string.Join(',', live.OrderBy(r => r.ShopId).Select(r =>
            $"{r.ShopId:D}/{r.AffiliationId:D}/{CheckoutOrdersV1.Instant(r.StartsAt)}/{r.Capacity}/{r.Members}/{r.SuspensionMode ?? "-"}/{(r.Closed ? 1 : 0)}/{r.ActiveMembers}/{r.SeatHash}"))
            + "/" + live[0].AccountRestriction;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private Task<bool> AlreadyAppliedAsync(ReductionReservationV1 c, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.reduction_reservation_receipts
                WHERE account_id = {c.AccountId} AND term_id = {c.TermId} AND outcome = 'Applied') AS "Value"
            """).SingleAsync(ct);

    private Task<bool> HasActiveConflictAsync(ReductionReservationV1 c, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.reduction_reservation_receipts r
                WHERE r.account_id = {c.AccountId} AND r.term_id = {c.TermId}
                    AND r.phase = 'ReductionReserve' AND r.outcome = 'Reserved' AND r.expires_at > {now}
                    AND NOT (r.schedule_id = {c.ScheduleId} AND r.generation = {c.Generation})
                    AND NOT EXISTS (SELECT FROM public.reduction_reservation_receipts x
                        WHERE x.account_id = r.account_id AND x.schedule_id = r.schedule_id
                            AND x.generation = r.generation AND x.phase = 'ReductionConsume'))
                OR EXISTS (SELECT FROM public.capacity_reservation_receipts r
                    WHERE r.account_id = {c.AccountId} AND r.term_id = {c.TermId}
                        AND r.phase = 'CapacityReserve' AND r.outcome = 'Reserved' AND r.expires_at > {now}
                        AND NOT EXISTS (SELECT FROM public.capacity_reservation_receipts x
                            WHERE x.account_id = r.account_id AND x.order_id = r.order_id
                                AND x.phase = 'CapacityConsume')) AS "Value"
            """).SingleAsync(ct);

    private Task<PriorReceipt?> PriorReceiptAsync(ReductionReservationV1 c, string phase, CancellationToken ct) =>
        db.Database.SqlQuery<PriorReceipt>($"""
            SELECT event_id, digest, fence_hash, outcome, command FROM public.reduction_reservation_receipts
            WHERE account_id = {c.AccountId} AND schedule_id = {c.ScheduleId}
                AND generation = {c.Generation} AND phase = {phase}
            """).SingleOrDefaultAsync(ct);

    private sealed record ObservedRow(Guid ShopId, Guid? AffiliationId, int ActiveMembers, string SeatHash, bool HasPendingAffiliation);
    private sealed record ManifestPrior(Guid EventId, string Digest);
    private sealed record ManifestShopRow(Guid ShopId, Guid AffiliationRequestId, bool Retained, int ExpectedActiveMembers, string ExpectedSeatHash);
    private sealed record FenceShopRow(Guid ShopId, Guid AffiliationId, string State, DateTimeOffset StartsAt,
        DateTimeOffset AccessEndsAt, int Capacity, int Members, int ActiveMembers, string SeatHash,
        string? SuspensionMode, bool Closed, string AccountRestriction);
    private sealed record PriorReceipt(Guid EventId, string Digest, string? FenceHash, string Outcome, byte[] Command);
}
