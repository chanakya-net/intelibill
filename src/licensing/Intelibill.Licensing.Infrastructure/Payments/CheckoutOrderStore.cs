using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed class CheckoutOrderStore(LicensingDbContext db) : ICheckoutOrderStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
            var shops = await ReadSourceShopsAsync(account, id, [], ct);
            return prior is not null && shops.Length is > 0 and <= CheckoutOrdersV1.MaxShops
                ? new(shops, prior) : null;
        }

        var selected = await ReadSourceShopsAsync(account, null, requested, ct);
        return selected.Length == requested.Length && selected.All(shop => shop.TermId is null)
            ? new(selected, null) : null;
    }

    private async Task<CheckoutSourceShop[]> ReadSourceShopsAsync(Guid account, Guid? term, Guid[] requested,
        CancellationToken ct) =>
        await db.Database.SqlQuery<CheckoutSourceShop>($"""
            SELECT a.shop_id AS shop_id, a.shop_name AS name, a.request_id AS request_id,
                   coalesce(a.term_id, p.term_id) AS term_id, a.applied_at AS applied_at, a.coverage_ends_at AS coverage_ends_at
            FROM public.shop_affiliations a
            LEFT JOIN public.payment_fulfilment_shops p ON p.account_id = a.account_id AND p.shop_id = a.shop_id
            WHERE a.account_id = {account} AND (a.term_id = {term} OR a.shop_id = ANY({requested}))
            ORDER BY a.shop_id LIMIT {CheckoutOrdersV1.MaxShops + 1}
            """).ToArrayAsync(ct);

    private Task<CheckoutRenewalSource?> ReadTermAsync(Guid account, Guid term, CancellationToken ct) =>
        db.Database.SqlQuery<CheckoutRenewalSource>($"""
            SELECT t.starts_at AS starts_at, t.ends_at AS ends_at, t.capacity AS capacity,
                   f.basis AS basis, f.billing_period AS period, f.anchor_local AS anchor_local,
                   f.period_ordinal AS ordinal, f.calendar_version AS calendar_version,
                   f.access_policy_version AS access_policy_version, coalesce((
                       SELECT max(c.revision) FROM public.scheduled_subscription_changes c
                       WHERE c.account_id = t.account_id AND c.term_id = t.term_id), 0) AS cancellation_revision
            FROM public.subscription_coverage_terms t
            JOIN public.subscription_term_facts f USING (account_id, term_id)
            WHERE t.account_id = {account} AND t.term_id = {term}
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
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.checkout_orders
                (account_id, order_id, actor_id, operation_key, request_hash, replay_id, term_id, quote_revision,
                 amount_paise, shop_count, shop_hash, snapshot_hash, created_at, expires_at, snapshot)
            VALUES ({q.AccountId}, {q.OrderId}, {actor}, {key}, {requestHash}, {replay}, {q.TermId}, {q.Revision},
                {q.AmountPaise}, {q.ShopCount}, {q.ShopHash}, {q.SnapshotHash}, {q.CreatedAt}, {q.ExpiresAt},
                {JsonSerializer.Serialize(q, Json)}::jsonb)
            """, ct);
    }

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
                SELECT a.shop_id AS shop_id, a.shop_name AS name, coalesce(a.term_id, p.term_id) AS term_id
                FROM public.shop_affiliations a
                LEFT JOIN public.payment_fulfilment_shops p ON p.account_id = a.account_id AND p.shop_id = a.shop_id
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
