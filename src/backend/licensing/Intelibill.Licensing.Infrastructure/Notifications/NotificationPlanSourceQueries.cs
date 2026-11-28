using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Infrastructure.Data;
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
        ISubscriptionAccessFactsReader factsReader,
        Guid accountId,
        DateTimeOffset now,
        int termPageSize,
        Guid[]? filterTermIds,
        CancellationToken ct)
    {
        var filterSet = filterTermIds is { Length: > 0 } ? filterTermIds.ToHashSet() : null;
        var sources = new List<TermPlanSource>();
        Guid? afterTerm = null;
        while (true)
        {
            var page = await factsReader.ReadAccountAsync(accountId, afterTerm, termPageSize, ct);
            if (page.Count == 0)
                break;

            var rows = filterSet is null
                ? page
                : page.Where(item => filterSet.Contains(item.Term.TermId)).ToArray();
            if (rows.Count > 0)
                await AppendEffectiveTermsAsync(db, accountId, rows, now, sources, ct);

            afterTerm = page[^1].Term.TermId;
            if (page.Count < termPageSize)
                break;
        }

        return sources;
    }

    internal static async Task<int> ReadTrialExtensionRevisionAsync(
        LicensingDbContext db,
        Guid accountId,
        Guid termId,
        CancellationToken ct)
    {
        var revisions = await ReadTrialExtensionRevisionsAsync(db, accountId, [termId], ct);
        return revisions.GetValueOrDefault(termId, 0);
    }

    private static async Task AppendEffectiveTermsAsync(
        LicensingDbContext db,
        Guid accountId,
        IReadOnlyList<AccountTermRow> rows,
        DateTimeOffset now,
        List<TermPlanSource> target,
        CancellationToken ct)
    {
        var termIds = rows.Select(item => item.Term.TermId).ToArray();
        var convertedTrials = await ReadConvertedTrialTermIdsAsync(db, accountId, termIds, now, ct);
        var shopLookup = await ReadTermShopsAsync(db, accountId, termIds, now, ct);
        var trialRevisions = await ReadTrialExtensionRevisionsAsync(db, accountId, termIds, ct);

        foreach (var row in rows)
        {
            if (row.Term.Trial is not null && convertedTrials.Contains(row.Term.TermId))
                continue;

            var shops = shopLookup[row.Term.TermId].ToArray();
            var extRev = trialRevisions.GetValueOrDefault(row.Term.TermId, 0);
            target.Add(new TermPlanSource(row.Term, extRev, shops));
        }
    }

    private static async Task<HashSet<Guid>> ReadConvertedTrialTermIdsAsync(
        LicensingDbContext db,
        Guid accountId,
        Guid[] termIds,
        DateTimeOffset now,
        CancellationToken ct) =>
        (await db.Database.SqlQuery<Guid>($"""
            SELECT trial_term_id AS "Value"
            FROM public.payment_trial_conversions
            WHERE account_id = {accountId}
              AND trial_term_id = ANY({termIds})
              AND paid_starts_at <= {now}
            """).ToListAsync(ct)).ToHashSet();

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
                row.HandoffEndsAt?.ToUniversalTime()));

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
        DateTimeOffset? HandoffEndsAt);

    private sealed record TrialExtensionRevisionRow(Guid TermId, int Revision);
}
