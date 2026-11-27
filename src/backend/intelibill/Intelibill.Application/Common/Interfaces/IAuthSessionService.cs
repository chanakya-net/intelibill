using ErrorOr;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// The only way a session, and therefore an access token, comes into being. Credential checks hand
/// it a proof; everything else may only reuse a session that is already valid.
/// </summary>
public interface IAuthSessionService
{
    /// <summary>
    /// Creates a session for a credential check that just succeeded, with its refresh family. The
    /// account is locked and re-read first, so a proof whose authority has moved is refused rather
    /// than carried forward.
    /// </summary>
    Task<ErrorOr<AuthResult>> StartAsync(
        AuthenticationProof proof,
        Guid? preferredShopId,
        CancellationToken ct);

    /// <summary>
    /// Creates the session inside a transaction the caller already owns and will commit, for the
    /// flows that have just proved authority under their own lock — mailbox enrollment and claim,
    /// and registration, where the account row is being inserted by this same transaction.
    /// </summary>
    /// <param name="proof">
    /// What proved the account, named at the call site rather than assumed. This method does not
    /// take the account lock or re-read the account's version — the caller must already hold that
    /// lock, which is why <see cref="StartAsync"/> exists for everything else.
    /// </param>
    Task<AuthResult> IssueForProvedAccountAsync(
        User user,
        AuthenticationProof proof,
        Guid? preferredShopId,
        CancellationToken ct);

    /// <summary>
    /// Exchanges a refresh credential for its one successor inside the session that holds it. The
    /// session id does not change, so a device keeps its identity across renewals. A credential that
    /// was already exchanged ends its session instead: a second use is indistinguishable from the
    /// replay of a stolen copy.
    /// </summary>
    Task<ErrorOr<AuthResult>> RotateAsync(string refreshToken, Guid? preferredShopId, CancellationToken ct);

    /// <summary>
    /// Ordinary sign-out: ends the device session the credential belongs to, with every credential
    /// it issued. Takes the same locks in the same order as <see cref="RotateAsync"/>, so a renewal
    /// running alongside cannot leave a successor behind.
    /// </summary>
    Task<ErrorOr<Success>> SignOutAsync(string refreshToken, CancellationToken ct);

    /// <summary>
    /// Moves one device into a shop it holds a live membership in and reissues only its access
    /// token. No other device's scope changes.
    /// </summary>
    Task<ErrorOr<AccessScopeResult>> RescopeAsync(Guid userId, Guid sessionId, Guid shopId, CancellationToken ct);

    Task<ErrorOr<IReadOnlyList<AuthSessionDto>>> ListAsync(Guid userId, Guid currentSessionId, CancellationToken ct);

    /// <summary>Ends one of the caller's own devices. Another user's session is not found.</summary>
    Task<ErrorOr<Success>> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Ends every device on the account, the caller's own included: someone reaching for this has
    /// lost track of where they are signed in, and leaving the device that asked still signed in
    /// would be the one exception they cannot check.
    /// </summary>
    Task<ErrorOr<Success>> RevokeAllAsync(Guid userId, CancellationToken ct);
}
