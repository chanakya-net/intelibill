using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Services;

/// <summary>
/// Owns the one transaction a session may be born in. Every path through here takes the account
/// lock first and re-reads the account's authority from the database, because the gap between
/// checking a credential and creating a session is exactly where a password reset or a disablement
/// would otherwise be overtaken.
/// </summary>
internal sealed class AuthSessionService(
    IAuthSessionRepository sessions,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ITokenService tokens,
    IRecoveryEpochProvider recoveryEpochs,
    IDeviceMetadataProvider devices,
    TimeProvider clock) : IAuthSessionService
{
    public async Task<ErrorOr<AuthResult>> StartAsync(
        AuthenticationProof proof,
        Guid? preferredShopId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proof);

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

        var user = await users.GetByIdWithDetailsAsync(proof.UserId, ct);
        if (user is null)
            return Errors.Auth.InvalidCredentials;

        var result = await IssueAsync(user, authority.SecurityVersion, preferredShopId, ct);
        if (!await sessions.CommitAsync(ct))
            return Errors.Auth.InvalidCredentials;

        return result;
    }

    public async Task<ErrorOr<AuthResult>> RotateAsync(string refreshToken, Guid? preferredShopId, CancellationToken ct)
    {
        await using var transaction = await sessions.BeginAsync(ct);

        var existing = await refreshTokens.GetActiveByTokenAsync(refreshToken, ct);

        // A refresh row written before sessions existed names none. It is refused rather than
        // adopted by a session invented for it, which is what makes the cutover a fresh sign-in.
        if (existing?.AuthSessionId is null)
            return Errors.Auth.InvalidRefreshToken;

        var authority = await sessions.LockUserAuthorityAsync(existing.UserId, ct);
        if (authority is null || !authority.CanHoldSession || existing.SecurityVersion != authority.SecurityVersion)
            return Errors.Auth.InvalidRefreshToken;

        var session = await sessions.FindAsync(existing.UserId, existing.AuthSessionId.Value, ct);
        var now = clock.GetUtcNow();
        if (session is null ||
            !session.IsActive(now) ||
            session.SecurityVersion != authority.SecurityVersion ||
            !string.Equals(session.RecoveryEpoch, await recoveryEpochs.GetCurrentAsync(ct), StringComparison.Ordinal))
            return Errors.Auth.InvalidRefreshToken;

        existing.Revoke();
        refreshTokens.Update(existing);

        var user = existing.User;

        // Renew in the shop this device was already working in. Only if that membership has gone
        // does selection fall back, and then the scope version moves so the old access token stops
        // authorising anything against the shop it named.
        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user, session.ActiveShopId ?? preferredShopId);
        session.ChangeScope(activeShopId, now);

        var replacement = await BindRefreshAsync(user.Id, session, authority.SecurityVersion, ct);
        var (accessToken, accessTokenExpiry) = await tokens.GenerateAccessTokenAsync(user, session, activeShopRole, ct);

        if (!await sessions.CommitAsync(ct))
            return Errors.Auth.InvalidRefreshToken;

        return Compose(user, session, accessToken, accessTokenExpiry, replacement, activeShopId, shops);
    }

    public async Task<ErrorOr<AccessScopeResult>> RescopeAsync(Guid userId, Guid sessionId, Guid shopId, CancellationToken ct)
    {
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

        var user = await users.GetByIdWithDetailsAsync(userId, ct);
        if (user is null)
            return Errors.Auth.SessionInvalidated;

        // Any active member may select a shop they are assigned to; a regional manager works across
        // several stores. Role still governs what they may do once inside it.
        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == shopId && sm.IsActive);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

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

        // Scoped to the caller: another user's session is not theirs to see, so it is reported
        // missing rather than refused, which would confirm that it exists.
        var session = await sessions.FindAsync(userId, sessionId, ct);
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

    public Task<AuthResult> IssueWithinTransactionAsync(User user, Guid? preferredShopId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
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
        var refreshToken = tokens.CreateRefreshToken(user.Id);
        refreshToken.BindSecurityVersion(securityVersion);

        var session = AuthSession.Start(
            user.Id,
            securityVersion,
            await recoveryEpochs.GetCurrentAsync(ct),
            activeShopId,
            devices.Current,
            now,
            refreshToken.ExpiresAt);

        await sessions.AddAsync(session, ct);

        refreshToken.BindSession(session.Id);
        await refreshTokens.AddAsync(refreshToken, ct);

        var (accessToken, accessTokenExpiry) = await tokens.GenerateAccessTokenAsync(user, session, activeShopRole, ct);

        return Compose(user, session, accessToken, accessTokenExpiry, refreshToken, activeShopId, shops);
    }

    private async Task<RefreshToken> BindRefreshAsync(Guid userId, AuthSession session, long securityVersion, CancellationToken ct)
    {
        var replacement = tokens.CreateRefreshToken(userId);
        replacement.BindSecurityVersion(securityVersion);
        replacement.BindSession(session.Id);
        await refreshTokens.AddAsync(replacement, ct);
        return replacement;
    }

    private static AuthResult Compose(
        User user,
        AuthSession session,
        string accessToken,
        DateTimeOffset accessTokenExpiry,
        RefreshToken refreshToken,
        Guid? activeShopId,
        IReadOnlyList<UserShopDto> shops) =>
        new(
            accessToken,
            refreshToken.Token,
            accessTokenExpiry,
            refreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language),
            activeShopId,
            shops,
            session.Id);
}
