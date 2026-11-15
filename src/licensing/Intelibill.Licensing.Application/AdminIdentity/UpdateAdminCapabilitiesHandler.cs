using ErrorOr;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public sealed class UpdateAdminCapabilitiesHandler(IAdminCapabilitiesStore store, TimeProvider clock,
    AuditEvidenceFactory audit)
{
    private const int PageSize = 50;

    public Task<ErrorOr<AdminCapabilitiesPage>> ListAsync(AdminSessionCaller caller, Guid? after, CancellationToken token) =>
        store.InTransactionAsync<AdminCapabilitiesPage>(async ct =>
        {
            var (actor, _) = await store.LockIdentitiesAsync(caller.AdminId, caller.AdminId, ct);
            var session = await store.LockSessionAsync(caller, ct);
            var now = clock.GetUtcNow();
            if (CheckAuthority(caller, actor, session, now) is { } error) return Deny(caller, now, error);
            var rows = await store.ListAsync(after, PageSize + 1, ct);
            var items = rows.Take(PageSize).Select(row => ToRow(row, caller.AdminId)).ToArray();
            store.Add(audit.Create(BootstrapAuditAction.AdminCapabilitiesInspected, now, AuditActor.Administrator,
                AuditReason.AdminCapabilitiesInspected, caller.AdminId));
            return new AdminCapabilitiesPage(items, rows.Count > PageSize ? items[^1].Id : null);
        }, token);

    public Task<ErrorOr<AdminCapabilitiesRow>> UpdateAsync(AdminSessionCaller caller, Guid targetId,
        AdminCapabilityUpdate? request, CancellationToken token) =>
        store.InTransactionAsync<AdminCapabilitiesRow>(async ct =>
        {
            await store.LockOwnerFenceAsync(ct);
            var (actor, target) = await store.LockIdentitiesAsync(caller.AdminId, targetId, ct);
            var session = await store.LockSessionAsync(caller, ct);
            var now = clock.GetUtcNow();
            if (CheckAuthority(caller, actor, session, now) is { } error) return Deny(caller, now, error);
            if (CheckMutationSession(caller, session!, now) is { } refusal) return Deny(caller, now, refusal);
            return await ChangeLockedAsync(caller, session!, target, request, now, ct);
        }, token);

    private async Task<ErrorOr<AdminCapabilitiesRow>> ChangeLockedAsync(AdminSessionCaller caller, AdminSession session,
        AdminIdentity? target, AdminCapabilityUpdate? request, DateTimeOffset now, CancellationToken token)
    {
        if (request is not { Confirmed: true, ExpectedVersion: > 0 }
            || !AdminCapabilityNames.TryParse(request.Capabilities, out var grants)
            || AdminCapabilityErrors.ParseReason(request.Reason) is not { } reason)
            return Deny(caller, now, (AdminCapabilityErrors.Invalid, AuditReason.RequestRejected));
        if (target is not { DeactivatedAt: null })
            return Deny(caller, now, (AdminCapabilityErrors.TargetUnavailable, AuditReason.ScopeUnavailable));
        if (target.Version != request.ExpectedVersion)
            return Deny(caller, now, (AdminCapabilityErrors.VersionConflict, AuditReason.RequestRejected));
        if (target.Capabilities == grants && !request.Disable) return ToRow(target, caller.AdminId);

        var owner = grants.HasFlag(AdminCapability.PlatformOwner);
        if (owner && !target.IsPlatformOwner && (target.MfaEnrolledAt is null || request.Disable))
            return Deny(caller, now, (AdminCapabilityErrors.OwnerNeedsMfa, AuditReason.RequestRejected));
        if (target.IsPlatformOwner && (!owner || request.Disable) && !await store.HasOtherRecoverableOwnerAsync(target.Id, token))
            return Deny(caller, now, (AdminCapabilityErrors.LastOwner, AuditReason.LastRecoverableOwner));

        var sessions = await store.LockTargetSessionsAsync(target.Id, token);
        now = clock.GetUtcNow();
        if (CheckMutationSession(caller, session, now) is { } refusal) return Deny(caller, now, refusal);

        var before = Reference(target);
        target.UpdateCapabilities(grants, request.Disable, now);
        await store.RevokeSessionsAsync(sessions, now, token);
        await store.FlushAsync(token);
        var action = request.Disable ? BootstrapAuditAction.AdminDisabled : BootstrapAuditAction.AdminCapabilitiesChanged;
        store.Add(audit.Create(action, now, AuditActor.Administrator, reason, caller.AdminId,
            before: before, after: Reference(target)));
        return ToRow(target, caller.AdminId);
    }

    private static (Error Error, AuditReason Reason)? CheckAuthority(
        AdminSessionCaller caller, AdminIdentity? actor, AdminSession? session, DateTimeOffset now)
    {
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return (AdminRefreshErrors.SessionEnded, AuditReason.ScopeUnavailable);
        if (!actor.IsPlatformOwner) return (AdminCapabilityErrors.AccessDenied, AuditReason.CapabilityRequired);
        return null;
    }

    private static (Error Error, AuditReason Reason)? CheckMutationSession(
        AdminSessionCaller caller, AdminSession session, DateTimeOffset now)
    {
        if (now >= caller.AccessExpiresAt || !session.IsLiveAt(now))
            return (AdminRefreshErrors.SessionEnded, AuditReason.ScopeUnavailable);
        if (!new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return (AdminCapabilityErrors.RecentMfa, AuditReason.RecentMfaRequired);
        return null;
    }

    private Error Deny(AdminSessionCaller caller, DateTimeOffset now, (Error Error, AuditReason Reason) refusal)
    {
        store.Add(audit.Create(BootstrapAuditAction.AdminCapabilitiesDenied, now, AuditActor.Administrator,
            refusal.Reason, caller.AdminId));
        return refusal.Error;
    }

    private static AdminCapabilitiesRow ToRow(AdminIdentity admin, Guid actorId) => new(admin.Id, admin.Email,
        admin.Version, AdminCapabilityNames.ToNames(admin.Capabilities), admin.MfaEnrolledAt is not null, admin.DeactivatedAt is not null, admin.Id == actorId);

    private static AuditReference Reference(AdminIdentity admin) =>
        new(AuditTarget.Administrator, admin.Id,
            admin.DeactivatedAt is not null ? AuditState.Disabled
                : admin.MfaEnrolledAt is not null ? AuditState.MfaEnrolled : AuditState.MfaPending,
            admin.Version, Capabilities: admin.Capabilities);
}
