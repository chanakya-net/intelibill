using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public sealed record AdminCapabilityUpdate(string?[]? Capabilities, uint ExpectedVersion, string? Reason,
    bool Confirmed, bool Disable = false);

public sealed record AdminCapabilitiesRow(Guid Id, string Email, uint Version, string[] Capabilities, bool MfaEnrolled, bool Disabled, bool IsCurrent);
public sealed record AdminCapabilitiesPage(IReadOnlyList<AdminCapabilitiesRow> Items, Guid? NextCursor);

public interface IAdminCapabilitiesStore
{
    Task<ErrorOr<T>> InTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken token);
    Task LockOwnerFenceAsync(CancellationToken token);
    Task<(AdminIdentity? Actor, AdminIdentity? Target)> LockIdentitiesAsync(Guid actorId, Guid targetId, CancellationToken token);
    Task<AdminSession?> LockSessionAsync(AdminSessionCaller caller, CancellationToken token);
    Task<bool> HasOtherRecoverableOwnerAsync(Guid adminId, CancellationToken token);
    Task<IReadOnlyList<AdminIdentity>> ListAsync(Guid? after, int take, CancellationToken token);
    Task<IReadOnlyList<AdminSession>> LockTargetSessionsAsync(Guid adminId, CancellationToken token);
    Task RevokeSessionsAsync(IReadOnlyList<AdminSession> sessions, DateTimeOffset now, CancellationToken token);
    Task FlushAsync(CancellationToken token);
    void Add(ProviderAuditEvent auditEvent);
}

public static class AdminCapabilityErrors
{
    public static Error RecentMfa => Error.Forbidden("Admin.RecentMfaRequired", "Sign in again with your authenticator to edit administrator access.");
    public static Error AccessDenied => Error.Forbidden("Admin.AccessDenied", "You cannot manage administrator access.");
    public static Error Invalid => Error.Validation("Admin.CapabilityUpdateInvalid", "Select valid grants and a reason, then confirm the change.");
    public static Error TargetUnavailable => Error.NotFound("Admin.CapabilityTargetUnavailable", "The active administrator was not found.");
    public static Error VersionConflict => Error.Conflict("Admin.CapabilityVersionConflict", "Administrator access changed. Reload before editing.");
    public static Error OwnerNeedsMfa => Error.Conflict("Admin.OwnerRequiresMfa", "A platform owner must be active and enrolled in MFA.");
    public static Error LastOwner => Error.Conflict("Admin.LastRecoverableOwner", "This change would leave no recoverable platform owner.");
    public static Error Busy => Error.Failure("Admin.CapabilitiesBusy", "Administrator access is busy. Reload before editing.");

    internal static AuditReason? ParseReason(string? reason) => reason switch
    {
        "responsibilities-changed" => AuditReason.AdminResponsibilitiesChanged,
        "access-review" => AuditReason.AdminAccessReviewed,
        "access-withdrawn" => AuditReason.AdminAccessWithdrawn,
        _ => null,
    };
}
