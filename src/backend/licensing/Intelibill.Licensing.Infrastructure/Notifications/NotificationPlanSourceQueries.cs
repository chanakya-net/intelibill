using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

/// <summary>
/// Shared SQL queries for reading effective notice-planning terms, shop bindings, trial conversion
/// exclusions, trial extension revisions, and active suspension flags inside an account-scoped transaction.
/// </summary>
internal static class NotificationPlanSourceQueries
{
    internal static Task<bool> IsAccountSuspendedAsync(
        LicensingDbContext db,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.active_operational_restrictions
                WHERE account_id = {accountId} AND shop_id IS NULL AND effective_at <= {now}
            ) AS "Value"
            """).SingleAsync(ct);

    internal static async Task<IReadOnlyList<TermPlanSource>> ReadEffectiveTermSourcesAsync(
        LicensingDbContext db,
        SubscriptionAccessFactsReader factsReader,
        Guid accountId,
        DateTimeOffset now,
        int termPageSize,
        Guid[]? filterTermIds,
        CancellationToken ct,
        Guid? displayActor = null)
    {
        var sources = new List<TermPlanSource>();
        Guid? after = null;
        while (true)
        {
            var ids = await ReadCandidateTermsAsync(db, accountId, now, after, termPageSize, filterTermIds, displayActor, ct);
            if (ids.Length == 0)
                break;
            sources.AddRange(await ReadTermsByIdAsync(db, factsReader, accountId, ids, now, ct));
            if (ids.Length < termPageSize)
                break;
            after = ids[^1];
        }
        return sources;
    }

    internal static async Task<IReadOnlyList<TermPlanSource>> ReadTermsByIdAsync(
        LicensingDbContext db,
        SubscriptionAccessFactsReader factsReader,
        Guid accountId,
        Guid[] ids,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var terms = await factsReader.ReadNoticeTermsAsync(accountId, ids, now, ct);
        var shops = await ReadTermShopsAsync(db, accountId, ids, now, ct);
        var revisions = await ReadTrialExtensionRevisionsAsync(db, accountId, ids, ct);
        return terms.Select(term => new TermPlanSource(
            term,
            revisions.GetValueOrDefault(term.TermId),
            shops[term.TermId].ToArray())).ToArray();
    }

    private static Task<Guid[]> ReadCandidateTermsAsync(
        LicensingDbContext db, Guid account, DateTimeOffset now, Guid? after, int take,
        Guid[]? selected, Guid? displayActor, CancellationToken ct) =>
        db.Database.SqlQuery<Guid>($"""
            SELECT t.term_id AS "Value"
            FROM public.subscription_coverage_terms t
            LEFT JOIN public.subscription_term_facts f USING (account_id, term_id)
            LEFT JOIN public.trial_history h USING (account_id, term_id)
            WHERE t.account_id = {account}
              AND ({selected}::uuid[] IS NULL OR t.term_id = ANY({selected}::uuid[]))
              AND ({after}::uuid IS NULL OR t.term_id > {after})
              AND ((f.basis = 'Paid' AND (
                        t.ends_at + interval '7 days' >= {now} OR
                        ({displayActor}::uuid IS NOT NULL AND EXISTS (
                            SELECT FROM public.effective_notification_jobs j WHERE j.account_id = t.account_id
                            AND j.source_id = t.term_id AND j.scope_kind = 'Account' AND j.recipient_user_id = {displayActor}
                            AND j.threshold = 'GraceExpiry')))
                    AND NOT EXISTS (
                        SELECT FROM public.payment_fulfilments pf WHERE pf.account_id = t.account_id
                        AND pf.predecessor_term_id = t.term_id AND pf.fulfilled_at <= {now}
                        AND NOT EXISTS (SELECT FROM public.paid_fresh_starts x WHERE x.account_id = pf.account_id
                            AND x.order_id = pf.order_id AND x.kind = 'RestoredAccess')))
                OR (h.term_id IS NOT NULL AND COALESCE((
                        SELECT e.ends_at FROM public.trial_extensions e WHERE e.account_id = h.account_id
                        AND e.shop_id = h.shop_id ORDER BY revision DESC LIMIT 1), t.ends_at) > {now}
                    AND NOT EXISTS (SELECT FROM public.payment_trial_conversions x WHERE x.account_id = t.account_id
                        AND x.trial_term_id = t.term_id AND x.paid_starts_at <= {now})))
            ORDER BY t.term_id
            LIMIT {take}
            """).ToArrayAsync(ct);

    internal static async Task<int> ReadTrialExtensionRevisionAsync(
        LicensingDbContext db,
        Guid accountId,
        Guid termId,
        CancellationToken ct)
    {
        var revisions = await ReadTrialExtensionRevisionsAsync(db, accountId, [termId], ct);
        return revisions.GetValueOrDefault(termId, 0);
    }

    private static async Task<ILookup<Guid, ShopPlanSource>> ReadTermShopsAsync(
        LicensingDbContext db,
        Guid accountId,
        Guid[] termIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<TermShopRow>($"""
            SELECT assignments.term_id, assignments.shop_id, assignments.affiliation_request_id,
                assignments.shop_name,
                EXISTS (
                    SELECT FROM public.active_operational_closure_targets ct
                    WHERE ct.account_id = {accountId}
                      AND ct.shop_id = assignments.shop_id
                      AND ct.affiliation_request_id = assignments.affiliation_request_id
                ) AS is_closed,
                EXISTS (
                    SELECT FROM public.active_operational_restrictions r
                    WHERE r.account_id = {accountId}
                      AND r.shop_id = assignments.shop_id
                      AND r.effective_at <= {now}
                ) AS is_suspended,
                pl.coverage_from,
                CASE WHEN pl.is_released THEN pl.coverage_ends_at END AS handoff_ends_at
            FROM (
                SELECT t.term_id, er.shop_id, er.affiliation_request_id, a.shop_name
                FROM unnest({termIds}) AS t(term_id)
                CROSS JOIN LATERAL public.term_effective_roster({accountId}, t.term_id, {now}) er
                JOIN public.current_shop_affiliations a
                    ON a.request_id = er.affiliation_request_id AND a.account_id = {accountId}
                UNION
                SELECT h.term_id, h.shop_id, a.request_id AS affiliation_request_id, a.shop_name
                FROM public.trial_history h
                JOIN public.current_shop_affiliations a
                    ON a.account_id = h.account_id AND a.shop_id = h.shop_id
                LEFT JOIN public.payment_trial_conversions x
                    ON x.account_id = h.account_id AND x.shop_id = h.shop_id
                WHERE h.account_id = {accountId}
                  AND h.term_id = ANY({termIds})
                  AND (x.paid_starts_at IS NULL OR x.paid_starts_at > {now})
                UNION
                SELECT l.term_id, l.shop_id, l.affiliation_request_id, a.shop_name
                FROM public.complimentary_grant_shops l
                JOIN public.current_shop_affiliations a
                    ON a.request_id = l.affiliation_request_id AND a.account_id = l.account_id
                WHERE l.account_id = {accountId} AND l.term_id = ANY({termIds})
            ) assignments
            LEFT JOIN LATERAL public.shop_effective_paid_link(
                {accountId}, assignments.shop_id, assignments.affiliation_request_id, {now}) pl
                ON pl.term_id = assignments.term_id
            ORDER BY assignments.term_id, assignments.shop_id
            """).ToListAsync(ct);
        return ToShopLookup(rows);
    }

    private static ILookup<Guid, ShopPlanSource> ToShopLookup(List<TermShopRow> rows) =>
        rows.ToLookup(
            row => row.TermId,
            row => new ShopPlanSource(
                row.ShopId,
                row.AffiliationRequestId,
                row.ShopName,
                row.IsClosed,
                row.IsSuspended,
                row.HandoffEndsAt?.ToUniversalTime(),
                row.CoverageFrom?.ToUniversalTime()));

    private static async Task<Dictionary<Guid, int>> ReadTrialExtensionRevisionsAsync(
        LicensingDbContext db,
        Guid accountId,
        Guid[] termIds,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<TrialExtensionRevisionRow>($"""
            SELECT h.term_id, COALESCE(MAX(e.revision), 0) AS revision
            FROM public.trial_history h
            LEFT JOIN public.trial_extensions e
                ON e.account_id = h.account_id AND e.shop_id = h.shop_id
            WHERE h.account_id = {accountId} AND h.term_id = ANY({termIds})
            GROUP BY h.term_id
            """).ToListAsync(ct);
        return rows.ToDictionary(row => row.TermId, row => row.Revision);
    }

    private sealed record TermShopRow(
        Guid TermId,
        Guid ShopId,
        Guid AffiliationRequestId,
        string ShopName,
        bool IsClosed,
        bool IsSuspended,
        DateTimeOffset? HandoffEndsAt,
        DateTimeOffset? CoverageFrom);

    private sealed record TrialExtensionRevisionRow(Guid TermId, int Revision);
}
