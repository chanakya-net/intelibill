using ErrorOr;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public interface IAdminSessionStore
{
    Task<ErrorOr<AdminSessionGrant>> OpenAsync(string? normalizedEmail, Func<string?, bool> passwordMatches,
        string? otp, CancellationToken cancellationToken);

    Task<AdminSessionAccess?> AuthorizeAsync(Guid adminId, Guid sessionId, DateTimeOffset accessExpiresAt,
        CancellationToken cancellationToken);

    Task<ErrorOr<AdminDeviceSessionPage>> ListDevicesAsync(AdminSessionCaller caller, int pageSize,
        AdminDeviceSessionSeek? seek, CancellationToken token);

    Task<ErrorOr<Success>> RevokeDeviceAsync(AdminSessionCaller caller, Guid targetId, AuditReason reason,
        CancellationToken token);

    Task SignOutDeviceAsync(AdminSessionCaller caller, CancellationToken token);
}

public sealed record AdminSessionAccess(Guid AdminId, DateTimeOffset MfaVerifiedAt, IReadOnlyList<string> Capabilities);

public interface IAdminSignInBudget
{
    Task<DateTimeOffset?> TakeAsync(string accountSubject, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class AdminSignInHandler(IAdminSessionStore sessions, IBootstrapSecrets secrets)
{
    // BCrypt cost matches issued credentials so an absent account also incurs the password check.
    private const string DummyHash = "$2a$12$NWpJQSOVZnhhNj/xRbce9OC13H4oFaYY7JKO1ls74MlFszrCNuoJm";

    public static Error Refused => Error.Unauthorized("Admin.InvalidSignIn", "The sign-in details could not be verified.");

    public Task<ErrorOr<AdminSessionGrant>> HandleAsync(string? email, string? password, string? otp, CancellationToken cancellationToken)
    {
        var normalizedEmail = BootstrapValidation.NormalizeEmail(email);
        return sessions.OpenAsync(normalizedEmail, hash => Matches(password, hash), otp, cancellationToken);
    }

    private bool Matches(string? password, string? hash)
    {
        var valid = BootstrapValidation.IsPasswordValid(password);
        var matches = secrets.VerifyPassword(valid ? password! : "invalid-password", hash ?? DummyHash);
        return valid && hash is not null && matches;
    }
}
