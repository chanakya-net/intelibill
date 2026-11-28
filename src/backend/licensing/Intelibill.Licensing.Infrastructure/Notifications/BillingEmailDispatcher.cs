using Intelibill.Licensing.Application.Notifications;
using System.Security.Cryptography;
using Intelibill.Shared.Contracts.V1;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class BillingEmailDispatcher(
    NotificationEmailDeliveryStore store,
    NotificationEmailAuthorization authorization,
    NotificationDestinationProtector protector,
    StandaloneProofEmail proofs,
    IOptions<BillingEmailOptions> options,
    BillingEmailTransport transport) : IBillingEmailDispatcher
{
    public async Task<bool> DeliverOneAsync(CancellationToken ct)
    {
        if (!transport.Enabled)
            return false;
        var claim = await store.ClaimAsync(ct);
        if (claim.Lease is not { } lease)
            return claim.Found;

        using var client = new SmtpClient { Timeout = 15_000 };
        if (!await transport.ConnectAsync(client, ct))
        {
            await store.FinishAsync(lease, "Leased", "RetryPending", ct);
            return true;
        }
        var job = await authorization.ReadAsync(lease, ct);
        if (job is null)
            await DeliverProofAsync(client, lease, ct);
        else
            await DeliverReminderAsync(client, lease, job, ct);
        return true;
    }

    private async Task DeliverProofAsync(SmtpClient client, EmailLease lease, CancellationToken ct)
    {
        using var message = await proofs.PrepareAndMarkAsync(lease, ct);
        if (message is null)
            await store.FinishAsync(lease, "Leased", "Suppressed", ct);
        else
            await RecordSubmissionAsync(client, lease, message, ct);
    }

    private async Task DeliverReminderAsync(SmtpClient client, EmailLease lease, ReminderEmail job, CancellationToken ct)
    {
        var email = protector.UnprotectJobDestination(job.AccountId, job.AffiliationRequestId is null ? "Account" : "Shop",
            job.ScopeId, job.RecipientKey, job.ProtectedDestination);
        if (email is null || !BillingEmailOptions.Mailbox(email)
            || !CryptographicOperations.FixedTimeEquals(NotificationDestinationProtector.HashEmail(email), job.DestinationHash))
        {
            await store.FinishAsync(lease, "Leased", "Failed", ct);
            return;
        }

        var observed = await authorization.ObserveAsync(job, email, ct);
        var audience = job.AffiliationRequestId is null
            ? LicenseNoticesV1.AudienceCommercialBillingRole : LicenseNoticesV1.AudienceShopOwnerOperational;
        using var message = BillingEmailRenderer.Reminder(options.Value, lease, job, email, audience);
        using var contact = BillingEmailRenderer.Reminder(options.Value, lease, job, email, LicenseNoticesV1.AudienceCommercialContact);
        if (message is null || contact is null)
        {
            await store.FinishAsync(lease, "Leased", "Failed", ct);
            return;
        }
        var decision = await authorization.DecideAndMarkAsync(lease, job, observed, ct);
        if (decision.State == "Sending")
        {
            var content = decision.Audience == LicenseNoticesV1.AudienceCommercialContact ? contact : message;
            await RecordSubmissionAsync(client, lease, content, ct);
        }
        else if (decision.State != "LeaseLost")
            await store.FinishAsync(lease, "Leased", decision.State, ct);
    }

    private async Task RecordSubmissionAsync(SmtpClient client, EmailLease lease, MimeKit.MimeMessage message, CancellationToken ct)
    {
        // Expiry recovers a crashed worker between the SMTP reply and this commit.
        var outcome = await BillingEmailTransport.SubmitAsync(client, message, ct);
        await store.FinishAsync(lease, "Sending", outcome, ct);
    }
}
