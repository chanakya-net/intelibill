using System.Data;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
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
        DateTimeOffset now,
        Guid? afterAccount,
        int take,
        CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT account_id AS "Value"
            FROM public.due_notification_planning({now}, {afterAccount}, {take})
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

        var isAccountSuspended = await NotificationPlanSourceQueries.IsAccountSuspendedAsync(db, accountId, now, ct);
        var members = await ReadActiveMembersAsync(accountId, ct);
        var contacts = await ReadEligibleContactsAsync(accountId, ct);
        var terms = await NotificationPlanSourceQueries.ReadEffectiveTermSourcesAsync(
            db,
            factsReader,
            accountId,
            now,
            Math.Clamp(termPageSize, 1, 100),
            filterTermIds: null,
            ct);
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

    private sealed record MemberRow(Guid Id, Guid MerchantUserId, int Role, int Revision);

    private sealed record ContactRow(
        Guid Id,
        Guid? MerchantUserId,
        byte[] DestinationHash,
        string ProtectedDestination,
        string DestinationHint,
        string Locale,
        int Revision);
}
