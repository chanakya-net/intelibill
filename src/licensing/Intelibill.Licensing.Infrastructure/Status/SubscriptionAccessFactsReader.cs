using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Status;

/// <summary>
/// Reads coverage rows, ended ones included, with their optional supplement in one statement under the
/// transaction's row-level scope. A missing supplement stays missing; overlapping sources are not merged.
/// </summary>
internal sealed class SubscriptionAccessFactsReader(LicensingDbContext db) : ISubscriptionAccessFactsReader
{
    public async Task<ShopCoverage?> ReadShopAsync(Guid shop, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ShopRow>($"""
            SELECT a.account_id AS affiliation_account_id, t.term_id AS term_id, t.account_id AS account_id,
                t.mode AS mode, t.capacity AS capacity, t.starts_at AS starts_at, t.ends_at AS ends_at,
                f.basis AS basis, f.billing_period AS billing_period, f.anchor_local AS anchor_local,
                f.time_zone AS time_zone, f.period_ordinal AS period_ordinal, f.member_allowance AS member_allowance,
                f.price_paise AS price_paise, f.currency AS currency, f.plan_version_id AS plan_version_id,
                f.source_reference AS source_reference, f.development_fixture AS development_fixture,
                f.calendar_version AS calendar_version, f.access_policy_version AS access_policy_version
            FROM public.shop_affiliations a
            LEFT JOIN public.subscription_coverage_terms t ON t.account_id = a.account_id AND t.term_id = a.term_id
            LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
            WHERE a.shop_id = {shop}
            """).ToListAsync(ct);
        return rows switch
        {
            [] => null,
            [var row] => new ShopCoverage(row.AffiliationAccountId, row.TermId is null ? null : row.ToTerm()),
            _ => throw new InvalidOperationException("A shop has at most one affiliation."),
        };
    }

    public async Task<IReadOnlyList<AccountTermRow>> ReadAccountAsync(Guid account, Guid? after, int take, CancellationToken ct)
    {
        var terms = await db.Database.SqlQuery<TermRow>($"""
            SELECT t.term_id AS term_id, t.account_id AS account_id, t.mode AS mode, t.capacity AS capacity,
                t.starts_at AS starts_at, t.ends_at AS ends_at, f.basis AS basis, f.billing_period AS billing_period,
                f.anchor_local AS anchor_local, f.time_zone AS time_zone, f.period_ordinal AS period_ordinal,
                f.member_allowance AS member_allowance, f.price_paise AS price_paise, f.currency AS currency,
                f.plan_version_id AS plan_version_id, f.source_reference AS source_reference,
                f.development_fixture AS development_fixture,
                f.calendar_version AS calendar_version, f.access_policy_version AS access_policy_version
            FROM public.subscription_coverage_terms t
            LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
            WHERE t.account_id = {account} AND ({after}::uuid IS NULL OR t.term_id > {after}::uuid)
            ORDER BY t.term_id LIMIT {take}
            """).ToListAsync(ct);
        if (terms.Count == 0) return [];

        var ids = terms.Select(term => term.TermId).ToArray();
        var shops = await db.Database.SqlQuery<CoveredRow>($"""
            SELECT term_id AS term_id, shop_id AS shop_id, shop_name AS shop_name, total AS total FROM (
                SELECT term_id, shop_id, shop_name, count(*) OVER (PARTITION BY term_id) AS total,
                    row_number() OVER (PARTITION BY term_id ORDER BY shop_id) AS position
                FROM public.shop_affiliations WHERE account_id = {account} AND term_id = ANY({ids})
            ) covered WHERE position <= {SubscriptionStatusV1.MaximumCoveredShops}
            ORDER BY term_id, shop_id
            """).ToListAsync(ct);
        var byTerm = shops.ToLookup(row => row.TermId);
        return [.. terms.Select(term => new AccountTermRow(term.ToTerm(),
            [.. byTerm[term.TermId].Select(row => new CoveredShopV1(row.ShopId, row.ShopName))],
            (int)(byTerm[term.TermId].FirstOrDefault()?.Total ?? 0)))];
    }

    private sealed record ShopRow(Guid AffiliationAccountId, Guid? TermId, Guid? AccountId, string? Mode, int? Capacity,
        DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal,
        string? TimeZone, int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency,
        Guid? PlanVersionId, string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion)
    {
        internal TermFactsRow ToTerm() => new(TermId!.Value, AccountId!.Value, Mode!, Capacity!.Value, StartsAt!.Value,
            EndsAt!.Value, Basis, BillingPeriod, AnchorLocal, TimeZone, PeriodOrdinal, MemberAllowance, PricePaise,
            Currency, PlanVersionId, SourceReference, DevelopmentFixture, CalendarVersion, AccessPolicyVersion);
    }

    private sealed record TermRow(Guid TermId, Guid AccountId, string Mode, int Capacity, DateTimeOffset StartsAt,
        DateTimeOffset EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal, string? TimeZone,
        int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency, Guid? PlanVersionId,
        string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion)
    {
        internal TermFactsRow ToTerm() => new(TermId, AccountId, Mode, Capacity, StartsAt, EndsAt, Basis, BillingPeriod,
            AnchorLocal, TimeZone, PeriodOrdinal, MemberAllowance, PricePaise, Currency, PlanVersionId,
            SourceReference, DevelopmentFixture, CalendarVersion, AccessPolicyVersion);
    }

    private sealed record CoveredRow(Guid TermId, Guid ShopId, string ShopName, long Total);
}
