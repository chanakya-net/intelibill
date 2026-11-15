using Intelibill.Licensing.Application.Audit;
using ErrorOr;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public sealed class InviteAdminHandler(
    IAdminInvitationStore store, IBootstrapSecrets secrets, IAdminInvitationMailer mailer, TimeProvider clock,
    AuditEvidenceFactory audit)
{
    /// <summary>
    /// Confirms the actor's recent MFA and persisted platform-owner authority, and returns the
    /// provider origin that invitation links and request checks are bound to.
    /// </summary>
    public async Task<ErrorOr<string>> AuthorizeAsync(ProviderActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRecentMfa(clock.GetUtcNow()))
            return Error.Forbidden("Admin.RecentMfaRequired", "Verify your authenticator again to manage administrators.");

        var origin = await store.FindOwnerOriginAsync(actor.AdminId, cancellationToken);
        return origin is null
            ? Error.Forbidden("Admin.AccessDenied", "You cannot manage administrator invitations.")
            : origin;
    }

    public async Task<ErrorOr<IssuedInvitation>> InviteAsync(
        ProviderActor actor, string? email, CancellationToken cancellationToken)
    {
        var authority = await AuthorizeAsync(actor, cancellationToken);
        if (authority.IsError) return authority.Errors;

        var normalized = BootstrapValidation.NormalizeEmail(email);
        if (normalized is null)
            return Error.Validation("Invitation.InvalidEmail", "Enter the administrator's email address.");

        var secret = secrets.CreateInvitation();
        var issued = await store.InTransactionAsync(token => IssueAsync(actor, normalized, secret.Digest, token), cancellationToken);
        if (issued.IsError) return issued.Errors;

        var link = new Uri(authority.Value + "/auth/invitation#token=" + secret.Value);
        var invitation = issued.Value;
        if (await mailer.TrySendAsync(new AdminInvitationMessage(email!, link, invitation.ExpiresAt), cancellationToken))
            return new IssuedInvitation(invitation.Id, invitation.ExpiresAt);

        // Nobody received the token, so retiring the row only removes an unusable invitation.
        await store.RetireUndeliveredAsync(invitation.Id, actor.AdminId, clock.GetUtcNow(), CancellationToken.None);
        return Error.Failure("Invitation.DeliveryUnavailable", "The invitation email could not be sent. Try again later.");
    }

    private async Task<ErrorOr<AdminInvitation>> IssueAsync(
        ProviderActor actor, string email, string digest, CancellationToken cancellationToken)
    {
        var owner = await store.LockInviterAsync(actor.AdminId, cancellationToken);
        var caller = actor.Session;
        var session = caller is null ? null : await store.LockInviterSessionAsync(caller, cancellationToken);
        await store.LockEmailAsync(email, cancellationToken);
        var now = clock.GetUtcNow();
        if (owner is not { IsPlatformOwner: true, MfaEnrolledAt: not null, DeactivatedAt: null })
            return Error.Forbidden("Admin.AccessDenied", "You cannot manage administrator invitations.");
        if (caller is not null && (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)))
            return AdminRefreshErrors.SessionEnded;
        if (!(actor with { MfaVerifiedAt = session?.MfaVerifiedAt ?? actor.MfaVerifiedAt }).HasRecentMfa(now))
            return Error.Forbidden("Admin.RecentMfaRequired", "Verify your authenticator again to manage administrators.");
        if (await store.AdminExistsAsync(email, cancellationToken))
            return Error.Conflict("Invitation.AlreadyAdmin", "This email already belongs to an administrator.");

        var superseded = await store.SupersedeOpenAsync(email, now, cancellationToken);
        var invitation = new AdminInvitation(Guid.NewGuid(), email, digest, actor.AdminId, now, now + AdminInvitation.Lifetime);
        store.Add(invitation);
        await store.FlushAsync(cancellationToken);

        foreach (var retired in superseded)
        {
            store.Add(audit.Create(BootstrapAuditAction.InvitationSuperseded, now, AuditActor.Administrator,
                AuditReason.InvitationSuperseded, actor.AdminId, retired.After.TargetId, retired.Before, retired.After));
        }

        store.Add(audit.Create(BootstrapAuditAction.InvitationIssued, now, AuditActor.Administrator,
            AuditReason.InvitationIssued, actor.AdminId, invitation.Id, after: invitation.AuditReferenceAt(now)));
        return invitation;
    }
}

public sealed record IssuedInvitation(Guid InvitationId, DateTimeOffset ExpiresAt);
