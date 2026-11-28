using System.Security.Cryptography;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class StandaloneProofEmail(
    LicensingDbContext db,
    NotificationEmailDeliveryStore store,
    NotificationDestinationProtector protector,
    IOptions<BillingEmailOptions> options,
    [FromKeyedServices(SubscriptionStatusRegistration.ClockKey)] TimeProvider businessClock,
    [FromKeyedServices(BillingNotificationWorker.LeaseClock)] TimeProvider leaseClock)
{
    private sealed record ProofRow(Guid ContactId, string ProtectedDestination, byte[] DestinationHash, string Locale,
        string ProtectedProof, byte[] VerificationTokenHash);

    internal async Task<MimeMessage?> PrepareAndMarkAsync(EmailLease lease, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await store.EnterAsync(lease.AccountId, leaseClock.GetUtcNow(), ct);
        await BillingAccountLock.TakeSharedAsync(db, lease.AccountId, ct);
        var rows = await db.Database.SqlQuery<ProofRow>($"""
            SELECT c.id AS contact_id, c.protected_destination, c.destination_hash, c.locale, d.protected_proof, c.verification_token_hash
            FROM public.notification_email_deliveries d
            JOIN public.billing_notice_contacts c ON c.id = d.proof_contact_id AND c.account_id = d.account_id
            WHERE d.account_id = {lease.AccountId} AND d.id = {lease.Id} AND c.merchant_user_id IS NULL
                AND c.verification_expires_at > {businessClock.GetUtcNow()} AND c.verification_token_hash IS NOT NULL
                AND c.revoked_at IS NULL AND c.verified_at IS NULL AND c.revision = 1
            FOR SHARE OF c
            """).ToListAsync(ct);
        if (rows is not [var proof])
            return null;
        var email = protector.UnprotectContactDestination(lease.AccountId, proof.ContactId, proof.ProtectedDestination);
        var token = protector.OpenProof(lease.AccountId, proof.ContactId, lease.Id, proof.ProtectedProof);
        if (email is null || token is null || !BillingEmailOptions.Mailbox(email)
            || !CryptographicOperations.FixedTimeEquals(NotificationDestinationProtector.HashEmail(email), proof.DestinationHash)
            || !CryptographicOperations.FixedTimeEquals(proof.VerificationTokenHash,
                NotificationDestinationProtector.HashProof(lease.AccountId, proof.ContactId, proof.DestinationHash, token)))
            return null;
        var fragment = $"account={lease.AccountId:D}&contact={proof.ContactId:D}&proof={Uri.EscapeDataString(token)}";
        var url = new Uri(new Uri(options.Value.VerificationOrigin), $"/billing-contact/verify?lang={proof.Locale}#{fragment}");
        var text = BillingEmailRenderer.ProofText(proof.Locale);
        if (!await store.BeginSendAsync(lease, ct))
            return null;
        await tx.CommitAsync(ct);
        return BillingEmailRenderer.Compose(options.Value, lease.Id, email, text.Subject, $"{text.Body}\n{url.AbsoluteUri}");
    }
}
