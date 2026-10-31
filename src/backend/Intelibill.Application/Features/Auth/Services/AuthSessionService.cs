using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Intelibill.Application.Features.Auth.Services;

/// <summary>
/// Owns the one transaction a session may be born in. Every path through here takes the account
/// lock first and re-reads the account's authority from the database, because the gap between
/// checking a credential and creating a session is exactly where a password reset or a disablement
/// would otherwise be overtaken.
/// </summary>
internal sealed partial class AuthSessionService(
    IAuthSessionRepository sessions,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ITokenService tokens,
    IRecoveryEpochProvider recoveryEpochs,
    IDeviceMetadataProvider devices,
    TimeProvider clock,
    ILogger<AuthSessionService> logger) : IAuthSessionService
{
    private const string RefreshReuse = "refresh-reuse";

    public async Task<ErrorOr<AuthResult>> StartAsync(
        AuthenticationProof proof,
        Guid? preferredShopId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proof);

        // Read before the lock is taken. The account row is held only for the decision and the
        // writes that follow it, so a password change waiting on that row is not held up by an
        // unrelated read — and anything the decision rests on is re-read under the lock below.
        var user = await users.GetByIdWithDetailsAsync(proof.UserId, ct);
        if (user is null)
            return Errors.Auth.InvalidCredentials;

        await using var transaction = await sessions.BeginAsync(ct);

        var authority = await sessions.LockUserAuthorityAsync(proof.UserId, ct);
        if (authority is null || !authority.CanHoldSession)
            return Errors.Auth.InvalidCredentials;

        // A registration proof belongs to an account created inside this transaction, so there is
        // no earlier version to compare; every other proof names the version it was checked
        // against and is refused if the account has moved on since.
        if (proof.Credential != CredentialKind.Registration &&
            authority.SecurityVersion != proof.VerifiedSecurityVersion)
            return Errors.Auth.InvalidCredentials;

        var result = await IssueAsync(user, authority.SecurityVersion, preferredShopId, ct);
        if (!await sessions.CommitAsync(ct))
            return Errors.Auth.InvalidCredentials;

        return result;
    }

    public async Task<ErrorOr<AuthResult>> RotateAsync(string refreshToken, Guid? preferredShopId, CancellationToken ct)
    {
        await using var transaction = await sessions.BeginAsync(ct);

        var presented = await LockPresentedAsync(refreshToken, ct);
        if (presented is null)
            return Errors.Auth.InvalidRefreshToken;

        var (authority, session, credential, now) = presented;
        if (credential.IsConsumed)
            return await EndForReuseAsync(presented);

        if (!credential.IsUsableAt(now) ||
            authority is null ||
            !authority.CanHoldSession ||
            credential.SecurityVersion != authority.SecurityVersion ||
            !session.IsActive(now) ||
            session.SecurityVersion != authority.SecurityVersion ||
            !string.Equals(session.RecoveryEpoch, await recoveryEpochs.GetCurrentAsync(ct), StringComparison.Ordinal))
            return Errors.Auth.InvalidRefreshToken;

        // Read under the account lock, so the memberships shop selection rests on are the current
        // ones rather than whatever a pre-lock read saw.
        var user = await users.GetByIdWithDetailsAsync(authority.UserId, ct);
        if (user is null)
            return Errors.Auth.InvalidRefreshToken;

        var secret = tokens.CreateRefreshSecret();
        var successor = credential.Rotate(secret.TokenHash, secret.ExpiresAt, now);
        await refreshTokens.AddAsync(successor, ct);

        // Renew in the shop this device was already working in. Only if that membership has gone
        // does selection fall back, and then the scope version moves so the old access token stops
        // authorising anything against the shop it named.
        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user, session.ActiveShopId ?? preferredShopId);
        session.ChangeScope(activeShopId, now);

        // The session's window follows the credential that renews it, so a device in daily use is
        // not signed out on the anniversary of its first sign-in. Revocation still ends it at once,
        // and ExtendTo refuses to revive a revoked session.
        session.ExtendTo(successor.ExpiresAt);

        // Signed before commit and returned only after it. If signing throws, the transaction is
        // disposed uncommitted: the predecessor stays unconsumed and no successor exists.
        var (accessToken, accessTokenExpiry) = await tokens.GenerateAccessTokenAsync(user, session, activeShopRole, ct);

        if (!await sessions.CommitAsync(ct))
            return Errors.Auth.InvalidRefreshToken;

        return Compose(user, session, accessToken, accessTokenExpiry, secret.Token, successor.ExpiresAt, activeShopId, shops);
    }

    public async Task<ErrorOr<Success>> SignOutAsync(string refreshToken, CancellationToken ct)
    {
        await using var transaction = await sessions.BeginAsync(ct);

        var presented = await LockPresentedAsync(refreshToken, ct);
        if (presented is null)
            return Errors.Auth.InvalidRefreshToken;

        // Signing out with a credential that was already exchanged is the same signal as renewing
        // with one, and gets the same answer.
        if (presented.Credential.IsConsumed)
            return await EndForReuseAsync(presented);

        if (!presented.Credential.IsUsableAt(presented.Now))
            return Errors.Auth.InvalidRefreshToken;

        await sessions.RevokeAsync(presented.Session, "signed-out", presented.Now, ct);

        return await sessions.CommitAsync(ct) ? Result.Success : Errors.Auth.InvalidRefreshToken;
    }

    public async Task<ErrorOr<AccessScopeResult>> RescopeAsync(Guid userId, Guid sessionId, Guid shopId, CancellationToken ct)
    {
        // Read before the lock, for the same reason as StartAsync: the account row is held only
        // for the decision and the writes that follow it.
        var user = await users.GetByIdWithDetailsAsync(userId, ct);
        if (user is null)
            return Errors.Auth.SessionInvalidated;

        // Any active member may select a shop they are assigned to; a regional manager works across
        // several stores. Role still governs what they may do once inside it.
        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == shopId && sm.IsActive);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        await using var transaction = await sessions.BeginAsync(ct);

        var authority = await sessions.LockUserAuthorityAsync(userId, ct);
        if (authority is null || !authority.CanHoldSession)
            return Errors.Auth.SessionInvalidated;

        var session = await sessions.FindAsync(userId, sessionId, ct);
        var now = clock.GetUtcNow();
        if (session is null ||
            !session.IsActive(now) ||
            session.SecurityVersion != authority.SecurityVersion ||
            !string.Equals(session.RecoveryEpoch, await recoveryEpochs.GetCurrentAsync(ct), StringComparison.Ordinal))
            return Errors.Auth.SessionInvalidated;

        membership.MarkUsed();

        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user, shopId);
        session.ChangeScope(activeShopId, now);

        var (accessToken, accessTokenExpiry) = await tokens.GenerateAccessTokenAsync(user, session, activeShopRole, ct);

        if (!await sessions.CommitAsync(ct))
            return Errors.Auth.SessionInvalidated;

        return new AccessScopeResult(accessToken, accessTokenExpiry, activeShopId, session.ScopeVersion, shops);
    }

    public async Task<ErrorOr<IReadOnlyList<AuthSessionDto>>> ListAsync(Guid userId, Guid currentSessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var listed = await sessions.ListAsync(userId, ct);

        return listed
            .Where(x => x.IsActive(now))
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new AuthSessionDto(
                x.Id,
                x.DeviceLabel,
                x.UserAgent,
                x.CreatedFromIp,
                x.ActiveShopId,
                x.CreatedAt,
                x.LastSeenAt,
                x.ExpiresAt,
                x.Id == currentSessionId))
            .ToList();
    }

    public async Task<ErrorOr<Success>> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var transaction = await sessions.BeginAsync(ct);

        // The account lock first, then the session: the order a renewal takes. Otherwise a renewal
        // committing in between could mint a credential this revocation never sees.
        if (await sessions.LockUserAuthorityAsync(userId, ct) is null)
            return Errors.Auth.SessionNotFound;

        // Scoped to the caller: another user's session is not theirs to see, so it is reported
        // missing rather than refused, which would confirm that it exists.
        var session = await sessions.LockAsync(userId, sessionId, ct);
        var now = clock.GetUtcNow();
        if (session is null || !session.IsActive(now))
            return Errors.Auth.SessionNotFound;

        await sessions.RevokeAsync(session, "user-revoked", now, ct);

        return await sessions.CommitAsync(ct) ? Result.Success : Errors.Auth.SessionNotFound;
    }

    public async Task<ErrorOr<Success>> RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        await using var transaction = await sessions.BeginAsync(ct);

        var authority = await sessions.LockUserAuthorityAsync(userId, ct);
        if (authority is null)
            return Errors.Auth.UserNotFound;

        await sessions.RevokeAllAsync(userId, exceptSessionId: null, "user-revoked-all", clock.GetUtcNow(), ct);

        return await sessions.CommitAsync(ct) ? Result.Success : Errors.Auth.SessionNotFound;
    }

    public Task<AuthResult> IssueForProvedAccountAsync(
        User user,
        AuthenticationProof proof,
        Guid? preferredShopId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(proof);

        // The proof must be about this account. There is no lock to catch a mismatch here, so a
        // caller that pairs the wrong proof with the wrong user is a bug, not a refusal.
        if (proof.UserId != user.Id)
            throw new ArgumentException("The proof names a different account.", nameof(proof));

        return IssueAsync(user, user.SecurityVersion, preferredShopId, ct);
    }

    private async Task<AuthResult> IssueAsync(
        User user,
        long securityVersion,
        Guid? preferredShopId,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user, preferredShopId);

        // The refresh credential is minted first so its configured lifetime also bounds the
        // session: a device cannot outlive the credential that renews it.
        var secret = tokens.CreateRefreshSecret();

        var session = AuthSession.Start(
            user.Id,
            securityVersion,
            await recoveryEpochs.GetCurrentAsync(ct),
            activeShopId,
            devices.Current,
            now,
            secret.ExpiresAt);

        await sessions.AddAsync(session, ct);
        await refreshTokens.AddAsync(
            RefreshToken.Issue(user.Id, session.Id, securityVersion, secret.TokenHash, secret.ExpiresAt),
            ct);

        var (accessToken, accessTokenExpiry) = await tokens.GenerateAccessTokenAsync(user, session, activeShopRole, ct);

        return Compose(user, session, accessToken, accessTokenExpiry, secret.Token, secret.ExpiresAt, activeShopId, shops);
    }

    /// <summary>
    /// Finds the credential by its hash and takes the locks a decision about it needs, in the one
    /// order every security transition uses: account, session, credential. Everything returned was
    /// read under those locks. Null when the credential is unknown or predates sessions; neither is
    /// grounds to revoke anything, since an unknown value proves nothing about any session.
    /// </summary>
    private async Task<PresentedCredential?> LockPresentedAsync(string refreshToken, CancellationToken ct)
    {
        var owner = await refreshTokens.FindOwnerByHashAsync(RefreshTokenSecrets.Hash(refreshToken), ct);
        if (owner?.AuthSessionId is not Guid sessionId)
            return null;

        var authority = await sessions.LockUserAuthorityAsync(owner.UserId, ct);
        var session = await sessions.LockAsync(owner.UserId, sessionId, ct);
        var credential = await refreshTokens.LockAsync(owner.TokenId, ct);

        // Gone between the lookup and the lock: the account or session was deleted, taking its
        // credentials with it.
        if (session is null || credential is null)
            return null;

        return new PresentedCredential(authority, session, credential, clock.GetUtcNow());
    }

    /// <summary>
    /// A credential that was already exchanged has been presented again. Either the client lost the
    /// response and retried, or someone else holds a copy; the server cannot tell which, so the
    /// session and everything it issued end. The revocation is committed before the refusal is
    /// returned — rolling it back with the request would undo the one thing this path is for.
    /// </summary>
    /// <remarks>
    /// Deliberately not cancellable. Once the replay is detected, the client closing its tab must
    /// not abort the revocation: the caller may be the victim presenting the credential an attacker
    /// already exchanged, and cancelling here would leave the attacker's successor live.
    /// </remarks>
    private async Task<Error> EndForReuseAsync(PresentedCredential presented)
    {
        await sessions.RevokeAsync(presented.Session, RefreshReuse, presented.Now, CancellationToken.None);
        var committed = await sessions.CommitAsync(CancellationToken.None);

        LogRefreshReuse(presented.Session.UserId, presented.Session.Id, presented.Credential.Id, committed);

        return Errors.Auth.InvalidRefreshToken;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Consumed refresh credential {CredentialId} presented again; session {SessionId} of user {UserId} ended (committed: {Committed}).")]
    private partial void LogRefreshReuse(Guid userId, Guid sessionId, Guid credentialId, bool committed);

    private sealed record PresentedCredential(UserAuthority? Authority, AuthSession Session, RefreshToken Credential, DateTimeOffset Now);

    private static AuthResult Compose(
        User user,
        AuthSession session,
        string accessToken,
        DateTimeOffset accessTokenExpiry,
        string refreshToken,
        DateTimeOffset refreshTokenExpiry,
        Guid? activeShopId,
        IReadOnlyList<UserShopDto> shops) =>
        new(
            accessToken,
            refreshToken,
            accessTokenExpiry,
            refreshTokenExpiry,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language),
            activeShopId,
            shops,
            session.Id);
}
