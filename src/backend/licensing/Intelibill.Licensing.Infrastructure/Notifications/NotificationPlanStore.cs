using System.Data;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed partial class NotificationPlanStore(
    LicensingDbContext db,
    ISubscriptionAccessFactsReader factsReader,
    IClosureStore closures,
    NotificationDestinationProtector protector) : INotificationPlanStore
{
    public async Task<IReadOnlyList<Guid>> DiscoverAccountsAsync(
        Guid? afterAccount,
        int take,
        CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT account_id AS "Value"
            FROM public.due_notification_planning({afterAccount}, {take})
            """).ToListAsync(ct);

    public async Task<AccountPlanContext?> ReadAccountPlanContextAsync(
        Guid accountId,
        DateTimeOffset now,
        int termPageSize,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        await EnterAccountAsync(accountId, ct);

        var isAccountClosed = await closures.AccountClosedAsync(accountId, ct);
        if (isAccountClosed)
        {
            await tx.CommitAsync(ct);
            return new AccountPlanContext(accountId, true, false, [], [], []);
        }

        var isAccountSuspended = await IsAccountSuspendedAsync(accountId, now, ct);
        var members = await ReadActiveMembersAsync(accountId, ct);
        var contacts = await ReadEligibleContactsAsync(accountId, ct);
        var terms = await ReadAllTermSourcesAsync(accountId, now, Math.Clamp(termPageSize, 1, 100), ct);
        await tx.CommitAsync(ct);

        return new AccountPlanContext(
            accountId,
            IsAccountClosed: false,
            isAccountSuspended,
            members,
            contacts,
            terms);
    }

    private Task<int> EnterAccountAsync(Guid accountId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.billing_account_id', {accountId.ToString("D")}, true)",
            ct);

    private Task<bool> IsAccountSuspendedAsync(Guid accountId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.active_operational_restrictions
                WHERE account_id = {accountId} AND shop_id IS NULL AND effective_at <= {now}
            ) AS "Value"
            """).SingleAsync(ct);

    private async Task<IReadOnlyList<BillingMemberSnapshot>> ReadActiveMembersAsync(
        Guid accountId,
        CancellationToken ct)
    {
        const int pageSize = 100;
        var results = new List<BillingMemberSnapshot>();
        Guid? afterUser = null;
        while (true)
        {
            var rows = await db.Database.SqlQuery<MemberRow>($"""
                SELECT id, merchant_user_id, role, revision
                FROM public.account_notification_billing_members({accountId}, {afterUser}, {pageSize})
                """).ToListAsync(ct);
            if (rows.Count == 0)
                break;

            foreach (var row in rows)
            {
                results.Add(new BillingMemberSnapshot(
                    row.Id,
                    row.MerchantUserId,
                    (BillingAccountRole)row.Role,
                    row.Revision));
            }

            afterUser = rows[^1].MerchantUserId;
            if (rows.Count < pageSize)
                break;
        }

        return results;
    }

    private async Task<IReadOnlyList<BillingContactSnapshot>> ReadEligibleContactsAsync(
        Guid accountId,
        CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ContactRow>($"""
            SELECT id, merchant_user_id, destination_hash, protected_destination, destination_hint, locale, revision
            FROM public.billing_notice_contacts
            WHERE account_id = {accountId}
              AND verified_at IS NOT NULL
              AND opted_in_at IS NOT NULL
              AND revoked_at IS NULL
            ORDER BY id
            """).ToListAsync(ct);

        var list = new List<BillingContactSnapshot>(rows.Count);
        foreach (var row in rows)
        {
            var email = protector.UnprotectContactDestination(accountId, row.Id, row.ProtectedDestination);
            if (string.IsNullOrWhiteSpace(email))
                continue;

            list.Add(new BillingContactSnapshot(
                row.Id,
                row.MerchantUserId,
                row.DestinationHash,
                email,
                row.DestinationHint,
                row.Locale,
                row.Revision));
        }

        return list;
    }

    private async Task<IReadOnlyList<TermPlanSource>> ReadAllTermSourcesAsync(
        Guid accountId,
        DateTimeOffset now,
        int termPageSize,
        CancellationToken ct)
    {
        var sources = new List<TermPlanSource>();
        Guid? afterTerm = null;
        while (true)
        {
            var page = await factsReader.ReadAccountAsync(accountId, afterTerm, termPageSize, ct);
            if (page.Count == 0)
                break;

            var termIds = page.Select(item => item.Term.TermId).ToArray();
            var shopLookup = await ReadTermShopsAsync(accountId, termIds, now, ct);
            var trialRevisions = await ReadTrialExtensionRevisionsAsync(accountId, termIds, ct);

            foreach (var row in page)
            {
                var shops = shopLookup[row.Term.TermId].ToArray();
                var extRev = trialRevisions.GetValueOrDefault(row.Term.TermId, 0);
                sources.Add(new TermPlanSource(row.Term, extRev, shops));
            }

            afterTerm = page[^1].Term.TermId;
            if (page.Count < termPageSize)
                break;
        }

        return sources;
    }

    private async Task<ILookup<Guid, ShopPlanSource>> ReadTermShopsAsync(
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
                WHERE h.account_id = {accountId} AND h.term_id = ANY({termIds})
                UNION
                SELECT l.term_id, l.shop_id, l.affiliation_request_id, a.shop_name
                FROM public.complimentary_grant_shops l
                JOIN public.current_shop_affiliations a
                    ON a.request_id = l.affiliation_request_id AND a.account_id = l.account_id
                WHERE l.account_id = {accountId} AND l.term_id = ANY({termIds})
            ) assignments
            LEFT JOIN LATERAL public.shop_effective_paid_link(
                {accountId},
                assignments.shop_id,
                assignments.affiliation_request_id,
                {now}) pl ON pl.term_id = assignments.term_id
            ORDER BY assignments.term_id, assignments.shop_id
            """).ToListAsync(ct);

        return rows.ToLookup(
            row => row.TermId,
            row => new ShopPlanSource(
                row.ShopId,
                row.AffiliationRequestId,
                row.ShopName,
                row.IsClosed,
                row.IsSuspended,
                row.HandoffEndsAt?.ToUniversalTime()));
    }

    private async Task<Dictionary<Guid, int>> ReadTrialExtensionRevisionsAsync(
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

    private sealed record MemberRow(Guid Id, Guid MerchantUserId, int Role, int Revision);

    private sealed record ContactRow(
        Guid Id,
        Guid? MerchantUserId,
        byte[] DestinationHash,
        string ProtectedDestination,
        string DestinationHint,
        string Locale,
        int Revision);

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
