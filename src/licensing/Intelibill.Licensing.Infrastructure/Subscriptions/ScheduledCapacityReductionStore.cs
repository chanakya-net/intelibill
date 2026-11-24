using System.Text.Json;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Subscriptions;

internal sealed partial class ScheduledCapacityReductionStore(LicensingDbContext db, ICapacityChangeQuoteStore quotes,
    TimeProvider clock) : IScheduledCapacityReductionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal DateTimeOffset TransportNow => EntitlementSnapshotsV1.Instant(clock);

    public async Task<StoredReductionPreview?> FindPreviewByOperationAsync(Guid account, Guid operationKey, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<PreviewRow>($"""
            SELECT snapshot::text AS snapshot, actor_id AS actor_id, request_hash AS request_hash
            FROM public.capacity_reduction_previews
            WHERE account_id = {account} AND operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null
            : new(JsonSerializer.Deserialize<CapacityReductionPreviewV1>(row.Snapshot, Json)!, row.ActorId, row.RequestHash);
    }

    public async Task<CapacityReductionPreviewV1?> FindPreviewAsync(Guid account, Guid term, Guid previewId, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT snapshot::text AS "Value" FROM public.capacity_reduction_previews
            WHERE account_id = {account} AND term_id = {term} AND preview_id = {previewId}
            """).SingleOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<CapacityReductionPreviewV1>(json, Json);
    }

    public async Task<IReadOnlyDictionary<Guid, ReductionObservedShopV1>> PreviewShopsMapAsync(
        Guid account, Guid previewId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ObservedShopRow>($"""
            SELECT shop_id, affiliation_request_id, active_members, seat_hash
            FROM public.capacity_reduction_preview_shops
            WHERE account_id = {account} AND preview_id = {previewId} ORDER BY shop_id
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.ShopId,
            r => new ReductionObservedShopV1(r.ShopId, r.AffiliationRequestId, r.ActiveMembers, r.SeatHash, true));
    }

    public async Task<CapacityReductionShopPageV1?> PreviewShopsPageAsync(
        Guid account, Guid term, Guid previewId, Guid? after, CancellationToken ct)
    {
        var allowance = await db.Database.SqlQuery<int?>($"""
            SELECT target_member_allowance AS "Value" FROM public.capacity_reduction_previews
            WHERE account_id = {account} AND term_id = {term} AND preview_id = {previewId}
            """).SingleOrDefaultAsync(ct);
        if (allowance is not { } limit) return null;
        var rows = await db.Database.SqlQuery<PreviewShopRow>($"""
            SELECT shop_id, shop_name AS name, affiliation_request_id, active_members, exceeds_allowance
            FROM public.capacity_reduction_preview_shops
            WHERE account_id = {account} AND preview_id = {previewId} AND ({after}::uuid IS NULL OR shop_id > {after})
            ORDER BY shop_id LIMIT {CapacityReductionsV1.PageSize + 1}
            """).ToListAsync(ct);
        var items = rows.Take(CapacityReductionsV1.PageSize).Select(r => new CapacityReductionShopV1(
            r.ShopId, r.Name, r.AffiliationRequestId, r.ActiveMembers, r.ExceedsAllowance,
            Math.Max(0, r.ActiveMembers - limit))).ToArray();
        return new(account, previewId, items, rows.Count > items.Length ? items[^1].ShopId : null);
    }

    public async Task SavePreviewAsync(CapacityReductionPreviewV1 p, CapacityChangeSource source,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, Guid actor, Guid key, string requestHash,
        Guid replay, CancellationToken ct)
    {
        var shops = source.Shops.OrderBy(s => s.ShopId).ToArray();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.capacity_reduction_preview_shops
                (account_id, preview_id, shop_id, shop_name, affiliation_request_id, active_members, seat_hash, exceeds_allowance, replay_id)
            SELECT {p.AccountId}, {p.PreviewId}, shop_id, shop_name, request_id, active_members, seat_hash, exceeds, {replay}
            FROM unnest(
                {shops.Select(s => s.ShopId).ToArray()},
                {shops.Select(s => s.Name).ToArray()},
                {shops.Select(s => s.RequestId).ToArray()},
                {shops.Select(s => observed[s.ShopId].ActiveMembers).ToArray()},
                {shops.Select(s => observed[s.ShopId].SeatHash).ToArray()},
                {shops.Select(s => observed[s.ShopId].ActiveMembers > p.TargetMemberAllowance).ToArray()})
                AS s(shop_id, shop_name, request_id, active_members, seat_hash, exceeds)
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.capacity_reduction_previews
                (account_id, preview_id, term_id, actor_id, operation_key, request_hash, replay_id,
                 source_plan_version_id, target_plan_version_id, prior_capacity, target_capacity,
                 prior_member_allowance, target_member_allowance, target_mode, source_shop_count, source_shop_hash,
                 source_hash, observation_digest, amount_paise, current_starts_at, current_paid_through, renewal_ends_at,
                 created_at, expires_at, snapshot_hash, snapshot)
            VALUES ({p.AccountId}, {p.PreviewId}, {p.TermId}, {actor}, {key}, {requestHash}, {replay},
                {p.SourcePlanVersionId}, {p.TargetPlanVersionId}, {p.PriorCapacity}, {p.TargetCapacity},
                {p.PriorMemberAllowance}, {p.TargetMemberAllowance}, {p.TargetMode}, {p.SourceShopCount}, {p.SourceShopHash},
                {p.SourceHash}, {p.ObservationDigest}, {p.AmountPaise}, {p.CurrentStartsAt}, {p.CurrentPaidThrough},
                {p.RenewalEndsAt}, {p.CreatedAt}, {p.ExpiresAt}, {p.SnapshotHash}, {JsonSerializer.Serialize(p, Json)}::jsonb)
            """, ct);
    }

    public async Task<StoredScheduledReduction?> FindScheduleByOperationAsync(Guid account, Guid operationKey, CancellationToken ct) =>
        ToStored(await Schedules(account).Where(r => r.OperationKey == operationKey).SingleOrDefaultAsync(ct));

    public async Task<StoredScheduledReduction?> FindActiveScheduleAsync(Guid account, Guid term, CancellationToken ct) =>
        ToStored(await Schedules(account).Where(r => r.TermId == term)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ScheduleId).FirstOrDefaultAsync(ct));

    public async Task<StoredScheduledReduction?> FindScheduleAsync(Guid account, Guid term, Guid scheduleId, CancellationToken ct) =>
        ToStored(await Schedules(account).Where(r => r.TermId == term && r.ScheduleId == scheduleId).SingleOrDefaultAsync(ct));

    public async Task<StoredScheduledReduction?> FindScheduleByOrderAsync(Guid account, Guid orderId, CancellationToken ct) =>
        ToStored(await Schedules(account).Where(r => r.OrderId == orderId).SingleOrDefaultAsync(ct));

    public Task<StoredReductionShop[]> ScheduleShopsAsync(Guid account, Guid scheduleId, CancellationToken ct) =>
        db.Database.SqlQuery<StoredReductionShop>($"""
            SELECT shop_id, shop_name AS name, affiliation_request_id, retained, active_members, seat_hash
            FROM public.scheduled_capacity_reduction_shops
            WHERE account_id = {account} AND schedule_id = {scheduleId} ORDER BY shop_id
            """).ToArrayAsync(ct);

    public async Task<CapacityReductionShopPageV1?> ScheduleShopsPageAsync(
        Guid account, Guid term, Guid scheduleId, Guid? after, CancellationToken ct)
    {
        var allowance = await db.Database.SqlQuery<int?>($"""
            SELECT target_member_allowance AS "Value" FROM public.scheduled_capacity_reductions
            WHERE account_id = {account} AND term_id = {term} AND schedule_id = {scheduleId}
            """).SingleOrDefaultAsync(ct);
        if (allowance is not { } limit) return null;
        var rows = await db.Database.SqlQuery<ScheduleShopRow>($"""
            SELECT shop_id, shop_name AS name, affiliation_request_id, retained, active_members
            FROM public.scheduled_capacity_reduction_shops
            WHERE account_id = {account} AND schedule_id = {scheduleId} AND ({after}::uuid IS NULL OR shop_id > {after})
            ORDER BY shop_id LIMIT {CapacityReductionsV1.PageSize + 1}
            """).ToListAsync(ct);
        var items = rows.Take(CapacityReductionsV1.PageSize).Select(r => new CapacityReductionShopV1(
            r.ShopId, r.Name, r.AffiliationRequestId, r.ActiveMembers, r.ActiveMembers > limit,
            Math.Max(0, r.ActiveMembers - limit), r.Retained)).ToArray();
        return new(account, scheduleId, items, rows.Count > items.Length ? items[^1].ShopId : null);
    }

    public async Task<bool> SaveScheduleAsync(ScheduledCapacityReductionV1 s, CapacityChangeSource source,
        IReadOnlyDictionary<Guid, ReductionObservedShopV1> observed, HashSet<Guid> retained,
        long expectedSourceRevision, Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct)
    {
        var shops = source.Shops.OrderBy(item => item.ShopId).ToArray();
        try
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.scheduled_capacity_reduction_shops
                    (account_id, schedule_id, shop_id, shop_name, affiliation_request_id, retained, active_members, seat_hash, replay_id)
                SELECT {s.AccountId}, {s.ScheduleId}, shop_id, shop_name, request_id, kept, active_members, seat_hash, {replay}
                FROM unnest(
                    {shops.Select(x => x.ShopId).ToArray()},
                    {shops.Select(x => x.Name).ToArray()},
                    {shops.Select(x => x.RequestId).ToArray()},
                    {shops.Select(x => retained.Contains(x.ShopId)).ToArray()},
                    {shops.Select(x => observed[x.ShopId].ActiveMembers).ToArray()},
                    {shops.Select(x => observed[x.ShopId].SeatHash).ToArray()})
                    AS x(shop_id, shop_name, request_id, kept, active_members, seat_hash)
                """, ct);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.scheduled_capacity_reductions
                    (account_id, schedule_id, preview_id, term_id, actor_id, operation_key, request_hash, replay_id,
                     state, target_plan_version_id, prior_capacity, target_capacity, prior_member_allowance,
                     target_member_allowance, target_mode, source_shop_count, source_shop_hash, retained_shop_count,
                     retained_shop_hash, source_hash, observation_digest, amount_paise, effective_at, renewal_ends_at,
                     expected_source_revision, created_at, snapshot_hash, snapshot)
                VALUES ({s.AccountId}, {s.ScheduleId}, {s.PreviewId}, {s.TermId}, {actor}, {key}, {requestHash}, {replay},
                    'Scheduled', {s.TargetPlanVersionId}, {s.PriorCapacity}, {s.TargetCapacity}, {s.PriorMemberAllowance},
                    {s.TargetMemberAllowance}, {s.TargetMode}, {s.SourceShopCount}, {s.SourceShopHash}, {s.RetainedShopCount},
                    {s.RetainedShopHash}, {s.SourceHash}, {s.ObservationDigest}, {s.AmountPaise}, {s.EffectiveAt},
                    {s.RenewalEndsAt}, {expectedSourceRevision}, {s.CreatedAt}, {s.SnapshotHash},
                    {JsonSerializer.Serialize(s, Json)}::jsonb)
                """, ct);
            return true;
        }
        catch (PostgresException error) when (error.ConstraintName == "single_active_scheduled_reduction")
        {
            return false;
        }
    }

    public Task<Guid?> FindReusableOrderAsync(Guid account, Guid scheduleId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Guid?>($"""
            SELECT q.order_id AS "Value" FROM public.checkout_orders q
            WHERE q.account_id = {account} AND q.snapshot->'reduction'->>'scheduleId' = {scheduleId.ToString("D")}
                AND (q.expires_at > {now}
                    OR EXISTS (SELECT FROM public.payment_attempts a WHERE a.account_id = q.account_id AND a.order_id = q.order_id
                        AND NOT EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = a.attempt_id AND e.kind = 'CheckoutFailed'))
                    OR EXISTS (SELECT FROM public.manual_payment_receipts r WHERE r.account_id = q.account_id AND r.order_id = q.order_id))
            ORDER BY q.created_at DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    public Task<bool> IsLiveOwnerAsync(Guid account, Guid actorId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.billing_account_members
                WHERE account_id = {account} AND merchant_user_id = {actorId} AND status = 1 AND role = 1) AS "Value"
            """).SingleAsync(ct);

    public Task<bool> IsReductionOwnerAsync(Guid account, Guid orderId, Guid actorId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.billing_account_members m ON m.account_id = q.account_id
                WHERE q.account_id = {account} AND q.order_id = {orderId} AND q.snapshot->'reduction' IS NOT NULL
                    AND m.merchant_user_id = {actorId} AND m.status = 1 AND m.role = 1) AS "Value"
            """).SingleAsync(ct);

    public async Task<bool> ReductionOrderCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct)
    {
        if (quote.Reduction is not { } red) return true;
        if (quote.TermId is not { } term ||
            await quotes.SourceAsync(quote.AccountId, term, now, ct) is not { CompleteEvidence: true, Superseded: false, Closed: false } live ||
            live.CancellationRevision % 2 != 0)
            return false;
        var hash = live.Hash();
        return await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.scheduled_capacity_reductions s
                WHERE s.account_id = {quote.AccountId} AND s.schedule_id = {red.ScheduleId} AND s.term_id = {term}
                    AND s.state = 'Scheduled' AND s.snapshot_hash = {red.ScheduleHash} AND s.source_hash = {hash}
                    AND s.amount_paise = {quote.AmountPaise}
                    AND (s.snapshot->>'suspensionDisclosed')::bool = {live.Suspended}) AS "Value"
            """).SingleAsync(ct);
    }

    public async Task FundAsync(ReductionFunding f, Guid? attemptId, Guid? receiptId, DateTimeOffset fundedAt, CancellationToken ct)
    {
        var updated = await db.Database.ExecuteSqlAsync($"""
            UPDATE public.scheduled_capacity_reductions
            SET state = 'Funded', order_id = {f.OrderId}, attempt_id = {attemptId}, receipt_id = {receiptId},
                payment_effective_at = {f.EffectiveAt}, grace_days_included = {f.GraceDaysIncluded}, funded_at = {fundedAt}
            WHERE account_id = {f.AccountId} AND schedule_id = {f.ScheduleId} AND state = 'Scheduled' AND order_id IS NULL
            """, ct);
        if (updated != 1) throw new InvalidOperationException("Verified reduction order has no open schedule.");
    }

    public Task<string?> OrderStateAsync(Guid account, Guid orderId, CancellationToken ct) =>
        db.Database.SqlQuery<string>($"""
            SELECT CASE WHEN s.state = 'Applying' AND o.status = 'DeadLetter' THEN 'NeedsReview'
                ELSE s.state END AS "Value"
            FROM public.scheduled_capacity_reductions s
            LEFT JOIN public.entitlement_outbox o ON o.event_id = coalesce(s.consume_event, s.reserve_event)
            WHERE s.account_id = {account} AND s.order_id = {orderId}
            """).SingleOrDefaultAsync(ct);

    private IQueryable<ScheduleRow> Schedules(Guid account) => db.Database.SqlQuery<ScheduleRow>($"""
        SELECT schedule_id, term_id, actor_id, operation_key, request_hash, state, reason, conflicts::text AS conflicts,
            expected_source_revision, resolution_generation, order_id, attempt_id, receipt_id,
            payment_effective_at, grace_days_included, funded_at, successor_term_id, applied_at, created_at,
            snapshot::text AS snapshot
        FROM public.scheduled_capacity_reductions WHERE account_id = {account}
        """);

    private static StoredScheduledReduction? ToStored(ScheduleRow? row)
    {
        if (row is null) return null;
        var snap = JsonSerializer.Deserialize<ScheduledCapacityReductionV1>(row.Snapshot, Json)!;
        var conflicts = row.Conflicts is null ? null
            : JsonSerializer.Deserialize<ReductionConflictV1[]>(row.Conflicts, Json);
        var current = snap with
        {
            State = row.State, Reason = row.Reason, ResolutionGeneration = row.ResolutionGeneration,
            OrderId = row.OrderId, AppliedTermId = row.State == "Applied" ? row.SuccessorTermId : null,
            FundedAt = row.FundedAt?.ToUniversalTime(), AppliedAt = row.AppliedAt?.ToUniversalTime(),
            Conflicts = conflicts,
        };
        return new(current, row.ActorId, row.RequestHash, row.ExpectedSourceRevision, row.AttemptId, row.ReceiptId,
            row.PaymentEffectiveAt?.ToUniversalTime(), row.GraceDaysIncluded);
    }

    private sealed record PreviewRow(string Snapshot, Guid ActorId, string RequestHash);
    private sealed record ObservedShopRow(Guid ShopId, Guid AffiliationRequestId, int ActiveMembers, string SeatHash);
    private sealed record PreviewShopRow(Guid ShopId, string Name, Guid AffiliationRequestId, int ActiveMembers, bool ExceedsAllowance);
    private sealed record ScheduleShopRow(Guid ShopId, string Name, Guid AffiliationRequestId, bool Retained, int ActiveMembers);
    private sealed record ScheduleRow(Guid ScheduleId, Guid TermId, Guid ActorId, Guid OperationKey, string RequestHash,
        string State, string? Reason, string? Conflicts, long ExpectedSourceRevision, long ResolutionGeneration,
        Guid? OrderId, Guid? AttemptId, Guid? ReceiptId, DateTimeOffset? PaymentEffectiveAt, int? GraceDaysIncluded,
        DateTimeOffset? FundedAt, Guid? SuccessorTermId, DateTimeOffset? AppliedAt, DateTimeOffset CreatedAt, string Snapshot);
}
