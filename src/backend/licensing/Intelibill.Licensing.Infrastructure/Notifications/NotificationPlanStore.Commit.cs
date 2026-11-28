using System.Data;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed partial class NotificationPlanStore
{
    public async Task<NotificationPlanCommitResult> CommitPlannedJobsAsync(
        Guid accountId,
        DateTimeOffset plannedAt,
        IReadOnlyList<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        await EnterAccountAsync(accountId, ct);

        if (await closures.AccountClosedAsync(accountId, ct))
        {
            await tx.CommitAsync(ct);
            return new NotificationPlanCommitResult(0, 0, writes.Count);
        }

        var validMembers = await LockActiveMembersAsync(accountId, writes, ct);
        var validContacts = await LockEligibleContactsAsync(accountId, writes, ct);
        var activeShopAffiliations = await ReadActiveShopIdsAsync(accountId, ct);
        var closedShops = await ReadClosedShopIdsAsync(accountId, ct);

        var inserted = 0;
        var duplicates = 0;
        var authorityChanged = 0;

        foreach (var write in OrderDeterministic(writes))
        {
            if (!IsAuthorityStillValid(write, validMembers, validContacts, activeShopAffiliations, closedShops))
            {
                authorityChanged++;
                continue;
            }

            var insertedRow = await TryInsertJobAsync(plannedAt, write, ct);
            if (insertedRow)
                inserted++;
            else
                duplicates++;
        }

        await tx.CommitAsync(ct);
        return new NotificationPlanCommitResult(inserted, duplicates, authorityChanged);
    }

    private async Task<bool> TryInsertJobAsync(
        DateTimeOffset plannedAt,
        PlannedNotificationWrite write,
        CancellationToken ct)
    {
        var protectedDestination = write.EmailDestination is not null
            ? protector.ProtectJobDestination(
                write.AccountId,
                write.ScopeKind,
                write.ScopeId,
                write.RecipientKey,
                write.EmailDestination)
            : null;
        var jobId = Guid.CreateVersion7(plannedAt);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.notification_jobs (
                id, account_id, scope_kind, scope_id, recipient_key, recipient_user_id, contact_id,
                audience, purpose, source_kind, source_id, source_revision, threshold,
                due_at, effective_deadline_at, time_zone, locale, allowed_channels,
                status, template_key, template_arguments, protected_destination, destination_hint,
                planned_at, created_at)
            VALUES (
                {jobId}, {write.AccountId}, {write.ScopeKind}, {write.ScopeId}, {write.RecipientKey},
                {write.RecipientUserId}, {write.ContactId}, {write.Audience}, {write.Purpose},
                {write.SourceKind}, {write.SourceId}, {write.SourceRevision}, {write.Threshold},
                {write.DueAt}, {write.EffectiveDeadlineAt}, {BillingCalendar.Zone}, {write.Locale},
                {write.AllowedChannels}, {LicenseNoticesV1.StatusScheduled}, {write.TemplateKey},
                CAST({write.TemplateArgumentsJson} AS jsonb), {protectedDestination}, {write.DestinationHint},
                {plannedAt}, {plannedAt})
            ON CONFLICT (
                account_id, scope_kind, scope_id, recipient_key,
                purpose, source_kind, source_id, source_revision, threshold)
            DO NOTHING
            """, ct);
        return rows == 1;
    }

    private async Task<Dictionary<Guid, int>> LockActiveMembersAsync(
        Guid accountId,
        IReadOnlyList<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        var userIds = writes
            .Where(write => write.Audience == LicenseNoticesV1.AudienceCommercialBillingRole && write.RecipientUserId is not null)
            .Select(write => write.RecipientUserId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var active = new Dictionary<Guid, int>();
        foreach (var userId in userIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_actor_id', {userId.ToString("D")}, true)",
                ct);
            var rows = await db.Database.SqlQuery<MemberLockRow>($"""
                SELECT merchant_user_id, revision
                FROM public.billing_account_members
                WHERE account_id = {accountId}
                  AND merchant_user_id = {userId}
                  AND status = {(int)BillingAccountMemberStatus.Active}
                  AND role IN ({(int)BillingAccountRole.Owner}, {(int)BillingAccountRole.Manager})
                FOR SHARE
                """).ToListAsync(ct);
            if (rows is [var found])
                active[found.MerchantUserId] = found.Revision;
        }

        if (userIds.Length > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.billing_actor_id', {string.Empty}, true)",
                ct);
        }

        return active;
    }

    private async Task<Dictionary<Guid, int>> LockEligibleContactsAsync(
        Guid accountId,
        IReadOnlyList<PlannedNotificationWrite> writes,
        CancellationToken ct)
    {
        var contactIds = writes
            .Where(write => write.Audience == LicenseNoticesV1.AudienceCommercialContact && write.ContactId is not null)
            .Select(write => write.ContactId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var eligible = new Dictionary<Guid, int>();
        foreach (var contactId in contactIds)
        {
            var rows = await db.Database.SqlQuery<ContactLockRow>($"""
                SELECT id, revision
                FROM public.billing_notice_contacts
                WHERE account_id = {accountId}
                  AND id = {contactId}
                  AND verified_at IS NOT NULL
                  AND opted_in_at IS NOT NULL
                  AND revoked_at IS NULL
                FOR SHARE
                """).ToListAsync(ct);
            if (rows is [var found])
                eligible[found.Id] = found.Revision;
        }

        return eligible;
    }

    private async Task<HashSet<Guid>> ReadActiveShopIdsAsync(Guid accountId, CancellationToken ct) =>
        (await db.Database.SqlQuery<Guid>($"""
            SELECT shop_id AS "Value"
            FROM public.current_shop_affiliations
            WHERE account_id = {accountId}
            """).ToListAsync(ct)).ToHashSet();

    private async Task<HashSet<Guid>> ReadClosedShopIdsAsync(Guid accountId, CancellationToken ct) =>
        (await db.Database.SqlQuery<Guid>($"""
            SELECT ct.shop_id AS "Value"
            FROM public.active_operational_closure_targets ct
            JOIN public.current_shop_affiliations a
                ON a.account_id = ct.account_id
                AND a.shop_id = ct.shop_id
                AND a.request_id = ct.affiliation_request_id
            WHERE ct.account_id = {accountId}
            """).ToListAsync(ct)).ToHashSet();

    private static bool IsAuthorityStillValid(
        PlannedNotificationWrite write,
        Dictionary<Guid, int> validMembers,
        Dictionary<Guid, int> validContacts,
        HashSet<Guid> activeShops,
        HashSet<Guid> closedShops)
    {
        if (write.Audience == LicenseNoticesV1.AudienceCommercialBillingRole)
        {
            return write.RecipientUserId is { } userId
                && validMembers.TryGetValue(userId, out var revision)
                && revision == write.ExpectedMemberRevision;
        }

        if (write.Audience == LicenseNoticesV1.AudienceCommercialContact)
        {
            return write.ContactId is { } contactId
                && validContacts.TryGetValue(contactId, out var revision)
                && revision == write.ExpectedContactRevision;
        }

        return activeShops.Contains(write.ScopeId) && !closedShops.Contains(write.ScopeId);
    }

    private static IEnumerable<PlannedNotificationWrite> OrderDeterministic(
        IReadOnlyList<PlannedNotificationWrite> writes) =>
        writes
            .OrderBy(write => write.ScopeKind, StringComparer.Ordinal)
            .ThenBy(write => write.ScopeId)
            .ThenBy(write => write.RecipientKey, StringComparer.Ordinal)
            .ThenBy(write => write.Purpose, StringComparer.Ordinal)
            .ThenBy(write => write.SourceId)
            .ThenBy(write => write.Threshold, StringComparer.Ordinal);

    private sealed record MemberLockRow(Guid MerchantUserId, int Revision);

    private sealed record ContactLockRow(Guid Id, int Revision);
}
