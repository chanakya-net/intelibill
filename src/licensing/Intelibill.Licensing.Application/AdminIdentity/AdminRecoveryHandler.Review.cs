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
    public async Task<ErrorOr<IssuedRecoveryReset>> ReviewResetAsync(ProviderActor actor, AdminRecoveryReview review,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(review);
        if (!actor.HasRecentMfa(clock.GetUtcNow()))
            return await DenyAsync(actor, AuditReason.RecentMfaRequired, AdminRecoveryRules.RecentMfaRequired, cancellationToken);

        var evidence = AdminRecoveryRules.ParseEvidence(review);
        var email = BootstrapValidation.NormalizeEmail(review.Email);
        if (evidence is null || email is null) return AdminRecoveryRules.InvalidReview;
        var secret = secrets.CreateRecoveryReset();
        return await store.InTransactionAsync(token => ReviewLockedAsync(actor, email, evidence, secret, token),
            cancellationToken);
    }

    private async Task<ErrorOr<IssuedRecoveryReset>> ReviewLockedAsync(ProviderActor actor, string email,
        AdminRecoveryEvidence evidence, BootstrapSecret secret, CancellationToken token)
    {
        var (reviewer, target) = await store.LockReviewAsync(actor.AdminId, email, token);
        var now = clock.GetUtcNow();
        if (reviewer is not { DeactivatedAt: null, MfaEnrolledAt: not null } || !actor.HasRecentMfa(now)
            || !await store.IsPlatformOwnerAsync(reviewer.Id, token))
            return Deny(actor, now, AuditReason.CapabilityRequired, AdminRecoveryRules.AccessDenied);
        if (target is not { DeactivatedAt: null })
            return Deny(actor, now, AuditReason.ScopeUnavailable, AdminRecoveryRules.TargetUnavailable);
        if (await store.IsPlatformOwnerAsync(target.Id, token) && await store.CountRecoverableOwnersAsync(target.Id, token) == 0)
            return Deny(actor, now, AuditReason.LastRecoverableOwner, AdminRecoveryRules.LastRecoverableOwner);
        if (target.Id == reviewer.Id)
            return Deny(actor, now, AuditReason.RequestRejected, AdminRecoveryRules.SelfReview);

        var before = Reference(target);
        await store.ResetMfaAsync(target, now, keepRecoveryCodes: false, token);
        var recovery = AdminRecovery.Reviewed(target.Id, reviewer.Id, evidence, secret.Digest, now);
        store.Add(recovery);
        store.Add(audit.Create(BootstrapAuditAction.RecoveryResetIssued, now, AuditActor.Administrator,
            AuditReason.RecoveryResetReviewed, reviewer.Id, before: before, after: Reference(target, now)));
        return new IssuedRecoveryReset(recovery.Id, secret.Value, recovery.GrantExpiresAt!.Value);
    }

    private Task<ErrorOr<IssuedRecoveryReset>> DenyAsync(ProviderActor actor, AuditReason reason, Error error,
        CancellationToken cancellationToken) =>
        store.InTransactionAsync<ErrorOr<IssuedRecoveryReset>>(_ =>
            Task.FromResult<ErrorOr<IssuedRecoveryReset>>(Deny(actor, clock.GetUtcNow(), reason, error)), cancellationToken);

    private Error Deny(ProviderActor actor, DateTimeOffset now, AuditReason reason, Error error)
    {
        store.Add(audit.Create(BootstrapAuditAction.RecoveryResetDenied, now, AuditActor.Administrator, reason, actor.AdminId));
        return error;
    }
}
