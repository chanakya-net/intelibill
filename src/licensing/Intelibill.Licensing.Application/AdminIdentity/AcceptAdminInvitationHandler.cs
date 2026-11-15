using Intelibill.Licensing.Application.Audit;
using ErrorOr;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

/// <summary>
/// Turns one open invitation into a verified, MFA-pending administrator. The result is the same
/// enrollment-only credential the first owner receives: no business session and no capability.
/// </summary>
public sealed class AcceptAdminInvitationHandler(IAdminInvitationStore store, IBootstrapSecrets secrets,
    TimeProvider clock, AuditEvidenceFactory audit)
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
                await RecordMismatchAsync(invitation, now, transaction);
                return Invalid;
            }

            if (await store.AdminExistsAsync(invitation.Email, transaction)) return Unavailable;
            return await EnrollAsync(invitation, passwordHash, now, transaction);
        }, cancellationToken);
    }

    private async Task RecordMismatchAsync(AdminInvitation invitation, DateTimeOffset now, CancellationToken token)
    {
        var before = invitation.AuditReferenceAt(now);
        invitation.RecordMismatch(now);
        await store.FlushAsync(token);
        store.Add(audit.Create(BootstrapAuditAction.InvitationRefused, now, AuditActor.Unauthenticated,
            AuditReason.InvitationIdentityMismatch, invitationId: invitation.Id, before: before,
            after: invitation.AuditReferenceAt(now)));
    }

    private async Task<ErrorOr<EnrollmentGrant>> EnrollAsync(AdminInvitation invitation, string passwordHash,
        DateTimeOffset now, CancellationToken token)
    {
        var before = invitation.AuditReferenceAt(now);
        var adminId = Guid.NewGuid();
        store.Add(new AdminIdentity(adminId, invitation.Email, passwordHash, "invitation:" + invitation.Id, now));
        invitation.Accept(adminId, now);

        var credential = secrets.CreateEnrollment();
        var expiry = now + EnrollmentLifetime;
        store.Add(new BootstrapEnrollmentCredential(Guid.NewGuid(), adminId, credential.Digest, now, expiry));
        await store.FlushAsync(token);
        store.Add(audit.Create(BootstrapAuditAction.InvitationAccepted, now, AuditActor.Unauthenticated,
            AuditReason.InvitationAccepted, adminId, invitation.Id, before, invitation.AuditReferenceAt(now)));
        return new EnrollmentGrant(credential, expiry);
    }
}
