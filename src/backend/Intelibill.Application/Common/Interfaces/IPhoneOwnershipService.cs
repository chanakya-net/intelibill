using ErrorOr;
using Intelibill.Application.Features.Auth.DTOs;

namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Verified phone authentication. Every path issues a short-lived, purpose-bound code and spends
/// it exactly once; nothing here grants a session on a number that has not been proved.
/// </summary>
public interface IPhoneOwnershipService
{
    /// <summary>
    /// Starts enrollment of a number nobody has proved. The receipt is deliberately the same shape
    /// whether or not the number was eligible, so a caller cannot use this endpoint to learn who
    /// has an account; an ineligible request carries <see cref="PhoneChallengeReceipt.ChallengeId"/>
    /// of <see cref="Guid.Empty"/>.
    /// </summary>
    Task<ErrorOr<PhoneChallengeReceipt>> RequestEnrollmentAsync(string phone, CancellationToken ct);

    /// <summary>Starts a sign-in on a number its holder proved earlier.</summary>
    Task<ErrorOr<PhoneChallengeReceipt>> RequestLoginAsync(string phone, CancellationToken ct);

    Task<ErrorOr<AuthResult>> ConfirmEnrollmentAsync(Guid challengeId, string code, string firstName, string lastName, CancellationToken ct);

    Task<ErrorOr<AuthResult>> ConfirmLoginAsync(Guid challengeId, string code, CancellationToken ct);

    /// <summary>
    /// Starts moving the caller's sign-in number to a new one. Needs the live session it arrives
    /// on, and a reauthentication proof as well where the account has a password to check. The new
    /// number is not touched until it has been proved.
    /// </summary>
    Task<ErrorOr<PhoneChallengeReceipt>> RequestPhoneChangeAsync(Guid actor, long version, string phone, string? reauthProof, CancellationToken ct);

    /// <summary>
    /// Completes the move. Every device is signed out, because the factor that reaches the account
    /// has changed, and both the old and the new number are told.
    /// </summary>
    Task<ErrorOr<bool>> ConfirmPhoneChangeAsync(Guid actor, long version, Guid challengeId, string code, CancellationToken ct);
}

/// <summary>
/// What a caller learns when a code has been queued: which challenge to confirm against, a masked
/// destination so the holder can tell which of their numbers it went to, when a replacement may be
/// requested, and when this one stops working. Never the code.
/// </summary>
public sealed record PhoneChallengeReceipt(
    Guid ChallengeId,
    string MaskedDestination,
    DateTimeOffset ResendAllowedAt,
    DateTimeOffset ExpiresAt);
