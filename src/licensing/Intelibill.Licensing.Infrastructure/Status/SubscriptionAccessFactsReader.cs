using System.Text.Json;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Status;

/// <summary>
/// Reads coverage rows, ended ones included, with their optional supplement in one statement under the
/// transaction's row-level scope. A missing supplement stays missing; overlapping sources are not merged.
/// Complimentary grants are read as separate candidates beside the baseline term, never in its place. A shop's
/// latest paid fulfilment link (a renewal chain's tail) precedes an affiliation-assigned term it may have renewed.
/// </summary>
internal sealed class SubscriptionAccessFactsReader(LicensingDbContext db,
    [FromKeyedServices(SubscriptionStatusRegistration.ClockKey)] TimeProvider clock) : ISubscriptionAccessFactsReader
{
    public async Task<ShopCoverage?> ReadShopAsync(Guid shop, CancellationToken ct, DateTimeOffset? evaluatedAt = null)
    {
        var at = evaluatedAt ?? SubscriptionStatusProjection.EvaluationInstant(clock);
        var bindings = await db.Database.SqlQuery<ShopBindingRow>($"""
            SELECT a.account_id AS affiliation_account_id, a.request_id AS request_id, a.term_id AS affiliation_term_id,
                h.account_id AS trial_account_id, h.term_id AS trial_term_id,
                e.ends_at AS extended_ends_at, x.paid_starts_at AS converted_paid_starts_at,
                CASE WHEN h.shop_id IS NOT NULL THEN jsonb_build_object(
                    'ComputationVersion', h.computation_version, 'MemberCap', h.member_cap, 'ActiveMembers', h.active_members,
                    'AccountTrialCap', h.account_trial_cap, 'DevelopmentFixture', h.development_fixture,
                    'ConflictingCoverage', a.term_id IS NOT NULL OR a.account_id <> h.account_id)::text END AS trial
            FROM public.shop_affiliations a
            LEFT JOIN public.payment_trial_conversions x ON x.account_id = a.account_id AND x.shop_id = a.shop_id
            LEFT JOIN public.trial_history h ON h.shop_id = a.shop_id
                AND (x.paid_starts_at IS NULL OR x.paid_starts_at > {at})
            LEFT JOIN LATERAL (SELECT ends_at FROM public.trial_extensions
                WHERE shop_id = h.shop_id AND account_id = h.account_id ORDER BY revision DESC LIMIT 1) e ON true
            WHERE a.shop_id = {shop}
            """).ToListAsync(ct);
        return bindings switch
        {
            [] => null,
            [var binding] => await ReadBoundCoverageAsync(shop, binding, at, ct),
            _ => throw new InvalidOperationException("A shop has at most one affiliation."),
        };
    }

    private async Task<ShopCoverage> ReadBoundCoverageAsync(Guid shop, ShopBindingRow binding, DateTimeOffset at, CancellationToken ct)
    {
        PaidLinkRow? paidLink = null;
        if (binding.TrialTermId is null)
        {
            paidLink = await db.Database.SqlQuery<PaidLinkRow>($"""
                SELECT l.term_id, l.coverage_from, l.slot_assignment_id
                FROM public.payment_fulfilment_shops l
                JOIN public.payment_fulfilments lf
                    ON lf.account_id = l.account_id AND lf.order_id = l.order_id AND lf.term_id = l.term_id
                JOIN public.subscription_coverage_terms lt
                    ON lt.account_id = l.account_id AND lt.term_id = l.term_id
                WHERE l.shop_id = {shop} AND l.account_id = {binding.AffiliationAccountId}
                    AND l.affiliation_request_id = {binding.RequestId}
                    AND (l.coverage_from IS NULL OR l.coverage_from <= {at})
                    AND lf.fulfilled_at <= {at}
                ORDER BY lt.ends_at DESC LIMIT 1
                """).SingleOrDefaultAsync(ct);
        }

        TermFactsRow? term = null;
        var targetAccount = binding.TrialAccountId ?? binding.AffiliationAccountId;
        var targetTerm = binding.TrialTermId ?? paidLink?.TermId ?? binding.AffiliationTermId;
        if (targetTerm is { } termId)
        {
            var termRow = await db.Database.SqlQuery<ShopTermRow>($"""
                SELECT t.term_id AS term_id, t.account_id AS account_id,
                    CASE WHEN i.capacity>1 THEN 'Grouped' ELSE t.mode END AS mode, coalesce(i.capacity,t.capacity) AS capacity,
                    t.starts_at AS starts_at, t.ends_at AS ends_at,
                    f.basis AS basis, f.billing_period AS billing_period, f.anchor_local AS anchor_local,
                    f.time_zone AS time_zone, f.period_ordinal AS period_ordinal, coalesce(i.members,f.member_allowance) AS member_allowance,
                    coalesce(i.gross,f.price_paise) AS price_paise, f.currency AS currency, coalesce(i.plan_version,f.plan_version_id) AS plan_version_id,
                    f.source_reference AS source_reference, f.development_fixture AS development_fixture,
                    f.calendar_version AS calendar_version, f.access_policy_version AS access_policy_version,
                    CASE WHEN c.change_id IS NOT NULL THEN jsonb_build_object(
                        'ChangeId', coalesce(c.withdraws_change_id, c.change_id), 'Version', c.revision, 'EffectiveAt', c.effective_at,
                        'PolicyVersion', c.policy_version, 'ShopHash', c.shop_hash, 'ShopCount', c.shop_count,
                        'WithdrawnAt', CASE WHEN c.kind = 'WithdrawCancellation' THEN c.recorded_at END)::text END AS cancellation,
                    EXISTS (SELECT FROM public.payment_fulfilments s WHERE s.account_id = t.account_id
                        AND s.predecessor_term_id = t.term_id AND NOT EXISTS (
                            SELECT FROM public.paid_fresh_starts pf WHERE pf.account_id = s.account_id
                                AND pf.order_id = s.order_id AND pf.kind = 'RestoredAccess')) AS renewed_at_end,
                    rd.new_paid_through AS refund_paid_through, rd.new_access_ends_at AS refund_access_ends_at
                FROM public.subscription_coverage_terms t
                LEFT JOIN LATERAL (SELECT capacity,members,gross,plan_version FROM public.capacity_increases
                    WHERE account_id=t.account_id AND term_id=t.term_id AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
                LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
                LEFT JOIN LATERAL public.refund_boundary(t.account_id, t.term_id) rd ON true
                LEFT JOIN LATERAL (SELECT * FROM public.scheduled_subscription_changes
                    WHERE account_id = t.account_id AND term_id = t.term_id ORDER BY revision DESC LIMIT 1) c ON true
                WHERE t.account_id = {targetAccount} AND t.term_id = {termId}
                """).SingleOrDefaultAsync(ct);
            term = termRow?.ToTerm(binding.Trial, binding.ExtendedEndsAt);
        }

        var predecessors = term is { Basis: "Paid", PeriodOrdinal: > 0 }
            ? await ReadPaidPredecessorsAsync(shop, term, ct) : [];
        if (term is not null)
        {
            var chainStart = predecessors.Count > 0 ? predecessors[^1].StartsAt : term.StartsAt;
            var paidFrom = binding.Trial is null && paidLink?.CoverageFrom is { } joined && joined.ToUniversalTime() > chainStart
                ? joined.ToUniversalTime() : predecessors.Count > 0 ? chainStart : (DateTimeOffset?)null;
            var slotAssignmentId = binding.Trial is null ? paidLink?.SlotAssignmentId : null;
            if (paidFrom is not null || slotAssignmentId is not null)
                term = term with { PaidFrom = paidFrom, SlotAssignmentId = slotAssignmentId };
        }
        return new ShopCoverage(binding.AffiliationAccountId, term, await ReadGrantsAsync(shop, binding.AffiliationAccountId, ct),
            predecessors, binding.ConvertedPaidStartsAt?.ToUniversalTime());
    }

    private async Task<List<TermFactsRow>> ReadPaidPredecessorsAsync(Guid shop, TermFactsRow tail, CancellationToken ct)
    {
        var facts = SubscriptionStatusProjection.ToFacts(tail);
        if (facts.Evidence != TermEvidenceState.Complete) return [];

        var rows = await Terms(tail.AccountId, null, null, null, paidShop: shop).ToListAsync(ct);
        var byId = rows.Select(row => row.ToTerm()).ToDictionary(row => row.TermId);
        var links = await db.Database.SqlQuery<PredecessorLink>($"""
            SELECT f.term_id, f.predecessor_term_id FROM public.payment_fulfilments f
            WHERE f.account_id = {tail.AccountId} AND f.predecessor_term_id IS NOT NULL
                AND NOT EXISTS (SELECT FROM public.paid_fresh_starts x
                    WHERE x.account_id = f.account_id AND x.order_id = f.order_id AND x.kind = 'RestoredAccess')
            """).ToDictionaryAsync(link => link.TermId, link => link.PredecessorTermId, ct);
        var candidates = byId.Values.ToLookup(row => row.EndsAt);
        var periods = new List<TermFactsRow>();
        var seen = new HashSet<Guid> { tail.TermId };
        var successor = tail;
        while (true)
        {
            TermFactsRow? previous = null;
            if (links.TryGetValue(successor.TermId, out var linkedId) && byId.TryGetValue(linkedId, out var linked)
                && linked.EndsAt == successor.StartsAt && linked.PeriodOrdinal == successor.PeriodOrdinal - 1
                && SubscriptionStatusProjection.ToFacts(linked) is { Evidence: TermEvidenceState.Complete, Anchor: var linkedAnchor }
                && linkedAnchor == facts.Anchor && SubscriptionStatusProjection.ActiveCancellation(linked) is null)
            {
                previous = linked;
            }
            else if (!links.ContainsKey(successor.TermId) && candidates[successor.StartsAt].Where(row =>
                    row.PeriodOrdinal == successor.PeriodOrdinal - 1 && row.Capacity == successor.Capacity
                    && row.Mode == successor.Mode
                    && SubscriptionStatusProjection.ToFacts(row) is { Evidence: TermEvidenceState.Complete, Anchor: var anchor }
                    && anchor == facts.Anchor && SubscriptionStatusProjection.ActiveCancellation(row) is null)
                .Take(2).ToArray() is [var heuristic])
            {
                previous = heuristic;
            }
            if (previous is null || !seen.Add(previous.TermId)) break;
            periods.Add(previous);
            successor = previous;
        }
        return periods;
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

    private IQueryable<TermRow> Terms(Guid account, Guid? term, Guid? after, int? take, Guid? paidShop = null) =>
        db.Database.SqlQuery<TermRow>($"""
            SELECT t.term_id AS term_id, t.account_id AS account_id, CASE WHEN i.capacity>1 THEN 'Grouped' ELSE t.mode END AS mode, coalesce(i.capacity,t.capacity) AS capacity,
                t.starts_at AS starts_at, t.ends_at AS ends_at, f.basis AS basis, f.billing_period AS billing_period,
                f.anchor_local AS anchor_local, f.time_zone AS time_zone, f.period_ordinal AS period_ordinal,
                coalesce(i.members,f.member_allowance) AS member_allowance, coalesce(i.gross,f.price_paise) AS price_paise, f.currency AS currency,
                coalesce(i.plan_version,f.plan_version_id) AS plan_version_id, f.source_reference AS source_reference,
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
                    'Consistent', g.starts_at = t.starts_at AND g.ends_at = t.ends_at)::text END AS complimentary,
                EXISTS (SELECT FROM public.payment_fulfilments s WHERE s.account_id = t.account_id
                    AND s.predecessor_term_id = t.term_id AND NOT EXISTS (
                        SELECT FROM public.paid_fresh_starts f WHERE f.account_id = s.account_id AND f.order_id = s.order_id
                            AND f.kind = 'RestoredAccess')) AS renewed_at_end,
                rd.new_paid_through AS refund_paid_through, rd.new_access_ends_at AS refund_access_ends_at
            FROM public.subscription_coverage_terms t
            LEFT JOIN public.trial_history h ON h.term_id = t.term_id AND h.account_id = t.account_id
            LEFT JOIN public.shop_affiliations a ON a.shop_id = h.shop_id AND a.account_id = h.account_id
            LEFT JOIN LATERAL (SELECT ends_at FROM public.trial_extensions
                WHERE shop_id = h.shop_id AND account_id = h.account_id ORDER BY revision DESC LIMIT 1) e ON true
            LEFT JOIN LATERAL (SELECT capacity,members,gross,plan_version FROM public.capacity_increases
                WHERE account_id=t.account_id AND term_id=t.term_id AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
            LEFT JOIN public.subscription_term_facts f ON f.account_id = t.account_id AND f.term_id = t.term_id
            LEFT JOIN LATERAL public.refund_boundary(t.account_id, t.term_id) rd ON true
            LEFT JOIN public.complimentary_grants g ON g.term_id = t.term_id AND g.account_id = t.account_id
            LEFT JOIN LATERAL (SELECT * FROM public.scheduled_subscription_changes
                WHERE account_id = t.account_id AND term_id = t.term_id ORDER BY revision DESC LIMIT 1) c ON true
            WHERE t.account_id = {account} AND ({term}::uuid IS NULL OR t.term_id = {term}::uuid) AND ({after}::uuid IS NULL OR t.term_id > {after}::uuid)
                AND ({paidShop}::uuid IS NULL OR f.basis = 'Paid' AND EXISTS (
                    SELECT FROM public.shop_affiliations s
                    WHERE s.shop_id = {paidShop} AND s.account_id = t.account_id
                        AND (s.term_id = t.term_id OR EXISTS (SELECT FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = s.account_id AND l.shop_id = s.shop_id
                                AND l.affiliation_request_id = s.request_id AND l.term_id = t.term_id))))
            ORDER BY t.term_id LIMIT {take}
            """);

    private sealed record ShopBindingRow(Guid AffiliationAccountId, Guid RequestId, Guid? AffiliationTermId,
        Guid? TrialAccountId, Guid? TrialTermId, DateTimeOffset? ExtendedEndsAt,
        DateTimeOffset? ConvertedPaidStartsAt, string? Trial);

    private sealed record PaidLinkRow(Guid TermId, DateTimeOffset? CoverageFrom, Guid? SlotAssignmentId);

    private sealed record ShopTermRow(Guid TermId, Guid AccountId, string Mode, int Capacity,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal,
        string? TimeZone, int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency,
        Guid? PlanVersionId, string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion,
        string? Cancellation, bool RenewedAtEnd, DateTimeOffset? RefundPaidThrough, DateTimeOffset? RefundAccessEndsAt)
    {
        internal TermFactsRow ToTerm(string? trial, DateTimeOffset? extendedEndsAt) => new(TermId, AccountId, Mode, Capacity,
            StartsAt, EndsAt, Basis, BillingPeriod, AnchorLocal, TimeZone, PeriodOrdinal, MemberAllowance, PricePaise,
            Currency, PlanVersionId, SourceReference, DevelopmentFixture, CalendarVersion, AccessPolicyVersion,
            ParseTrial(trial, extendedEndsAt), Cancellation: ParseCancellation(Cancellation), RenewedAtEnd: RenewedAtEnd,
            RefundBoundary: Boundary(RefundPaidThrough, RefundAccessEndsAt));
    }

    private sealed record PredecessorLink(Guid TermId, Guid PredecessorTermId);

    private sealed record TermRow(Guid TermId, Guid AccountId, string Mode, int Capacity, DateTimeOffset StartsAt,
        DateTimeOffset EndsAt, string? Basis, string? BillingPeriod, DateTime? AnchorLocal, string? TimeZone,
        int? PeriodOrdinal, int? MemberAllowance, long? PricePaise, string? Currency, Guid? PlanVersionId,
        string? SourceReference, bool? DevelopmentFixture, string? CalendarVersion, string? AccessPolicyVersion, string? Trial,
        string? Complimentary, DateTimeOffset? ExtendedEndsAt, string? Cancellation, bool RenewedAtEnd,
        DateTimeOffset? RefundPaidThrough, DateTimeOffset? RefundAccessEndsAt)
    {
        internal TermFactsRow ToTerm() => new(TermId, AccountId, Mode, Capacity, StartsAt, EndsAt, Basis, BillingPeriod,
            AnchorLocal, TimeZone, PeriodOrdinal, MemberAllowance, PricePaise, Currency, PlanVersionId,
            SourceReference, DevelopmentFixture, CalendarVersion, AccessPolicyVersion, ParseTrial(Trial, ExtendedEndsAt), ParseGrant(Complimentary), ParseCancellation(Cancellation),
            RenewedAtEnd: RenewedAtEnd, RefundBoundary: Boundary(RefundPaidThrough, RefundAccessEndsAt));
    }

    private static RefundTermBoundary? Boundary(DateTimeOffset? paidThrough, DateTimeOffset? accessEndsAt) =>
        paidThrough is { } paid && accessEndsAt is { } ends ? new(paid.ToUniversalTime(), ends.ToUniversalTime()) : null;

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
