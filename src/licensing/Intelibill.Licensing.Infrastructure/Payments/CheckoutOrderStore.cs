using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class CheckoutOrderStore(LicensingDbContext db, Application.Subscriptions.ICapacityChangeQuoteStore changes,
    Application.Changes.IScheduledCapacityReductionStore reductions) : ICheckoutOrderStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<bool> ReductionOrderCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct) =>
        reductions.ReductionOrderCurrentAsync(quote, now, ct);

    public Task<bool> IsReductionOwnerAsync(Guid account, Guid order, Guid actor, CancellationToken ct) =>
        reductions.IsReductionOwnerAsync(account, order, actor, ct);

    public Task<bool> HasConflictingPaidCoverageAsync(Guid account, Guid[] shops, Guid? excludeTerm, DateTimeOffset startsAt,
        DateTimeOffset endsAt, CancellationToken ct) =>
        shops.Length == 0 ? Task.FromResult(false) : db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM unnest({shops}) AS s(shop_id)
                WHERE public.shop_has_conflicting_paid_coverage({account}, s.shop_id, {excludeTerm}::uuid, {startsAt}, {endsAt})
            ) AS "Value"
            """).SingleAsync(ct);

    public async Task<bool> IncreaseStillCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct)
    {
        if (quote.Increase is null) return true;
        if (quote.TermId is not { } term || await changes.SourceAsync(quote.AccountId, term, now, ct) is not
            { CompleteEvidence: true, Superseded: false, Closed: false } live) return false;
        var hash = live.Hash();
        return await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.capacity_change_quotes WHERE account_id={quote.AccountId}
                AND quote_id={quote.OrderId} AND term_id={term} AND source_hash={hash}
                AND snapshot_hash={quote.Increase.QuoteHash} AND (snapshot->>'suspensionDisclosed')::bool={live.Suspended}) AS "Value"
            """).SingleAsync(ct);
    }

    public Task<bool> IsIncreaseOwnerAsync(Guid account, Guid order, Guid actor, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.capacity_change_quotes q JOIN public.billing_account_members m USING (account_id)
                WHERE q.account_id = {account} AND q.quote_id = {order} AND q.actor_id = {actor}
                    AND m.merchant_user_id = {actor} AND m.status = 1 AND m.role = 1) AS "Value"
            """).SingleAsync(ct);

    public async Task<StoredCheckoutQuote?> FindAsync(Guid account, Guid id, bool byOperation, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<QuoteRow>($"""
            SELECT q.snapshot::text AS snapshot, q.actor_id AS actor_id, q.request_hash AS request_hash,
                   a.operation_key AS accept_key, a.accepted_at AS accepted_at
            FROM public.checkout_orders q
            LEFT JOIN public.checkout_order_acceptances a USING (account_id, order_id)
            WHERE q.account_id = {account} AND
                (({byOperation} AND q.operation_key = {id}) OR (NOT {byOperation} AND q.order_id = {id}))
            """).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;
        var quote = JsonSerializer.Deserialize<CheckoutQuoteV1>(row.Snapshot, Json)!;
        if (row.AcceptKey is { } key && row.AcceptedAt is { } acceptedAt)
            quote = quote with { Acceptance = new(key, quote.Revision, quote.SnapshotHash, acceptedAt) };
        return new(quote, row.ActorId, row.RequestHash);
    }

    public async Task<CheckoutSource?> SourceAsync(Guid account, Guid? term, Guid[] requested, CancellationToken ct)
    {
        if (term is { } id)
        {
            var prior = await ReadTermAsync(account, id, ct);
            var shops = await ReadTermShopsAsync(account, id, ct);
            return prior is not null && shops.Length is > 0 and <= CheckoutOrdersV1.MaxShops
                ? new(shops, prior) : null;
        }

        var selected = await ReadSourceShopsAsync(account, requested, ct);
        return selected.Length == requested.Length && selected.All(shop => shop.TermId is null)
            ? new(selected, null, await ReadTrialSourcesAsync(account, requested, ct)) : null;
    }

    private Task<CheckoutTrialSource[]> ReadTrialSourcesAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        db.Database.SqlQuery<CheckoutTrialSource>($"""
            SELECT h.shop_id, h.term_id, coalesce(e.revision, 0) AS revision, coalesce(e.ends_at, h.ends_at) AS ends_at
            FROM public.trial_history h
            LEFT JOIN LATERAL (SELECT revision, ends_at FROM public.trial_extensions e
                WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY revision DESC LIMIT 1) e ON true
            WHERE h.account_id = {account} AND h.shop_id = ANY({shops}) ORDER BY h.shop_id
            """).ToArrayAsync(ct);

    // A shop's paid coverage is its latest paid link, else the term its affiliation assigned.
    private async Task<CheckoutSourceShop[]> ReadSourceShopsAsync(Guid account, Guid[] requested, CancellationToken ct) =>
        await db.Database.SqlQuery<CheckoutSourceShop>($"""
            SELECT a.shop_id AS shop_id, a.shop_name AS name, a.request_id AS request_id,
                   coalesce((SELECT p.term_id FROM public.payment_fulfilment_shops p
                       JOIN public.subscription_coverage_terms pt ON pt.account_id = p.account_id AND pt.term_id = p.term_id
                       WHERE p.account_id = a.account_id AND p.shop_id = a.shop_id ORDER BY pt.ends_at DESC LIMIT 1), a.term_id) AS term_id,
                   a.applied_at AS applied_at, a.coverage_ends_at AS coverage_ends_at
            FROM public.shop_affiliations a
            WHERE a.account_id = {account} AND a.shop_id = ANY({requested})
            ORDER BY a.shop_id LIMIT {CheckoutOrdersV1.MaxShops + 1}
            """).ToArrayAsync(ct);

    // Affiliation-assigned shops of an imported term (V1 digest bytes unchanged), or the paid links of a term the
    // payment pipeline issued, whose coverage ends with that term.
    private async Task<CheckoutSourceShop[]> ReadTermShopsAsync(Guid account, Guid term, CancellationToken ct) =>
        await db.Database.SqlQuery<CheckoutSourceShop>($"""
            SELECT shop_id, name, request_id, term_id, applied_at, coverage_ends_at FROM (
                SELECT a.shop_id AS shop_id, a.shop_name AS name, a.request_id AS request_id, a.term_id AS term_id,
                       a.applied_at AS applied_at, a.coverage_ends_at AS coverage_ends_at
                FROM public.shop_affiliations a WHERE a.account_id = {account} AND a.term_id = {term}
                UNION ALL
                SELECT a.shop_id, a.shop_name, a.request_id, p.term_id, a.applied_at, t.ends_at
                FROM public.payment_fulfilment_shops p
                JOIN public.shop_affiliations a ON a.account_id = p.account_id AND a.shop_id = p.shop_id
                    AND a.request_id = p.affiliation_request_id
                JOIN public.subscription_coverage_terms t ON t.account_id = p.account_id AND t.term_id = p.term_id
                WHERE p.account_id = {account} AND p.term_id = {term}
            ) covered ORDER BY shop_id LIMIT {CheckoutOrdersV1.MaxShops + 1}
            """).ToArrayAsync(ct);

    // Only the latest paid period is a renewal source: a term another fulfilment already extended is stale.
    private Task<CheckoutRenewalSource?> ReadTermAsync(Guid account, Guid term, CancellationToken ct) =>
        db.Database.SqlQuery<CheckoutRenewalSource>($"""
            SELECT t.starts_at AS starts_at, t.ends_at AS ends_at, coalesce(i.capacity,t.capacity) AS capacity,
                   rd.new_access_ends_at AS refund_access_ends_at, coalesce(rd.term_revision,0) AS refund_revision,
                   f.basis AS basis, f.billing_period AS period, f.anchor_local AS anchor_local,
                   f.period_ordinal AS ordinal, f.calendar_version AS calendar_version,
                   f.access_policy_version AS access_policy_version, coalesce((
                       SELECT max(c.revision) FROM public.scheduled_subscription_changes c
                       WHERE c.account_id = t.account_id AND c.term_id = t.term_id), 0) AS cancellation_revision,
                   coalesce(i.target_revision,0) AS capacity_revision,
                   CASE WHEN i.capacity>1 THEN 'Grouped' ELSE t.mode END AS mode, EXISTS (SELECT FROM public.payment_fulfilments r WHERE r.account_id = t.account_id
                       AND r.term_id = t.term_id AND (r.predecessor_term_id IS NOT NULL OR EXISTS (
                           SELECT FROM public.paid_fresh_starts x WHERE x.account_id = r.account_id AND x.order_id = r.order_id))) AS renewed,
                   (SELECT sr.schedule_id FROM public.scheduled_capacity_reductions sr
                       WHERE sr.account_id = t.account_id AND sr.term_id = t.term_id
                           AND sr.state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview') LIMIT 1) AS active_reduction_id,
                   EXISTS (SELECT FROM public.scheduled_capacity_reductions sr
                       WHERE sr.account_id = t.account_id AND sr.term_id = t.term_id
                           AND (sr.state IN ('Funded','Applying','NeedsResolution')
                               OR (sr.state = 'NeedsReview' AND NOT public.reduction_review_settled(sr.account_id, sr.schedule_id))
                               OR (sr.state = 'Scheduled' AND EXISTS (
                                   SELECT FROM public.checkout_orders q
                                   WHERE q.account_id = sr.account_id AND q.term_id = sr.term_id
                                       AND q.snapshot->'reduction'->>'scheduleId' = sr.schedule_id::text
                                       AND (EXISTS (SELECT FROM public.payment_attempts a
                                               WHERE a.account_id = q.account_id AND a.order_id = q.order_id
                                                   AND (NOT EXISTS (SELECT FROM public.payment_events e
                                                           WHERE e.attempt_id = a.attempt_id AND e.kind = 'CheckoutFailed')
                                                       OR EXISTS (SELECT FROM public.verified_payments v
                                                           WHERE v.attempt_id = a.attempt_id)))
                                           OR EXISTS (SELECT FROM public.manual_payment_receipts r
                                               WHERE r.account_id = q.account_id AND r.order_id = q.order_id)))))) AS blocks_restored_access
            FROM public.subscription_coverage_terms t
            LEFT JOIN LATERAL (SELECT capacity,target_revision FROM public.capacity_increases WHERE account_id=t.account_id
                AND term_id=t.term_id AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
            LEFT JOIN LATERAL (SELECT new_access_ends_at, term_revision FROM public.refund_access_decisions
                WHERE account_id=t.account_id AND term_id=t.term_id AND term_revision IS NOT NULL
                ORDER BY term_revision DESC LIMIT 1) rd ON true
            JOIN public.subscription_term_facts f USING (account_id, term_id)
            WHERE t.account_id = {account} AND t.term_id = {term} AND NOT EXISTS (SELECT FROM public.capacity_increases i
                WHERE i.account_id=t.account_id AND i.term_id=t.term_id AND i.state='Pending')
                AND NOT EXISTS (SELECT FROM public.payment_fulfilments s
                WHERE s.account_id = t.account_id AND s.predecessor_term_id = t.term_id)
            """).SingleOrDefaultAsync(ct);

    public async Task SaveAsync(CheckoutQuoteV1 q, CheckoutSource source, Guid actor, Guid key, string requestHash,
        Guid replay, CancellationToken ct)
    {
        // The deferred FK permits inserting the children first. Once the parent exists the set is sealed.
        var ids = source.Shops.Select(s => s.ShopId).ToArray();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.checkout_order_shops (account_id, order_id, shop_id, shop_name, affiliation_request_id, replay_id)
            SELECT account_id, {q.OrderId}, shop_id, shop_name, request_id, {replay}
            FROM public.shop_affiliations WHERE account_id = {q.AccountId} AND shop_id = ANY({ids})
            """, ct);
        if (source.Trials is { Length: > 0 } trials)
            await SaveTrialSourcesAsync(q, trials, ct);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.checkout_orders
                (account_id, order_id, actor_id, operation_key, request_hash, replay_id, term_id, quote_revision,
                 amount_paise, shop_count, shop_hash, snapshot_hash, created_at, expires_at, snapshot)
            VALUES ({q.AccountId}, {q.OrderId}, {actor}, {key}, {requestHash}, {replay}, {q.TermId}, {q.Revision},
                {q.AmountPaise}, {q.ShopCount}, {q.ShopHash}, {q.SnapshotHash}, {q.CreatedAt}, {q.ExpiresAt},
                {JsonSerializer.Serialize(q, Json)}::jsonb)
            """, ct);
    }

    private Task<int> SaveTrialSourcesAsync(CheckoutQuoteV1 quote, CheckoutTrialSource[] trials, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.checkout_trial_sources (account_id, order_id, shop_id, trial_term_id, revision, ends_at)
            SELECT {quote.AccountId}, {quote.OrderId}, shop_id, trial_term_id, revision, ends_at
            FROM unnest({trials.Select(t => t.ShopId).ToArray()}, {trials.Select(t => t.TermId).ToArray()},
                {trials.Select(t => t.Revision).ToArray()}, {trials.Select(t => t.EndsAt.ToUniversalTime()).ToArray()})
                AS s(shop_id, trial_term_id, revision, ends_at)
            """, ct);

    public async Task<CheckoutShopPageV1> PageAsync(Guid account, Guid? order, Guid? after, CancellationToken ct)
    {
        var rows = await (order is { } id
            ? db.Database.SqlQuery<CheckoutShopV1>($"""
                SELECT s.shop_id AS shop_id, s.shop_name AS name, q.term_id AS term_id
                FROM public.checkout_order_shops s JOIN public.checkout_orders q USING (account_id, order_id)
                WHERE s.account_id = {account} AND s.order_id = {id}
                    AND ({after}::uuid IS NULL OR s.shop_id > {after})
                ORDER BY s.shop_id LIMIT {CheckoutOrdersV1.PageSize + 1}
                """)
            : db.Database.SqlQuery<CheckoutShopV1>($"""
                SELECT a.shop_id AS shop_id, a.shop_name AS name,
                   coalesce((SELECT p.term_id FROM public.payment_fulfilment_shops p
                       JOIN public.subscription_coverage_terms pt ON pt.account_id = p.account_id AND pt.term_id = p.term_id
                       WHERE p.account_id = a.account_id AND p.shop_id = a.shop_id ORDER BY pt.ends_at DESC LIMIT 1), a.term_id) AS term_id
                FROM public.shop_affiliations a
                WHERE a.account_id = {account} AND ({after}::uuid IS NULL OR a.shop_id > {after})
                ORDER BY a.shop_id LIMIT {CheckoutOrdersV1.PageSize + 1}
                """)).ToListAsync(ct);
        var items = rows.Take(CheckoutOrdersV1.PageSize).ToArray();
        return new(account, order, items, rows.Count > items.Length ? items[^1].ShopId : null);
    }

    public Task<Guid[]> ShopIdsAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"""
            SELECT shop_id AS "Value" FROM public.checkout_order_shops
            WHERE account_id = {account} AND order_id = {order} ORDER BY shop_id
            """).ToArrayAsync(ct);

    public Task<StoredCheckoutAcceptance?> FindAcceptanceAsync(Guid account, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<StoredCheckoutAcceptance>($"""
            SELECT order_id AS order_id, actor_id AS actor_id, request_hash AS request_hash
            FROM public.checkout_order_acceptances WHERE account_id = {account} AND operation_key = {key}
            """).SingleOrDefaultAsync(ct);

    public Task<int> AcceptAsync(CheckoutQuoteV1 quote, CheckoutAcceptanceV1 acceptance, Guid actor, Guid request,
        Guid replay, string requestHash, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.checkout_order_acceptances (account_id, order_id, actor_id, membership_revision,
                request_id, operation_key, request_hash, replay_id, quote_revision, snapshot_hash,
                customer_terms_version, refund_policy_version, accepted_at)
            SELECT account_id, {quote.OrderId}, merchant_user_id, revision, {request}, {acceptance.OperationKey},
                {requestHash}, {replay}, {acceptance.QuoteRevision}, {acceptance.SnapshotHash},
                {quote.CustomerTermsVersion}, {quote.RefundPolicyVersion}, {acceptance.AcceptedAt}
            FROM public.billing_account_members WHERE account_id = {quote.AccountId} AND merchant_user_id = {actor}
                AND status = 1 AND role IN (1, 2)
            """, ct);

    private sealed record QuoteRow(string Snapshot, Guid ActorId, string RequestHash, Guid? AcceptKey, DateTimeOffset? AcceptedAt);
}
