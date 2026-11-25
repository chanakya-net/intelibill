using ErrorOr;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageReplacementStore(LicensingDbContext db, ICapacityChangeQuoteStore terms,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock) : ICoverageReplacementStore
{
    public async Task<ErrorOr<CoverageReplacementPreviewV1>> ReadAsync(Guid account, Guid term, int page, CancellationToken ct)
    {
        const int size = CoverageReplacementsV1.PageSize;
        if (page is < 1 or > int.MaxValue / size) return Error.Validation("Coverage.ReplacementInvalid");
        var offset = (page - 1) * size;
        var now = EntitlementSnapshotsV1.Instant(clock);
        var source = await terms.SourceAsync(account, term, now, ct);
        if (source is not { Basis: "Paid", MemberAllowance: { } members }) return Error.NotFound("Coverage.ReplacementNotFound");
        var covered = source.Shops.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.ShopId).Skip(offset).Take(size).ToArray();
        var deadlines = await db.Database.SqlQuery<Deadline>($"""
            SELECT shop_id, affiliation_request_id, max(valid_until) AS valid_until FROM public.offline_device_observations
            WHERE account_id={account} AND shop_id=ANY({covered.Select(s => s.ShopId).ToArray()}) AND valid_until>{now}
            GROUP BY shop_id, affiliation_request_id
            """).ToDictionaryAsync(d => (d.ShopId,d.AffiliationRequestId), d => d.ValidUntil, ct);
        var shops = await db.Database.SqlQuery<SlotShopV1>($"""
            SELECT shop_id,shop_name FROM public.shop_affiliations WHERE account_id={account}
                AND public.slot_shop_eligible({account},shop_id,{now},'infinity')
            ORDER BY shop_name,shop_id LIMIT {size + 1} OFFSET {offset}
            """).ToArrayAsync(ct);
        var changes = await Rows(account, now).Where(r => r.TermId == term).OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.ReplacementId).Skip(offset).Take(size + 1).ToArrayAsync(ct);
        var canSchedule = Intelibill.Licensing.Application.Changes.ScheduledChangeValidator.ValidateSource(source, now) is null &&
            await db.Database.SqlQuery<bool>($"""
                SELECT public.replacement_term_available({account},{term}) AND EXISTS(SELECT FROM public.billing_account_members
                    WHERE account_id={account} AND merchant_user_id=nullif(current_setting('app.billing_actor_id',true),'')::uuid
                        AND role=1 AND status=1) AND NOT EXISTS(SELECT FROM public.coverage_replacements
                    WHERE account_id={account} AND term_id={term} AND state IN ('AwaitingConsent','Scheduled')
                        AND NOT public.replacement_stale(replacement_id,{now})) AS "Value"
                """).SingleAsync(ct);
        return new CoverageReplacementPreviewV1(term, source.Capacity, members, source.EndsAt, canSchedule,
            [.. covered.Select(s => new ReplacementCoveredShopV1(s.ShopId, s.Name, deadlines.GetValueOrDefault((s.ShopId, s.RequestId))))],
            [.. shops.Take(size)], [.. changes.Take(size)], changes.Length > size || source.Shops.Length - offset > size || shops.Length > size);
    }

    private sealed record Deadline(Guid ShopId, Guid AffiliationRequestId, DateTimeOffset? ValidUntil);
}
