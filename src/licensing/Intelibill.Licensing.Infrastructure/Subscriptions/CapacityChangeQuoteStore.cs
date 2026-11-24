using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Subscriptions;

/// <summary>
/// SQL-owned capacity-change quote evidence. Every read runs inside the delegation transaction, so row-level security
/// limits it to the delegated account and the live owner; the term facts come from fulfilment, never the catalogue.
/// </summary>
internal sealed class CapacityChangeQuoteStore(LicensingDbContext db) : ICapacityChangeQuoteStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StoredCapacityChangeQuote?> FindAsync(Guid account, Guid operationKey, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<QuoteRow>($"""
            SELECT snapshot::text AS snapshot, actor_id AS actor_id, request_hash AS request_hash
            FROM public.capacity_change_quotes WHERE account_id = {account} AND operation_key = {operationKey}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null
            : new(JsonSerializer.Deserialize<CapacityChangeQuoteV1>(row.Snapshot, Json)!, row.ActorId, row.RequestHash);
    }

    public async Task<CapacityChangeSource?> SourceAsync(Guid account, Guid term, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<SourceRow>($"""
            SELECT t.term_id AS term_id, t.starts_at AS starts_at, t.ends_at AS ends_at, t.capacity AS capacity,
                f.basis AS basis, f.billing_period AS period, f.anchor_local AS anchor_local, f.period_ordinal AS ordinal,
                f.calendar_version AS calendar_version, coalesce(f.member_allowance, h.member_cap) AS member_allowance,
                f.price_paise AS price_paise, f.currency AS currency, f.plan_version_id AS plan_version_id,
                coalesce(f.development_fixture, h.development_fixture, false) AS development_fixture,
                h.shop_id IS NOT NULL AS trial,
                EXISTS (SELECT FROM public.payment_fulfilments s
                    WHERE s.account_id = t.account_id AND s.predecessor_term_id = t.term_id) AS superseded,
                coalesce((SELECT max(c.revision) FROM public.scheduled_subscription_changes c
                    WHERE c.account_id = t.account_id AND c.term_id = t.term_id), 0) AS cancellation_revision
            FROM public.subscription_coverage_terms t
            LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
            LEFT JOIN public.trial_history h ON h.account_id = t.account_id AND h.term_id = t.term_id
            WHERE t.account_id = {account} AND t.term_id = {term}
            """).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;
        var shops = await ShopsAsync(account, term, ct);
        var ids = shops.Select(s => s.ShopId).ToArray();
        return row.ToSource(shops, await SuspendedAsync(account, ids, now, ct), await ClosedAsync(account, ids, ct));
    }

    // Affiliation-assigned shops of an imported term, the paid links the payment pipeline issued, or a trial's shop.
    private Task<CapacityChangeSourceShop[]> ShopsAsync(Guid account, Guid term, CancellationToken ct) =>
        db.Database.SqlQuery<CapacityChangeSourceShop>($"""
            SELECT shop_id, name, request_id FROM (
                SELECT a.shop_id AS shop_id, a.shop_name AS name, a.request_id AS request_id
                FROM public.shop_affiliations a WHERE a.account_id = {account} AND a.term_id = {term}
                UNION
                SELECT a.shop_id, a.shop_name, a.request_id FROM public.payment_fulfilment_shops p
                JOIN public.shop_affiliations a ON a.account_id = p.account_id AND a.shop_id = p.shop_id
                    AND a.request_id = p.affiliation_request_id
                WHERE p.account_id = {account} AND p.term_id = {term}
                UNION
                SELECT a.shop_id, a.shop_name, a.request_id FROM public.trial_history h
                JOIN public.shop_affiliations a ON a.account_id = h.account_id AND a.shop_id = h.shop_id
                WHERE h.account_id = {account} AND h.term_id = {term}
            ) covered ORDER BY shop_id LIMIT {CheckoutOrdersV1.MaxShops + 1}
            """).ToArrayAsync(ct);

    private Task<bool> SuspendedAsync(Guid account, Guid[] shops, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.active_operational_restrictions r WHERE r.account_id = {account}
                AND r.effective_at <= {now} AND (r.shop_id IS NULL OR r.shop_id = ANY({shops}))) AS "Value"
            """).SingleAsync(ct);

    private Task<bool> ClosedAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.active_operational_closure_targets c
                WHERE c.account_id = {account} AND c.shop_id = ANY({shops})) AS "Value"
            """).SingleAsync(ct);

    public async Task SaveAsync(CapacityChangeQuoteV1 q, CapacityChangeSource source, PaymentProviderPin? pin,
        long? routingRevision, Guid actor, Guid key, string requestHash, Guid replay, CancellationToken ct)
    {
        // The deferred FK lets the named shops precede their header; once the header exists the set is sealed.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.capacity_change_quote_shops (account_id, quote_id, shop_id, shop_name, affiliation_request_id, replay_id)
            SELECT {q.AccountId}, {q.QuoteId}, shop_id, shop_name, request_id, {replay}
            FROM unnest({source.Shops.Select(s => s.ShopId).ToArray()}, {source.Shops.Select(s => s.Name).ToArray()},
                {source.Shops.Select(s => s.RequestId).ToArray()}) AS s(shop_id, shop_name, request_id)
            """, ct);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.capacity_change_quotes (account_id, quote_id, term_id, actor_id, operation_key, request_hash,
                replay_id, outcome, reason, source_plan_version_id, target_plan_version_id, prior_capacity, target_capacity,
                prior_member_allowance, target_member_allowance, shop_count, shop_hash, source_hash, purchased_gross_paise,
                target_gross_paise, difference_paise, amount_paise, interval_starts_at, paid_through, total_seconds,
                remaining_seconds, proration_version, provider_id, provider_account_id, provider_environment,
                provider_configuration_version, routing_revision, created_at, expires_at, snapshot_hash, snapshot)
            VALUES ({q.AccountId}, {q.QuoteId}, {q.TermId}, {actor}, {key}, {requestHash}, {replay}, {q.Outcome}, {q.Reason},
                {q.SourcePlanVersionId}, {q.TargetPlanVersionId}, {q.PriorCapacity}, {q.TargetCapacity},
                {q.PriorMemberAllowance}, {q.TargetMemberAllowance}, {q.ShopCount}, {q.ShopHash}, {q.SourceHash},
                {q.PurchasedGrossPaise}, {q.TargetGrossPaise}, {q.DifferencePaise}, {q.AmountPaise}, {q.IntervalStartsAt},
                {q.PaidThrough}, {q.TotalSeconds}, {q.RemainingSeconds}, {q.ProrationVersion}, {pin?.ProviderId},
                {pin?.ProviderAccountId}, {pin?.Environment}, {pin?.ConfigurationVersion}, {routingRevision},
                {q.CreatedAt}, {q.ExpiresAt}, {q.SnapshotHash}, {JsonSerializer.Serialize(q, Json)}::jsonb)
            """, ct);
    }

    public async Task<CapacityChangeShopPageV1?> PageAsync(Guid account, Guid term, Guid quote, Guid? after, CancellationToken ct)
    {
        var exists = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.capacity_change_quotes
                WHERE account_id = {account} AND term_id = {term} AND quote_id = {quote}) AS "Value"
            """).SingleAsync(ct);
        if (!exists)
            return null;
        var rows = await db.Database.SqlQuery<CapacityChangeShopV1>($"""
            SELECT shop_id AS shop_id, shop_name AS name FROM public.capacity_change_quote_shops
            WHERE account_id = {account} AND quote_id = {quote} AND ({after}::uuid IS NULL OR shop_id > {after})
            ORDER BY shop_id LIMIT {CapacityChangeQuotesV1.PageSize + 1}
            """).ToListAsync(ct);
        var items = rows.Take(CapacityChangeQuotesV1.PageSize).ToArray();
        return new(account, quote, items, rows.Count > items.Length ? items[^1].ShopId : null);
    }

    private sealed record QuoteRow(string Snapshot, Guid ActorId, string RequestHash);

    private sealed record SourceRow(Guid TermId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, string? Basis,
        string? Period, DateTime? AnchorLocal, int? Ordinal, string? CalendarVersion, int? MemberAllowance, long? PricePaise,
        string? Currency, Guid? PlanVersionId, bool DevelopmentFixture, bool Trial, bool Superseded, int CancellationRevision)
    {
        internal CapacityChangeSource ToSource(CapacityChangeSourceShop[] shops, bool suspended, bool closed) => new(TermId,
            StartsAt.ToUniversalTime(), EndsAt.ToUniversalTime(), Capacity, Basis, Period, AnchorLocal, Ordinal, CalendarVersion,
            MemberAllowance, PricePaise, Currency, PlanVersionId, DevelopmentFixture, Trial, Superseded, CancellationRevision,
            suspended, closed, shops);
    }
}
