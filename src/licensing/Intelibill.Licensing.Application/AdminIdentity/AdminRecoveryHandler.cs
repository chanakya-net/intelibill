using ErrorOr;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

/// <summary>
/// Recovers administrator access without bypassing MFA. Both paths need the account password plus a
/// one-use proof (a recovery code, or a reviewed reset grant); both end every session and leave only an
/// enrollment credential, so business access returns solely through a newly verified authenticator.
/// </summary>
public sealed partial class AdminRecoveryHandler(IAdminRecoveryStore store, IBootstrapSecrets secrets,
    TimeProvider clock, AuditEvidenceFactory audit)
{
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(15);

    // BCrypt cost matches issued credentials so an absent account also incurs the password check.
    private const string DummyHash = "$2a$12$NWpJQSOVZnhhNj/xRbce9OC13H4oFaYY7JKO1ls74MlFszrCNuoJm";

    public Task<ErrorOr<EnrollmentGrant>> RedeemCodeAsync(string? email, string? password, string? recoveryCode,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = BootstrapValidation.NormalizeEmail(email);
        var code = AdminRecoveryRules.NormalizeCode(recoveryCode);
        return store.InTransactionAsync<ErrorOr<EnrollmentGrant>>(async token =>
        {
            var admin = await store.LockByEmailAsync(normalizedEmail, token);
            var verified = PasswordMatches(password, admin?.PasswordHash);
            var now = clock.GetUtcNow();
            if (!verified || admin is not { DeactivatedAt: null } || code is null
                || !await store.ConsumeRecoveryCodeAsync(admin.Id, code, now, token))
                return Refuse(admin?.Id, now);

            var before = Reference(admin);
            await store.ResetMfaAsync(admin, now, keepRecoveryCodes: true, token);
            store.Add(AdminRecovery.FromCode(admin.Id, now));
            store.Add(audit.Create(BootstrapAuditAction.RecoveryCodeRedeemed, now, AuditActor.Enrollment,
                AuditReason.RecoveryCodeAccepted, admin.Id, before: before, after: Reference(admin, now)));
            return IssueEnrollment(admin.Id, now);
        }, cancellationToken);
    }

    public Task<ErrorOr<EnrollmentGrant>> RedeemResetAsync(string? email, string? password, string? resetToken,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = BootstrapValidation.NormalizeEmail(email);
        var digest = BootstrapValidation.IsRecoveryResetValid(resetToken) ? secrets.HashRecoveryReset(resetToken!) : null;
        return store.InTransactionAsync<ErrorOr<EnrollmentGrant>>(async token =>
        {
            var admin = await store.LockByEmailAsync(normalizedEmail, token);
            var verified = PasswordMatches(password, admin?.PasswordHash);
            var now = clock.GetUtcNow();
            var reset = admin is null || digest is null ? null : await store.LockResetAsync(admin.Id, digest, token);
            if (!verified || admin is not { DeactivatedAt: null, MfaEnrolledAt: null } || reset?.IsOpen(now) != true)
                return Refuse(admin?.Id, now);

            reset.Redeem(now);
            await store.RevokeEnrollmentAsync(admin.Id, now, token);
            store.Add(audit.Create(BootstrapAuditAction.RecoveryResetRedeemed, now, AuditActor.Enrollment,
                AuditReason.RecoveryResetAccepted, admin.Id, before: Reference(admin), after: Reference(admin, now)));
            return IssueEnrollment(admin.Id, now);
        }, cancellationToken);
    }

    private Error Refuse(Guid? adminId, DateTimeOffset now)
    {
        store.Add(audit.Create(BootstrapAuditAction.RecoveryRefused, now, AuditActor.Unauthenticated,
            AuditReason.RecoveryProofInvalid, adminId));
        return AdminRecoveryRules.Refused;
    }

    private EnrollmentGrant IssueEnrollment(Guid adminId, DateTimeOffset now)
    {
        var credential = secrets.CreateEnrollment();
        var expiry = now + EnrollmentLifetime;
        store.Add(new BootstrapEnrollmentCredential(Guid.NewGuid(), adminId, credential.Digest, now, expiry));
        return new EnrollmentGrant(credential, expiry);
    }

    private bool PasswordMatches(string? password, string? hash)
    {
        var valid = BootstrapValidation.IsPasswordValid(password);
        var matches = secrets.VerifyPassword(valid ? password! : "invalid-password", hash ?? DummyHash);
        return valid && hash is not null && matches;
    }

    /// <summary>The administrator's MFA state as audit evidence; never an address or credential.</summary>
    private static AuditReference Reference(AdminIdentity admin, DateTimeOffset? resetAt = null) =>
        resetAt is not null || admin.MfaEnrolledAt is null
            ? new(AuditTarget.Administrator, admin.Id, AuditState.MfaPending, MilestoneAt: resetAt ?? admin.VerifiedAt)
            : new(AuditTarget.Administrator, admin.Id, AuditState.MfaEnrolled, MilestoneAt: admin.MfaEnrolledAt);
}
