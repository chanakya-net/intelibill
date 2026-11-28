using System.Data;
using Intelibill.Licensing.Application.Status;
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
        // Serialize alias resolution across replicas after shared authority locks, including the first channel addition.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"notice:" + accountId.ToString("D")}, 0))", ct);
        var decisionAt = SubscriptionStatusProjection.EvaluationInstant(decisionClock);
        var liveFacts = await LoadLiveCommitPreconditionsAsync(accountId, sourceIds, decisionAt, plannedAt, ct);
        var existing = await LoadExistingKeysAsync(accountId, sourceIds, ct);

        var (inserted, duplicates, authorityChanged) = await CommitOrderedWritesAsync(
            decisionAt,
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
        DateTimeOffset decisionAt,
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

            var resolved = existing.Find(write, out var emailTaken);
            if (resolved is not null)
            {
                var updated = await AppendMissingChannelAsync(resolved, write, emailTaken, decisionAt, ct);
                if (updated is not null)
                    existing.Track(updated);
                duplicates++;
                continue;
            }

            var candidate = emailTaken ? InAppOnly(write) : write;
            if (candidate is not null && await TryInsertJobAsync(decisionAt, candidate, ct) is { } id)
            {
                existing.Track(new ExistingJobRow(id, candidate.ScopeKind, candidate.ScopeId, candidate.RecipientKey,
                    candidate.RecipientUserId, candidate.Purpose, candidate.SourceKind, candidate.SourceId, candidate.SourceRevision,
                    candidate.Threshold, candidate.EmailDestinationHash, candidate.AllowedChannels));
                inserted++;
            }
            else
                duplicates++;
        }

        return (inserted, duplicates, authorityChanged);
    }

    private static PlannedNotificationWrite? InAppOnly(PlannedNotificationWrite write) =>
        write.AllowedChannels.Contains(LicenseNoticesV1.ChannelInApp)
            ? write with
            {
                AllowedChannels = [LicenseNoticesV1.ChannelInApp],
                EmailDestination = null,
                EmailDestinationHash = null,
                DestinationHint = null,
            }
            : null;

    private async Task<ExistingJobRow?> AppendMissingChannelAsync(
        ExistingJobRow job, PlannedNotificationWrite write, bool emailTaken, DateTimeOffset decisionAt, CancellationToken ct)
    {
        if (write.RecipientUserId is not { } user || write.ExpectedMemberRevision is not { } revision)
            return null;
        var channel = write.AllowedChannels.Except(job.AllowedChannels).SingleOrDefault();
        if (channel is null || (channel == LicenseNoticesV1.ChannelEmail && emailTaken))
            return null;

        var email = channel == LicenseNoticesV1.ChannelEmail;
        var ciphertext = email ? protector.ProtectJobDestination(
            write.AccountId, write.ScopeKind, write.ScopeId, job.RecipientKey, write.EmailDestination!) : null;
        var hash = email ? write.EmailDestinationHash : null;
        var hint = email ? write.DestinationHint : null;
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.notification_channel_additions
                (account_id, job_id, channel, recipient_user_id, authority_revision, destination_hash,
                    protected_destination, destination_hint, recorded_at)
            VALUES ({write.AccountId}, {job.Id}, {channel}, {user}, {revision}, {hash},
                {ciphertext}, {hint}, {decisionAt})
            ON CONFLICT (job_id) DO NOTHING
            """, ct);
        if (rows != 1)
            return null;
        if (email)
            await EnqueueEmailAsync(job.Id, write, ct);
        return job with
        {
            AllowedChannels = [LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail],
            RecipientUserId = user,
            DestinationHash = job.DestinationHash ?? hash,
        };
    }

    private async Task<Guid?> TryInsertJobAsync(
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
        if (rows != 1)
            return null;
        if (write.AllowedChannels.Contains(LicenseNoticesV1.ChannelEmail))
            await EnqueueEmailAsync(jobId, write, ct);
        return jobId;
    }

    private Task<int> EnqueueEmailAsync(Guid jobId, PlannedNotificationWrite write, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.notification_email_deliveries
                (id, account_id, job_id, affiliation_request_id, due_at, state, attempts, next_attempt_at)
            VALUES ({Guid.NewGuid()}, {write.AccountId}, {jobId}, {write.ExpectedAffiliationRequestId},
                {write.DueAt}, 'Pending', 0, '-infinity')
            """, ct);

    private static IEnumerable<PlannedNotificationWrite> OrderDeterministic(
        IReadOnlyList<PlannedNotificationWrite> writes) =>
        writes
            .OrderBy(write => write.ScopeKind, StringComparer.Ordinal)
            .ThenBy(write => write.ScopeId)
            .ThenBy(write => write.RecipientKey, StringComparer.Ordinal)
            .ThenBy(write => write.Purpose, StringComparer.Ordinal)
            .ThenBy(write => write.SourceId)
            .ThenBy(write => write.Threshold, StringComparer.Ordinal);

    private sealed record NoticeKey(string Scope, Guid ScopeId, string Recipient, string Purpose, string Kind, Guid Source,
        string Revision, string Threshold);

    private sealed class ExistingJobSets
    {
        private readonly Dictionary<NoticeKey, ExistingJobRow> _jobs = [];

        public ExistingJobSets(List<ExistingJobRow> rows)
        {
            foreach (var job in rows)
                Track(job);
        }

        public void Track(ExistingJobRow job)
        {
            var key = new NoticeKey(job.ScopeKind, job.ScopeId, job.RecipientKey, job.Purpose,
                job.SourceKind, job.SourceId, job.SourceRevision, job.Threshold);
            _jobs[key] = job;
            if (job.RecipientUserId is { } user)
                _jobs[key with { Recipient = NotificationPlanner.UserRecipientKey(user) }] = job;
            if (job.DestinationHash is { } hash)
                _jobs[key with { Recipient = "email:" + Convert.ToHexStringLower(hash) }] = job;
        }

        public ExistingJobRow? Find(PlannedNotificationWrite write, out bool emailTaken)
        {
            var key = new NoticeKey(write.ScopeKind, write.ScopeId, write.RecipientKey, write.Purpose,
                write.SourceKind, write.SourceId, write.SourceRevision, write.Threshold);
            var job = _jobs.GetValueOrDefault(key);
            var alias = write.EmailDestinationHash is { } hash
                ? _jobs.GetValueOrDefault(key with { Recipient = "email:" + Convert.ToHexStringLower(hash) })
                : null;
            emailTaken = alias is not null && alias.Id != job?.Id;
            if (job is not null || alias is null)
                return job;
            return write.RecipientUserId is null || alias.RecipientUserId is null || write.RecipientUserId == alias.RecipientUserId
                ? alias : null;
        }
    }
}
