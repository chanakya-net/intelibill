using System.Text.Json;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Status;

/// <summary>
/// Reads coverage rows, ended ones included, with their optional supplement in one statement under the
/// transaction's row-level scope. A missing supplement stays missing; overlapping sources are not merged.
/// Complimentary grants are read as separate candidates beside the baseline term, never in its place. A paid
/// fulfilment link is the last baseline fallback after trial and affiliation coverage.
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
                f.calendar_version AS calendar_version, f.access_policy_version AS access_policy_version,
                e.ends_at AS extended_ends_at,
                CASE WHEN c.change_id IS NOT NULL THEN jsonb_build_object(
                    'ChangeId', coalesce(c.withdraws_change_id, c.change_id), 'Version', c.revision, 'EffectiveAt', c.effective_at,
                    'PolicyVersion', c.policy_version, 'ShopHash', c.shop_hash, 'ShopCount', c.shop_count,
                    'WithdrawnAt', CASE WHEN c.kind = 'WithdrawCancellation' THEN c.recorded_at END)::text END AS cancellation,
                CASE WHEN h.shop_id IS NOT NULL THEN jsonb_build_object(
                    'ComputationVersion', h.computation_version, 'MemberCap', h.member_cap, 'ActiveMembers', h.active_members,
                    'AccountTrialCap', h.account_trial_cap, 'DevelopmentFixture', h.development_fixture,
                    'ConflictingCoverage', a.term_id IS NOT NULL OR a.account_id <> h.account_id)::text END AS trial
            FROM public.shop_affiliations a
            LEFT JOIN public.trial_history h ON h.shop_id = a.shop_id
            LEFT JOIN public.payment_fulfilment_shops p ON p.shop_id = a.shop_id AND p.account_id = a.account_id
            LEFT JOIN public.subscription_coverage_terms t
                ON t.account_id = coalesce(h.account_id, a.account_id) AND t.term_id = coalesce(h.term_id, a.term_id, p.term_id)
            LEFT JOIN LATERAL (SELECT ends_at FROM public.trial_extensions
                WHERE shop_id = h.shop_id AND account_id = h.account_id ORDER BY revision DESC LIMIT 1) e ON true
            LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
            LEFT JOIN LATERAL (SELECT * FROM public.scheduled_subscription_changes
                WHERE account_id = t.account_id AND term_id = t.term_id ORDER BY revision DESC LIMIT 1) c ON true
            WHERE a.shop_id = {shop}
            """).ToListAsync(ct);
        return rows switch
        {
            [] => null,
            [var row] => new ShopCoverage(row.AffiliationAccountId, row.TermId is null ? null : row.ToTerm(),
                await ReadGrantsAsync(shop, row.AffiliationAccountId, ct)),
            _ => throw new InvalidOperationException("A shop has at most one affiliation."),
        };
    }

    // Only grants naming this shop in its current account; operational columns only, never reason or actor.
    private async Task<IReadOnlyList<TermFactsRow>> ReadGrantsAsync(Guid shop, Guid account, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<GrantRow>($"""
            SELECT t.term_id AS term_id, t.account_id AS account_id, t.mode AS mode, t.capacity AS capacity,
                t.starts_at AS starts_at, t.ends_at AS ends_at,
                jsonb_build_object('PolicyVersion', g.policy_version, 'MemberAllowance', g.member_allowance,
                    'ShopCount', g.shop_capacity, 'Consistent', g.starts_at = t.starts_at AND g.ends_at = t.ends_at
                        AND l.starts_at = g.starts_at AND l.ends_at = g.ends_at)::text AS complimentary
            FROM public.complimentary_grant_shops l
            JOIN public.complimentary_grants g ON g.grant_id = l.grant_id AND g.account_id = l.account_id
            JOIN public.subscription_coverage_terms t ON t.term_id = g.term_id AND t.account_id = g.account_id
            WHERE l.shop_id = {shop} AND l.account_id = {account}
            ORDER BY t.term_id
            """).ToListAsync(ct);
        return [.. rows.Select(row => row.ToTerm())];
    }

    public async Task<IReadOnlyList<AccountTermRow>> ReadAccountAsync(Guid account, Guid? after, int take, CancellationToken ct)
    {
        var terms = await Terms(account, null, after, take).ToListAsync(ct);
        if (terms.Count == 0) return [];

        var ids = terms.Select(term => term.TermId).ToArray();
        var shops = await db.Database.SqlQuery<CoveredRow>($"""
            SELECT term_id AS term_id, shop_id AS shop_id, shop_name AS shop_name, total AS total FROM (
                SELECT term_id, shop_id, shop_name, count(*) OVER (PARTITION BY term_id) AS total,
                    row_number() OVER (PARTITION BY term_id ORDER BY shop_id) AS position
                FROM (
                    SELECT term_id, shop_id, shop_name
                    FROM public.shop_affiliations WHERE account_id = {account} AND term_id = ANY({ids})
                    UNION ALL
                    SELECT h.term_id, h.shop_id, a.shop_name
                    FROM public.trial_history h JOIN public.shop_affiliations a USING (account_id, shop_id)
                    WHERE h.account_id = {account} AND h.term_id = ANY({ids})
                    UNION ALL
                    SELECT l.term_id, l.shop_id, a.shop_name
                    FROM public.complimentary_grant_shops l JOIN public.shop_affiliations a
                        ON a.request_id = l.affiliation_request_id AND a.account_id = l.account_id
                    WHERE l.account_id = {account} AND l.term_id = ANY({ids})
                    UNION ALL
                    SELECT p.term_id, p.shop_id, a.shop_name
                    FROM public.payment_fulfilment_shops p JOIN public.shop_affiliations a
                        ON a.request_id = p.affiliation_request_id AND a.account_id = p.account_id
                    WHERE p.account_id = {account} AND p.term_id = ANY({ids})
                ) assignments
            ) covered WHERE position <= {SubscriptionStatusV1.MaximumCoveredShops}
            ORDER BY term_id, shop_id
            """).ToListAsync(ct);
        var byTerm = shops.ToLookup(row => row.TermId);
        return [.. terms.Select(term => new AccountTermRow(term.ToTerm(),
            [.. byTerm[term.TermId].Select(row => new CoveredShopV1(row.ShopId, row.ShopName))],
            (int)(byTerm[term.TermId].FirstOrDefault()?.Total ?? 0)))];
    }

    internal async Task<TermFactsRow?> ReadTermAsync(Guid account, Guid term, CancellationToken ct) =>
        (await Terms(account, term, null, 1).SingleOrDefaultAsync(ct))?.ToTerm();

    private IQueryable<TermRow> Terms(Guid account, Guid? term, Guid? after, int take) =>
        db.Database.SqlQuery<TermRow>($"""
            SELECT t.term_id AS term_id, t.account_id AS account_id, t.mode AS mode, t.capacity AS capacity,
                t.starts_at AS starts_at, t.ends_at AS ends_at, f.basis AS basis, f.billing_period AS billing_period,
                f.anchor_local AS anchor_local, f.time_zone AS time_zone, f.period_ordinal AS period_ordinal,
                f.member_allowance AS member_allowance, f.price_paise AS price_paise, f.currency AS currency,
                f.plan_version_id AS plan_version_id, f.source_reference AS source_reference,
                f.development_fixture AS development_fixture,
                f.calendar_version AS calendar_version, f.access_policy_version AS access_policy_version,
                e.ends_at AS extended_ends_at,
                CASE WHEN c.change_id IS NOT NULL THEN jsonb_build_object(
                    'ChangeId', coalesce(c.withdraws_change_id, c.change_id), 'Version', c.revision, 'EffectiveAt', c.effective_at,
                    'PolicyVersion', c.policy_version, 'ShopHash', c.shop_hash, 'ShopCount', c.shop_count,
                    'WithdrawnAt', CASE WHEN c.kind = 'WithdrawCancellation' THEN c.recorded_at END)::text END AS cancellation,
                CASE WHEN h.shop_id IS NOT NULL THEN jsonb_build_object(
                    'ComputationVersion', h.computation_version, 'MemberCap', h.member_cap, 'ActiveMembers', h.active_members,
                    'AccountTrialCap', h.account_trial_cap, 'DevelopmentFixture', h.development_fixture,
                    'ConflictingCoverage', a.term_id IS NOT NULL OR a.account_id IS DISTINCT FROM h.account_id)::text END AS trial,
                CASE WHEN g.grant_id IS NOT NULL THEN jsonb_build_object('PolicyVersion', g.policy_version,
                    'MemberAllowance', g.member_allowance, 'ShopCount', g.shop_capacity,
                    'Consistent', g.starts_at = t.starts_at AND g.ends_at = t.ends_at)::text END AS complimentary
            FROM public.subscription_coverage_terms t
            LEFT JOIN public.trial_history h ON h.term_id = t.term_id AND h.account_id = t.account_id
            LEFT JOIN public.shop_affiliations a ON a.shop_id = h.shop_id AND a.account_id = h.account_id
            LEFT JOIN LATERAL (SELECT ends_at FROM public.trial_extensions
                WHERE shop_id = h.shop_id AND account_id = h.account_id ORDER BY revision DESC LIMIT 1) e ON true
            LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
            LEFT JOIN public.complimentary_grants g ON g.term_id = t.term_id AND g.account_id = t.account_id
            LEFT JOIN LATERAL (SELECT * FROM public.scheduled_subscription_changes
                WHERE account_id = t.account_id AND term_id = t.term_id ORDER BY revision DESC LIMIT 1) c ON true
            WHERE t.account_id = {account} AND ({term}::uuid IS NULL OR t.term_id = {term}::uuid) AND ({after}::uuid IS NULL OR t.term_id > {after}::uuid)
            ORDER BY t.term_id LIMIT {take}
            """);

    private sealed record ShopRow(Guid AffiliationAccountId, Guid? TermId, Guid? AccountId, string? Mode, int? Capacity,
        DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal,
        string? TimeZone, int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency,
        Guid? PlanVersionId, string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion,
        string? Trial, DateTimeOffset? ExtendedEndsAt, string? Cancellation)
    {
        internal TermFactsRow ToTerm() => new(TermId!.Value, AccountId!.Value, Mode!, Capacity!.Value, StartsAt!.Value,
            EndsAt!.Value, Basis, BillingPeriod, AnchorLocal, TimeZone, PeriodOrdinal, MemberAllowance, PricePaise,
            Currency, PlanVersionId, SourceReference, DevelopmentFixture, CalendarVersion, AccessPolicyVersion, ParseTrial(Trial, ExtendedEndsAt), Cancellation: ParseCancellation(Cancellation));
    }

    private sealed record TermRow(Guid TermId, Guid AccountId, string Mode, int Capacity, DateTimeOffset StartsAt,
        DateTimeOffset EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal, string? TimeZone,
        int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency, Guid? PlanVersionId,
        string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion, string? Trial,
        string? Complimentary, DateTimeOffset? ExtendedEndsAt, string? Cancellation)
    {
        internal TermFactsRow ToTerm() => new(TermId, AccountId, Mode, Capacity, StartsAt, EndsAt, Basis, BillingPeriod,
            AnchorLocal, TimeZone, PeriodOrdinal, MemberAllowance, PricePaise, Currency, PlanVersionId,
            SourceReference, DevelopmentFixture, CalendarVersion, AccessPolicyVersion, ParseTrial(Trial, ExtendedEndsAt), ParseGrant(Complimentary), ParseCancellation(Cancellation));
    }

    private sealed record GrantRow(Guid TermId, Guid AccountId, string Mode, int Capacity, DateTimeOffset StartsAt,
        DateTimeOffset EndsAt, string Complimentary)
    {
        internal TermFactsRow ToTerm() => new(TermId, AccountId, Mode, Capacity, StartsAt, EndsAt, null, null, null, null,
            null, null, null, null, null, null, null, null, null, Complimentary: ParseGrant(Complimentary));
    }

    private static TrialTermProvenance? ParseTrial(string? json, DateTimeOffset? extendedEndsAt) =>
        json is null ? null : JsonSerializer.Deserialize<TrialTermProvenance>(json)! with { ExtendedEndsAt = extendedEndsAt };

    private static ComplimentaryTermProvenance? ParseGrant(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<ComplimentaryTermProvenance>(json);

    private static ScheduledSubscriptionChange? ParseCancellation(string? json) =>
        json is not null && JsonSerializer.Deserialize<ScheduledSubscriptionChange>(json) is { } change
            ? change with { EffectiveAt = change.EffectiveAt.ToUniversalTime(), WithdrawnAt = change.WithdrawnAt?.ToUniversalTime() }
            : null;

    private sealed record CoveredRow(Guid TermId, Guid ShopId, string ShopName, long Total);
}
