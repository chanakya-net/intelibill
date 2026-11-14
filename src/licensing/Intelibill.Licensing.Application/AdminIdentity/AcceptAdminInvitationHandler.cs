using ErrorOr;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

/// <summary>
/// Turns one open invitation into a verified, MFA-pending administrator. The result is the same
/// enrollment-only credential the first owner receives: no business session and no capability.
/// </summary>
public sealed class AcceptAdminInvitationHandler(IAdminInvitationStore store, IBootstrapSecrets secrets, TimeProvider clock)
{
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(15);

    private static Error Invalid => Error.Validation("Invitation.Invalid", "Check the invitation link, your email and the password requirements.");

    private static Error Unavailable => Error.Conflict("Invitation.Unavailable", "This invitation has expired or has already been used.");

    public async Task<ErrorOr<EnrollmentGrant>> AcceptAsync(
        string? token, string? email, string? password, CancellationToken cancellationToken)
    {
        if (!BootstrapValidation.IsInvitationValid(token) || !BootstrapValidation.IsPasswordValid(password))
            return Invalid;

        var digest = secrets.HashInvitation(token!);
        var invitedEmail = await store.FindInvitedEmailAsync(digest, cancellationToken);
        if (invitedEmail is null) return Invalid;

        // Hash before taking locks; the clock is read again under the lock.
        var passwordHash = secrets.HashPassword(password!);
        var claimed = BootstrapValidation.NormalizeEmail(email);
        return await store.InTransactionAsync(async transaction =>
        {
            await store.LockEmailAsync(invitedEmail, transaction);
            var invitation = await store.LockInvitationAsync(digest, transaction);
            var now = clock.GetUtcNow();
            if (invitation is null || !invitation.IsOpen(now)) return Unavailable;
            if (!string.Equals(claimed, invitation.Email, StringComparison.Ordinal))
            {
                invitation.RecordMismatch(now);
                store.Add(new ProviderAuditEvent(Guid.NewGuid(), BootstrapAuditAction.InvitationRefused, now, null, invitation.Id));
                return Invalid;
            }

            if (await store.AdminExistsAsync(invitation.Email, transaction)) return Unavailable;
            return Enroll(invitation, passwordHash, now);
        }, cancellationToken);
    }

    private ErrorOr<EnrollmentGrant> Enroll(AdminInvitation invitation, string passwordHash, DateTimeOffset now)
    {
        var adminId = Guid.NewGuid();
        store.Add(new AdminIdentity(adminId, invitation.Email, passwordHash, "invitation:" + invitation.Id, now));
        invitation.Accept(adminId, now);
        store.Add(new ProviderAuditEvent(Guid.NewGuid(), BootstrapAuditAction.InvitationAccepted, now, adminId, invitation.Id));

        var credential = secrets.CreateEnrollment();
        var expiry = now + EnrollmentLifetime;
        store.Add(new BootstrapEnrollmentCredential(Guid.NewGuid(), adminId, credential.Digest, now, expiry));
        return new EnrollmentGrant(credential, expiry);
    }
}
