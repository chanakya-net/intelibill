using System.Data;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Infrastructure.Billing;
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
        // Shared lock aligns with canonical account lock order: shares with concurrent planners
        // while fencing against exclusive account lifecycle writers.
        await BillingAccountLock.TakeSharedAsync(db, accountId, ct);

        if (await closures.AccountClosedAsync(accountId, ct))
        {
            await tx.CommitAsync(ct);
            return new NotificationPlanCommitResult(0, 0, writes.Count);
        }

        var validMembers = await LockActiveMembersAsync(accountId, writes, ct);
        var validContacts = await LockEligibleContactsAsync(accountId, writes, ct);
        var sourceIds = writes.Select(write => write.SourceId).Distinct().ToArray();
        var liveFacts = await LoadLiveCommitPreconditionsAsync(accountId, sourceIds, plannedAt, ct);
        var existing = await LoadExistingKeysAsync(accountId, sourceIds, ct);

        var (inserted, duplicates, authorityChanged) = await CommitOrderedWritesAsync(
            plannedAt,
            writes,
            validMembers,
            validContacts,
            liveFacts,
            existing,
            ct);

        await tx.CommitAsync(ct);
        return new NotificationPlanCommitResult(inserted, duplicates, authorityChanged);
    }

    private async Task<(int Inserted, int Duplicates, int AuthorityChanged)> CommitOrderedWritesAsync(
        DateTimeOffset plannedAt,
        IReadOnlyList<PlannedNotificationWrite> writes,
        Dictionary<Guid, int> validMembers,
        Dictionary<Guid, int> validContacts,
        LiveCommitSnapshot liveFacts,
        ExistingJobSets existing,
        CancellationToken ct)
    {
        var inserted = 0;
        var duplicates = 0;
        var authorityChanged = 0;

        foreach (var write in OrderDeterministic(writes))
        {
            if (!IsStillValid(write, validMembers, validContacts, liveFacts))
            {
                authorityChanged++;
                continue;
            }

            if (existing.SemanticKeys.Contains(SemanticKeyOf(write)))
            {
                duplicates++;
                continue;
            }

            var resolved = ApplyEmailAliasDeduplication(write, existing);
            if (resolved is null)
            {
                duplicates++;
                continue;
            }

            if (await InsertWithAliasFallbackAsync(plannedAt, resolved, existing, ct))
                inserted++;
            else
                duplicates++;
        }

        return (inserted, duplicates, authorityChanged);
    }

    private static PlannedNotificationWrite? ApplyEmailAliasDeduplication(
        PlannedNotificationWrite write,
        ExistingJobSets existing)
    {
        if (write.EmailDestinationHash is null)
            return write;

        if (!existing.EmailKeys.Contains(EmailKeyOf(write)))
            return write;

        if (write.Audience == LicenseNoticesV1.AudienceCommercialContact)
            return null;

        return write.Audience == LicenseNoticesV1.AudienceCommercialBillingRole
            ? StripEmailChannel(write)
            : write;
    }

    private async Task<bool> InsertWithAliasFallbackAsync(
        DateTimeOffset plannedAt,
        PlannedNotificationWrite write,
        ExistingJobSets existing,
        CancellationToken ct)
    {
        if (await TryInsertJobAsync(plannedAt, write, ct))
        {
            existing.TrackInserted(write);
            return true;
        }

        if (write.Audience == LicenseNoticesV1.AudienceCommercialBillingRole
            && write.EmailDestinationHash is not null
            && !await SemanticKeyExistsAsync(write, ct))
        {
            var inAppOnly = StripEmailChannel(write);
            if (await TryInsertJobAsync(plannedAt, inAppOnly, ct))
            {
                existing.TrackInserted(inAppOnly);
                return true;
            }
        }

        return false;
    }

    private static PlannedNotificationWrite StripEmailChannel(PlannedNotificationWrite write) =>
        write with
        {
            AllowedChannels = [LicenseNoticesV1.ChannelInApp],
            EmailDestination = null,
            EmailDestinationHash = null,
            DestinationHint = null,
        };

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
                status, template_key, template_arguments, destination_hash, protected_destination,
                destination_hint, planned_at, created_at)
            VALUES (
                {jobId}, {write.AccountId}, {write.ScopeKind}, {write.ScopeId}, {write.RecipientKey},
                {write.RecipientUserId}, {write.ContactId}, {write.Audience}, {write.Purpose},
                {write.SourceKind}, {write.SourceId}, {write.SourceRevision}, {write.Threshold},
                {write.DueAt}, {write.EffectiveDeadlineAt}, {BillingCalendar.Zone}, {write.Locale},
                {write.AllowedChannels}, {LicenseNoticesV1.StatusScheduled}, {write.TemplateKey},
                CAST({write.TemplateArgumentsJson} AS jsonb), {write.EmailDestinationHash}, {protectedDestination},
                {write.DestinationHint}, {plannedAt}, {plannedAt})
            ON CONFLICT DO NOTHING
            """, ct);
        return rows == 1;
    }

    private Task<bool> SemanticKeyExistsAsync(PlannedNotificationWrite write, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM public.notification_jobs
                WHERE account_id = {write.AccountId}
                  AND scope_kind = {write.ScopeKind}
                  AND scope_id = {write.ScopeId}
                  AND recipient_key = {write.RecipientKey}
                  AND purpose = {write.Purpose}
                  AND source_kind = {write.SourceKind}
                  AND source_id = {write.SourceId}
                  AND source_revision = {write.SourceRevision}
                  AND threshold = {write.Threshold}
            ) AS "Value"
            """).SingleAsync(ct);

    private static SemanticTuple SemanticKeyOf(PlannedNotificationWrite write) => new(
        write.ScopeKind,
        write.ScopeId,
        write.RecipientKey,
        write.Purpose,
        write.SourceKind,
        write.SourceId,
        write.SourceRevision,
        write.Threshold);

    private static EmailTuple EmailKeyOf(PlannedNotificationWrite write) => new(
        write.ScopeKind,
        write.ScopeId,
        Convert.ToHexStringLower(write.EmailDestinationHash!),
        write.Purpose,
        write.SourceKind,
        write.SourceId,
        write.SourceRevision,
        write.Threshold);

    private static IEnumerable<PlannedNotificationWrite> OrderDeterministic(
        IReadOnlyList<PlannedNotificationWrite> writes) =>
        writes
            .OrderBy(write => write.ScopeKind, StringComparer.Ordinal)
            .ThenBy(write => write.ScopeId)
            .ThenBy(write => write.RecipientKey, StringComparer.Ordinal)
            .ThenBy(write => write.Purpose, StringComparer.Ordinal)
            .ThenBy(write => write.SourceId)
            .ThenBy(write => write.Threshold, StringComparer.Ordinal);

    private sealed record SemanticTuple(
        string ScopeKind,
        Guid ScopeId,
        string RecipientKey,
        string Purpose,
        string SourceKind,
        Guid SourceId,
        string SourceRevision,
        string Threshold);

    private sealed record EmailTuple(
        string ScopeKind,
        Guid ScopeId,
        string DestinationHashHex,
        string Purpose,
        string SourceKind,
        Guid SourceId,
        string SourceRevision,
        string Threshold);

    private sealed class ExistingJobSets(HashSet<SemanticTuple> semanticKeys, HashSet<EmailTuple> emailKeys)
    {
        public HashSet<SemanticTuple> SemanticKeys { get; } = semanticKeys;
        public HashSet<EmailTuple> EmailKeys { get; } = emailKeys;

        public void TrackInserted(PlannedNotificationWrite write)
        {
            SemanticKeys.Add(SemanticKeyOf(write));
            if (write.EmailDestinationHash is not null)
                EmailKeys.Add(EmailKeyOf(write));
        }
    }
}
