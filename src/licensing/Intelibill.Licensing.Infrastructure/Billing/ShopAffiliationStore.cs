using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class ShopAffiliationStore(LicensingDbContext db, TimeProvider clock) : IShopAffiliationStore
{
    public async Task<ErrorOr<ShopAffiliationRequestV1>> RequestAsync(Guid account, Guid actor,
        RequestShopAffiliationV1 request, CancellationToken ct)
    {
        var prior = await db.Database.SqlQuery<Guid>($"""
            SELECT request_id AS "Value" FROM public.shop_affiliation_requests
            WHERE account_id = {account} AND requester_id = {actor} AND operation_key = {request.OperationKey}
            """).SingleOrDefaultAsync(ct);
        if (prior != Guid.Empty)
        {
            var existing = await ReadAsync(account, prior, ct);
            return existing?.ShopId == request.ShopId && existing.Term?.TermId == request.TermId
                ? existing : BillingAccountErrors.PayloadMismatch;
        }

        var now = Now;
        if (request.TermId is { } term && !await CanAssignAsync(account, term, now, ct))
            return ShopAffiliationPolicy.Conflict;

        var id = Guid.NewGuid();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_affiliation_requests
                (request_id, account_id, account_name, shop_id, requester_id, requester_revision,
                 operation_key, term_id, created_at, expires_at)
            SELECT {id}, a.id, a.name, {request.ShopId}, m.merchant_user_id, m.revision,
                {request.OperationKey}, {request.TermId}, {now}, {now.AddDays(7)}
            FROM public.billing_accounts a JOIN public.billing_account_members m ON a.id = m.account_id
            WHERE a.id = {account} AND m.merchant_user_id = {actor} AND m.status = 1
            """, ct);
        return inserted == 1 && await ReadAsync(account, id, ct) is { } created
            ? created : BillingDelegationErrors.Denied;
    }

    public async Task<ErrorOr<AccountAffiliationsV1>> ListAsync(Guid account, CancellationToken ct)
    {
        var name = await db.Database.SqlQuery<string>($"""
            SELECT name AS "Value" FROM public.billing_accounts WHERE id = {account}
            """).SingleOrDefaultAsync(ct);
        if (name is null)
            return BillingDelegationErrors.Denied;
        var terms = await db.Database.SqlQuery<SubscriptionCoverageV1>($"""
            SELECT term_id AS term_id, mode AS mode, capacity AS capacity,
                starts_at AS starts_at, ends_at AS ends_at
            FROM public.subscription_coverage_terms t WHERE t.account_id = {account} AND t.ends_at > {Now}
                AND NOT EXISTS (SELECT FROM public.trial_history h WHERE h.term_id = t.term_id)
            ORDER BY starts_at, term_id LIMIT {ShopAffiliationsV1.PageSize}
            """).ToArrayAsync(ct);
        var requests = await Requests(account).OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.RequestId).Take(ShopAffiliationsV1.PageSize).ToArrayAsync(ct);
        return new AccountAffiliationsV1(account, name, [.. requests.Select(row => row.View())], terms);
    }

    public async Task<ErrorOr<ShopAffiliationRequestV1>> ReadForOwnerAsync(Guid account, Guid shop, Guid request, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.affiliation_shop_id', {shop.ToString("D")}, true),
                   set_config('app.affiliation_request_id', {request.ToString("D")}, true)
            """, ct);
        return await ReadAsync(account, request, ct) is { } detail && detail.ShopId == shop
            ? detail : ShopAffiliationPolicy.Missing;
    }

    private DateTimeOffset Now => BillingAccountRules.StorageInstant(clock.GetUtcNow());

    private Task<bool> CanAssignAsync(Guid account, Guid term, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.subscription_coverage_terms t WHERE t.account_id = {account} AND t.term_id = {term}
                  AND NOT EXISTS (SELECT FROM public.trial_history h WHERE h.term_id = t.term_id)
                  AND t.starts_at <= {now} AND {now} < t.ends_at AND t.capacity >
                      (SELECT count(*) FROM public.shop_affiliations a WHERE a.term_id = t.term_id)
            ) AS "Value"
            """).SingleAsync(ct);

    private async Task<ShopAffiliationRequestV1?> ReadAsync(Guid account, Guid request, CancellationToken ct) =>
        (await Requests(account).SingleOrDefaultAsync(row => row.RequestId == request, ct))?.View();

    private IQueryable<RequestRow> Requests(Guid account) => db.Database.SqlQuery<RequestRow>($"""
        SELECT r.request_id AS request_id, r.account_id AS account_id, r.account_name AS account_name,
            r.shop_id AS shop_id, r.term_id AS term_id, t.mode AS mode, t.capacity AS capacity,
            t.starts_at AS starts_at, t.ends_at AS ends_at, r.expires_at AS expires_at, r.created_at AS created_at,
            COALESCE(i.status, 'AwaitingConsent') AS status,
            a.shop_name AS shop_name, a.owner_name AS owner_name, a.applied_at AS applied_at
        FROM public.shop_affiliation_requests r
        LEFT JOIN public.subscription_coverage_terms t ON r.term_id = t.term_id
        LEFT JOIN public.shop_affiliations a ON r.request_id = a.request_id
        LEFT JOIN public.shop_affiliation_inbox i ON r.request_id = i.request_id
        WHERE r.account_id = {account}
        """);

    private sealed record RequestRow(Guid RequestId, Guid AccountId, string AccountName, Guid ShopId, Guid? TermId,
        string? Mode, int? Capacity, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateTimeOffset ExpiresAt,
        DateTimeOffset CreatedAt, string Status, string? ShopName, string? OwnerName, DateTimeOffset? AppliedAt)
    {
        internal ShopAffiliationRequestV1 View() => new(RequestId, AccountId, AccountName, ShopId,
            TermId is { } id ? new(id, Mode!, Capacity!.Value, StartsAt!.Value, EndsAt!.Value) : null,
            ExpiresAt, Status, ShopName, OwnerName, AppliedAt);
    }
}
