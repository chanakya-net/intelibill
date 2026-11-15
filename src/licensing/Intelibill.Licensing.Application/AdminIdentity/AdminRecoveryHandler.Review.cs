using ErrorOr;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public sealed partial class AdminRecoveryHandler
{
    /// <summary>
    /// A platform owner with recent MFA resets another administrator's MFA on explicit, reviewed
    /// evidence. The target's sessions end at once; the returned one-use grant is handed over
    /// out of band and is useless without the target's own password.
    /// </summary>
    public async Task<ErrorOr<IssuedRecoveryReset>> ReviewResetAsync(AdminSessionCaller caller, AdminRecoveryReview review,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(review);
        var evidence = AdminRecoveryRules.ParseEvidence(review);
        var email = BootstrapValidation.NormalizeEmail(review.Email);
        if (evidence is null || email is null) return AdminRecoveryRules.InvalidReview;
        var secret = secrets.CreateRecoveryReset();
        return await store.InTransactionAsync(token => ReviewLockedAsync(caller, email, evidence, secret, token),
            cancellationToken);
    }

    private async Task<ErrorOr<IssuedRecoveryReset>> ReviewLockedAsync(AdminSessionCaller caller, string email,
        AdminRecoveryEvidence evidence, BootstrapSecret secret, CancellationToken token)
    {
        var (reviewer, target) = await store.LockReviewAsync(caller.AdminId, email, token);
        var session = await store.LockReviewerSessionAsync(caller, token);
        var now = clock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now))
            return Deny(caller, now, AuditReason.ScopeUnavailable, AdminRefreshErrors.SessionEnded);
        if (!new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return Deny(caller, now, AuditReason.RecentMfaRequired, AdminRecoveryRules.RecentMfaRequired);
        if (reviewer is not { DeactivatedAt: null, MfaEnrolledAt: not null }
            || !await store.IsPlatformOwnerAsync(reviewer.Id, token))
            return Deny(caller, now, AuditReason.CapabilityRequired, AdminRecoveryRules.AccessDenied);
        if (target is not { DeactivatedAt: null })
            return Deny(caller, now, AuditReason.ScopeUnavailable, AdminRecoveryRules.TargetUnavailable);
        if (await store.IsPlatformOwnerAsync(target.Id, token) && await store.CountRecoverableOwnersAsync(target.Id, token) == 0)
            return Deny(caller, now, AuditReason.LastRecoverableOwner, AdminRecoveryRules.LastRecoverableOwner);
        if (target.Id == reviewer.Id)
            return Deny(caller, now, AuditReason.RequestRejected, AdminRecoveryRules.SelfReview);

        var before = Reference(target);
        await store.ResetMfaAsync(target, now, keepRecoveryCodes: false, token);
        var recovery = AdminRecovery.Reviewed(target.Id, reviewer.Id, evidence, secret.Digest, now);
        store.Add(recovery);
        store.Add(audit.Create(BootstrapAuditAction.RecoveryResetIssued, now, AuditActor.Administrator,
            AuditReason.RecoveryResetReviewed, reviewer.Id, before: before, after: Reference(target, now)));
        return new IssuedRecoveryReset(recovery.Id, secret.Value, recovery.GrantExpiresAt!.Value);
    }

    private Error Deny(AdminSessionCaller caller, DateTimeOffset now, AuditReason reason, Error error)
    {
        store.Add(audit.Create(BootstrapAuditAction.RecoveryResetDenied, now, AuditActor.Administrator, reason, caller.AdminId));
        return error;
    }
}
