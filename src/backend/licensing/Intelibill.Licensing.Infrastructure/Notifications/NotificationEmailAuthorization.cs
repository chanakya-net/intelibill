using System.Security.Cryptography;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Shared.Web.Persistence;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed record ReminderEmail(
    Guid AccountId, Guid JobId, Guid ScopeId, Guid? RecipientUserId, string RecipientKey, Guid? AffiliationRequestId,
    string ProtectedDestination, byte[] DestinationHash, string Locale, string Purpose, string Threshold,
    DateTimeOffset Deadline, string Audience, Guid SourceId, string SourceKind, string SourceRevision,
    DateTimeOffset DueAt, string TemplateKey, long? PricePaise, string? Currency);

internal sealed record EmailDecision(
    string State, MimeKit.MimeMessage? Message = null, DateTimeOffset? WindowEnd = null, DateTimeOffset? LeaseEnd = null);
internal sealed record RecipientObservation(bool Available, bool SameMailbox, DateTimeOffset ValidUntil);

internal sealed class NotificationEmailAuthorization(
    LicensingDbContext db,
    NotificationEmailDeliveryStore store,
    IMerchantNoticeRecipientAuthority authority,
    ReminderSourceReader sources,
    TimeProvider transportClock,
    [FromKeyedServices(BillingNotificationWorker.LeaseClock)] TimeProvider leaseClock)
{
    internal async Task<ReminderEmail?> ReadAsync(EmailLease lease, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await store.EnterAsync(lease.AccountId, leaseClock.GetUtcNow(), ct);
        var rows = await db.Database.SqlQuery<ReminderEmail>($"""
            SELECT j.account_id, j.id AS job_id, j.scope_id, j.recipient_user_id, j.recipient_key, d.affiliation_request_id,
                j.protected_destination, j.destination_hash, j.locale, j.purpose, j.threshold,
                j.effective_deadline_at AS deadline, j.audience,
                j.source_id, j.source_kind, j.source_revision, j.due_at, j.template_key,
                (j.template_arguments->>'evidencedTermPricePaise')::bigint AS price_paise,
                j.template_arguments->>'evidencedTermCurrency' AS currency
            FROM public.notification_email_deliveries d
            JOIN public.effective_notification_jobs j ON j.id = d.job_id AND j.account_id = d.account_id
            WHERE d.account_id = {lease.AccountId} AND d.id = {lease.Id} AND d.lease_token = {lease.Token}
                AND 'Email' = ANY(j.allowed_channels)
            """).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return rows.SingleOrDefault();
    }

    internal async Task<RecipientObservation> ObserveAsync(ReminderEmail job, string email, CancellationToken ct)
    {
        if (job.RecipientUserId is not { } userId)
            return new(true, false, DateTimeOffset.MaxValue);

        var deadline = transportClock.GetUtcNow().AddSeconds(15);
        var shop = job.AffiliationRequestId.HasValue;
        Guid? cursor = null;
        do
        {
            if (transportClock.GetUtcNow() >= deadline)
                return new(false, false, deadline);
            var request = new NoticeRecipientAuthorityRequestV1(
                LicenseNoticesV1.SchemaVersion, Guid.NewGuid(), deadline, job.AccountId, job.AffiliationRequestId,
                shop ? job.ScopeId : null, shop ? [] : [userId], cursor, LicenseNoticesV1.MaxRecipientPageSize);
            var reply = await authority.InspectAsync(request, ct);
            if (reply is null)
                return new(false, false, deadline);
            var actor = (shop ? reply.ShopMembers : reply.Actors).SingleOrDefault(row => row.UserId == userId);
            if (!reply.AffiliationMatches || actor is not null)
            {
                var eligible = reply.AffiliationMatches && actor is { IsEmailVerified: true, IsLoginEnabled: true }
                    && (!shop || actor.ShopRole == "Owner")
                    && string.Equals(actor.VerifiedEmail?.Trim(), email, StringComparison.OrdinalIgnoreCase);
                return new(true, eligible, deadline);
            }
            cursor = reply.NextShopMemberCursor;
        } while (cursor.HasValue);
        return new(true, false, deadline);
    }

    internal async Task<EmailDecision> DecideAndMarkAsync(
        EmailLease lease,
        ReminderEmail job,
        RecipientObservation observation,
        string email,
        BillingEmailOptions options,
        CancellationToken ct)
    {
        if (!observation.Available)
            return new("RetryPending");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await store.EnterAsync(job.AccountId, leaseClock.GetUtcNow(), ct);
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        var member = await LockBillingRoleAsync(job, ct);
        var contact = await LockContactAsync(job, observation, ct);
        var audience = job.AffiliationRequestId is null
            ? CommercialAudience(member && observation.SameMailbox, contact, job)
            : !member && !contact && observation.SameMailbox && await HasAffiliationAsync(job, ct)
                ? LicenseNoticesV1.AudienceShopOwnerOperational : null;
        if (transportClock.GetUtcNow() >= observation.ValidUntil)
            return new("RetryPending");
        var relevance = audience is null ? new NoticeRelevance("RecipientIneligible") : await sources.ReadAsync(job, ct);
        if (relevance.Current is not { } current || audience is null)
        {
            var suppressed = await store.SuppressAsync(lease, relevance.Reason!, ct);
            await tx.CommitAsync(ct);
            return new(suppressed ? "Suppressed" : "LeaseLost");
        }
        var message = BillingEmailRenderer.Reminder(options, lease, job, email, audience, current, sources.Now);
        if (message is null)
            return new("Failed");
        var leaseStart = leaseClock.GetUtcNow();
        if (!await store.BeginSendAsync(lease, ct))
        {
            message.Dispose();
            return new("LeaseLost");
        }
        await tx.CommitAsync(ct);
        return new("Sending", message, relevance.ValidUntil, leaseStart.AddSeconds(60));
    }

    internal bool ReadyToSubmit(EmailDecision decision, RecipientObservation observation, DateTimeOffset due)
    {
        var now = sources.Now;
        return now >= due && now < decision.WindowEnd && transportClock.GetUtcNow() < observation.ValidUntil
            && leaseClock.GetUtcNow() < decision.LeaseEnd;
    }

    private static string? CommercialAudience(bool member, bool contact, ReminderEmail job)
    {
        if (member && job.Audience == LicenseNoticesV1.AudienceCommercialBillingRole)
            return LicenseNoticesV1.AudienceCommercialBillingRole;
        return contact ? LicenseNoticesV1.AudienceCommercialContact : null;
    }

    private async Task<bool> LockBillingRoleAsync(ReminderEmail job, CancellationToken ct)
    {
        if (job.RecipientUserId is not { } user)
            return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.billing_actor_id',{user.ToString("D")},true)", ct);
        var rows = await db.Database.SqlQuery<Guid>($"""
            SELECT merchant_user_id AS "Value" FROM public.billing_account_members
            WHERE account_id = {job.AccountId} AND merchant_user_id = {user} AND role IN (1,2) AND status = 1 FOR SHARE
            """).ToListAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.billing_actor_id','',true)", ct);
        return rows.Count == 1;
    }

    private async Task<bool> LockContactAsync(ReminderEmail job, RecipientObservation observation, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM public.billing_notice_contacts
            WHERE account_id = {job.AccountId} AND destination_hash = {job.DestinationHash}
                AND verified_at IS NOT NULL AND opted_in_at IS NOT NULL AND revoked_at IS NULL
                AND (merchant_user_id IS NULL OR (merchant_user_id = {job.RecipientUserId} AND {observation.SameMailbox}))
            FOR SHARE
            """).ToListAsync(ct);
        return rows.Count == 1;
    }

    private Task<bool> HasAffiliationAsync(ReminderEmail job, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.current_shop_affiliations WHERE account_id = {job.AccountId}
                AND shop_id = {job.ScopeId} AND request_id = {job.AffiliationRequestId}) AS "Value"
            """).SingleAsync(ct);
}
