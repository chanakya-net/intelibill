using Intelibill.Licensing.Application.Notifications;
using System.Security.Cryptography;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class BillingEmailDispatcher(
    LicensingDbContext db,
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

        var result = await SubmitCurrentReminderAsync(client, lease, job, email, ct);
        if (result.HasValue)
            await store.FinishAsync(lease, result.Value.From, result.Value.To, ct, result.Value.Reason);
    }

    private async Task<(string From, string To, string? Reason)?> SubmitCurrentReminderAsync(
        SmtpClient client,
        EmailLease lease,
        ReminderEmail job,
        string email,
        CancellationToken ct)
    {
        await using var fence = await BillingAccountLock.TryPinSharedAsync(db, lease.AccountId, ct);
        if (fence is null)
            return ("Leased", "RetryPending", null);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        EmailDecision decision;
        RecipientObservation observed;
        try
        {
            observed = await authorization.ObserveAsync(job, email, budget.Token);
            decision = await authorization.DecideAndMarkAsync(lease, job, observed, email, options.Value, budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested)
        {
            // If the Sending commit raced cancellation the Leased transition cannot win.
            return ("Leased", "RetryPending", null);
        }
        using var message = decision.Message;
        if (message is null)
            return decision.State is "Failed" or "RetryPending" ? ("Leased", decision.State, null) : null;
        if (!authorization.ReadyToSubmit(decision, observed, job.DueAt))
            return ("Sending", "RetryPending", "BeforeSubmission");
        return ("Sending", await BillingEmailTransport.SubmitAsync(client, message, ct), null);
    }

    private async Task RecordSubmissionAsync(SmtpClient client, EmailLease lease, MimeKit.MimeMessage message, CancellationToken ct)
    {
        // Expiry recovers a crashed worker between the SMTP reply and this commit.
        var outcome = await BillingEmailTransport.SubmitAsync(client, message, ct);
        await store.FinishAsync(lease, "Sending", outcome, ct);
    }
}
