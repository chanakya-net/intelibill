using System.Text.RegularExpressions;
using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

/// <summary>Persistence for administrator recovery. Every member runs inside <see cref="InTransactionAsync{T}"/>.</summary>
public interface IAdminRecoveryStore
{
    /// <summary>Runs <paramref name="operation"/> in one transaction and saves whatever it staged, refusals included.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>Locks the administrator row for <paramref name="normalizedEmail"/>, the order every MFA change uses.</summary>
    Task<AdminIdentity?> LockByEmailAsync(string? normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Locks the reviewer and the target in id order so two reviewers cannot deadlock.</summary>
    Task<(AdminIdentity? Reviewer, AdminIdentity? Target)> LockReviewAsync(Guid reviewerId, string normalizedEmail,
        CancellationToken cancellationToken);

    /// <summary>Consumes one unused recovery code that matches; false when none does.</summary>
    Task<bool> ConsumeRecoveryCodeAsync(Guid adminId, string normalizedCode, DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<AdminRecovery?> LockResetAsync(Guid adminId, string grantDigest, CancellationToken cancellationToken);

    Task<bool> IsPlatformOwnerAsync(Guid adminId, CancellationToken cancellationToken);

    /// <summary>Active, MFA-enrolled platform owners other than <paramref name="exceptAdminId"/>.</summary>
    Task<int> CountRecoverableOwnersAsync(Guid exceptAdminId, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session of the administrator, retires the authenticator, open enrollment credentials and
    /// open reset grants, and returns the identity to MFA-pending. Unused recovery codes survive only
    /// when <paramref name="keepRecoveryCodes"/> is set, so the holder can re-enter if enrollment lapses.
    /// </summary>
    Task ResetMfaAsync(AdminIdentity admin, DateTimeOffset now, bool keepRecoveryCodes, CancellationToken cancellationToken);

    /// <summary>Revokes the administrator's open enrollment credentials so one stays live at most.</summary>
    Task RevokeEnrollmentAsync(Guid adminId, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(AdminRecovery recovery);
    void Add(BootstrapEnrollmentCredential credential);
    void Add(ProviderAuditEvent auditEvent);
}

/// <summary>The raw review request. Parsing turns it into closed evidence or refuses it.</summary>
public sealed record AdminRecoveryReview(string? Email, string? Reason, string? Verification,
    string? EvidenceReference, bool? Confirmed);

public sealed record IssuedRecoveryReset(Guid RecoveryId, string ResetToken, DateTimeOffset ExpiresAt);

public static partial class AdminRecoveryRules
{
    public static Error Refused => Error.Unauthorized("Admin.RecoveryRefused", "The recovery details could not be verified.");
    public static Error RecentMfaRequired => Error.Forbidden("Admin.RecentMfaRequired", "Verify your authenticator again to review a recovery.");
    public static Error AccessDenied => Error.Forbidden("Admin.AccessDenied", "You cannot review administrator recovery.");
    public static Error InvalidReview => Error.Validation("Admin.RecoveryReviewInvalid",
        "Select the reason and verification, enter the evidence reference and confirm the review.");
    public static Error TargetUnavailable => Error.NotFound("Admin.RecoveryTargetUnavailable", "No active administrator uses that email.");
    public static Error LastRecoverableOwner => Error.Conflict("Admin.LastRecoverableOwner",
        "This reset would leave no platform owner able to recover access.");
    public static Error SelfReview => Error.Conflict("Admin.RecoverySelfReview", "Use your own recovery codes to recover your access.");

    /// <summary>Accepts 32 hexadecimal digits, optionally grouped by dashes as issued.</summary>
    public static string? NormalizeCode(string? input)
    {
        if (input is null || input.Length is not (32 or 35)) return null;
        var normalized = input.Replace("-", "", StringComparison.Ordinal);
        return normalized.Length == 32 && normalized.All(char.IsAsciiHexDigit) ? normalized.ToUpperInvariant() : null;
    }

    public static AdminRecoveryEvidence? ParseEvidence(AdminRecoveryReview review)
    {
        AdminRecoveryReason? reason = review.Reason switch
        {
            "authenticator-lost" => AdminRecoveryReason.AuthenticatorLost,
            "recovery-codes-lost" => AdminRecoveryReason.RecoveryCodesLost,
            "suspected-compromise" => AdminRecoveryReason.SuspectedCompromise,
            _ => null,
        };
        AdminRecoveryVerification? verification = review.Verification switch
        {
            "video-call" => AdminRecoveryVerification.VideoCall,
            "in-person" => AdminRecoveryVerification.InPerson,
            "verified-callback" => AdminRecoveryVerification.VerifiedCallback,
            _ => null,
        };
        return review.Confirmed == true && reason is { } r && verification is { } v
            && review.EvidenceReference is { } reference && ReferencePattern().IsMatch(reference)
            ? new AdminRecoveryEvidence(r, v, reference) : null;
    }

    // A ticket-style reference only: no spaces, no '@', so it cannot carry an address or a narrative.
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._:#/-]{2,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
