using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Auth.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Services;

/// <summary>
/// The rules that decide whether a session may be created, continued or rescoped. Every path here
/// re-reads the account's authority after taking its lock, because the gap between checking a
/// credential and creating a session is where a password reset or a disablement would otherwise be
/// overtaken.
/// </summary>
public class AuthSessionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private const string Epoch = "1";

    private readonly IAuthSessionRepository _sessions = Substitute.For<IAuthSessionRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IDeviceMetadataProvider _devices = Substitute.For<IDeviceMetadataProvider>();

    private AuthSessionService Service()
    {
        _sessions.BeginAsync(Arg.Any<CancellationToken>()).Returns(Substitute.For<IAsyncDisposable>());
        _sessions.CommitAsync(Arg.Any<CancellationToken>()).Returns(true);
        _devices.Current.Returns(DeviceMetadata.Unknown);
        _tokens.CreateRefreshToken(Arg.Any<Guid>())
            .Returns(call => RefreshToken.Create(call.Arg<Guid>(), $"refresh-{Guid.NewGuid():N}", Now.AddDays(7)));
        _tokens.GenerateAccessTokenAsync(Arg.Any<User>(), Arg.Any<AuthSession>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(("access-token", Now.AddMinutes(15)));

        return new AuthSessionService(_sessions, _users, _refreshTokens, _tokens, new FixedEpoch(Epoch), _devices, new FixedClock(Now));
    }

    private static User VerifiedOwner(out Shop shop, ShopRole role = ShopRole.Owner, MembershipStatus status = MembershipStatus.Active)
    {
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, role, true);
        if (status != MembershipStatus.Active)
            membership.ChangeAuthority(role, status);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);
        return user;
    }

    private static UserAuthority Authority(User user, bool loginEnabled = true, long? securityVersion = null) =>
        new(user.Id, securityVersion ?? user.SecurityVersion, loginEnabled, user.Email is not null, user.IsEmailVerified);

    private void AccountIs(User user, UserAuthority? authority = null)
    {
        _sessions.LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>()).Returns(authority ?? Authority(user));
        _users.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
    }

    private AuthSession LiveSession(
        User user,
        Guid? activeShopId,
        string epoch = Epoch,
        long? securityVersion = null,
        DateTimeOffset? startedAt = null)
    {
        var started = startedAt ?? Now;
        var session = AuthSession.Start(
            user.Id,
            securityVersion ?? user.SecurityVersion,
            epoch,
            activeShopId,
            DeviceMetadata.Unknown,
            started,
            started.AddDays(7));
        _sessions.FindAsync(user.Id, session.Id, Arg.Any<CancellationToken>()).Returns(session);
        return session;
    }

    // ── Starting a session ───────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_WithACurrentProof_CreatesASessionAndItsRefreshFamily()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);

        var result = await Service().StartAsync(
            AuthenticationProof.FromPassword(user.Id, user.SecurityVersion, Now),
            preferredShopId: null,
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shop.Id, result.Value.ActiveShopId);
        Assert.NotNull(result.Value.SessionId);
        await _sessions.Received(1).AddAsync(
            Arg.Is<AuthSession>(s => s.UserId == user.Id && s.RecoveryEpoch == Epoch && s.ActiveShopId == shop.Id),
            Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).AddAsync(
            Arg.Is<RefreshToken>(t => t.AuthSessionId != null && t.SecurityVersion == user.SecurityVersion),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Password verification is deliberately slow, so a reset can commit while it runs. The proof
    /// names the version it was checked against and is refused once the account has moved past it,
    /// rather than being stamped with whatever the version is by the time the lock is taken.
    /// </summary>
    [Fact]
    public async Task StartAsync_WhenTheAccountMovedPastTheProof_RefusesIt()
    {
        var user = VerifiedOwner(out _);
        AccountIs(user, Authority(user, securityVersion: user.SecurityVersion + 1));

        var result = await Service().StartAsync(
            AuthenticationProof.FromPassword(user.Id, user.SecurityVersion, Now),
            preferredShopId: null,
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidCredentials.Code, result.FirstError.Code);
        await _sessions.DidNotReceive().AddAsync(Arg.Any<AuthSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_WhenTheAccountWasDisabled_RefusesTheProof()
    {
        var user = VerifiedOwner(out _);
        AccountIs(user, Authority(user, loginEnabled: false));

        var result = await Service().StartAsync(
            AuthenticationProof.FromPassword(user.Id, user.SecurityVersion, Now),
            preferredShopId: null,
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidCredentials.Code, result.FirstError.Code);
        await _sessions.DidNotReceive().AddAsync(Arg.Any<AuthSession>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A registration proof belongs to an account this same transaction is inserting, so there is no
    /// earlier version to compare it against.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithARegistrationProof_DoesNotCompareVersions()
    {
        var user = VerifiedOwner(out _);
        AccountIs(user, Authority(user, securityVersion: 9));

        var result = await Service().StartAsync(
            AuthenticationProof.FromRegistration(user.Id, Now),
            preferredShopId: null,
            CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task StartAsync_WhenTheTransactionCannotCommit_ReportsFailureRatherThanAToken()
    {
        var user = VerifiedOwner(out _);
        AccountIs(user);
        var service = Service();
        _sessions.CommitAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await service.StartAsync(
            AuthenticationProof.FromPassword(user.Id, user.SecurityVersion, Now),
            preferredShopId: null,
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    // ── Rotating inside a session ────────────────────────────────────────────────

    [Fact]
    public async Task RotateAsync_KeepsTheSessionIdSoTheDeviceStaysTheSameDevice()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        GivenRefreshCredential(user, session.Id);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(session.Id, result.Value.SessionId);
        Assert.Equal(shop.Id, result.Value.ActiveShopId);
    }

    /// <summary>
    /// Renewal moves the session's window out with the credential it mints, so a device that keeps
    /// being used is not hard-signed-out a week after its first sign-in.
    /// </summary>
    [Fact]
    public async Task RotateAsync_MovesTheSessionWindowOutWithTheRenewedCredential()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        // Started five days ago, so the credential this renewal mints reaches further out than the
        // window the session was given at sign-in.
        var session = LiveSession(user, shop.Id, startedAt: Now.AddDays(-5));
        var originalExpiry = session.ExpiresAt;
        GivenRefreshCredential(user, session.Id);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(result.Value.RefreshTokenExpiresAt, session.ExpiresAt);
        Assert.True(session.ExpiresAt > originalExpiry);
    }

    /// <summary>
    /// A refresh row written before sessions existed names none. It is refused rather than adopted
    /// by a session invented for it, which is what makes the cutover a fresh sign-in.
    /// </summary>
    [Fact]
    public async Task RotateAsync_WhenTheCredentialNamesNoSession_RefusesIt()
    {
        var user = VerifiedOwner(out _);
        var legacy = RefreshToken.Create(user.Id, "held", Now.AddDays(7));
        legacy.BindSecurityVersion(user.SecurityVersion);
        _refreshTokens.GetActiveByTokenAsync("held", Arg.Any<CancellationToken>()).Returns(legacy);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task RotateAsync_WhenTheSessionWasRevoked_RefusesTheCredential()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        session.Revoke("user-revoked", Now);
        GivenRefreshCredential(user, session.Id);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
    }

    /// <summary>
    /// A database restored from before an epoch change hands back the sessions it held. They name
    /// the old epoch, so they are refused and the people holding them authenticate again.
    /// </summary>
    [Fact]
    public async Task RotateAsync_WhenTheSessionNamesAnOlderRecoveryEpoch_RefusesTheCredential()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id, epoch: "0");
        GivenRefreshCredential(user, session.Id);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task RotateAsync_WhenTheAccountMovedToANewSecurityVersion_RefusesTheCredential()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user, Authority(user, securityVersion: user.SecurityVersion + 1));
        var session = LiveSession(user, shop.Id);
        GivenRefreshCredential(user, session.Id);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
    }

    // ── Rescoping one device ─────────────────────────────────────────────────────

    [Fact]
    public async Task RescopeAsync_WhenTheCallerIsAnActiveMember_MovesThatSessionsScope()
    {
        var user = VerifiedOwner(out var shop, ShopRole.Manager);
        AccountIs(user);
        var session = LiveSession(user, activeShopId: null);

        var result = await Service().RescopeAsync(user.Id, session.Id, shop.Id, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shop.Id, result.Value.ActiveShopId);
        Assert.Equal(shop.Id, session.ActiveShopId);
        Assert.Equal(1, result.Value.ScopeVersion);
    }

    [Fact]
    public async Task RescopeAsync_WhenTheCallerHasNoMembershipInTheShop_IsRefused()
    {
        var user = VerifiedOwner(out _);
        AccountIs(user);
        var session = LiveSession(user, activeShopId: null);

        var result = await Service().RescopeAsync(user.Id, session.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task RescopeAsync_WhenTheMembershipIsSuspended_IsRefused()
    {
        var user = VerifiedOwner(out var shop, ShopRole.Manager, MembershipStatus.Suspended);
        AccountIs(user);
        var session = LiveSession(user, activeShopId: null);

        var result = await Service().RescopeAsync(user.Id, session.Id, shop.Id, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    /// <summary>
    /// Rescoping reuses the session it was asked for and creates nothing, so choosing a shop can
    /// never hand out a credential a sign-in did not earn.
    /// </summary>
    [Fact]
    public async Task RescopeAsync_CreatesNoSessionAndNoRefreshCredential()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, activeShopId: null);

        _ = await Service().RescopeAsync(user.Id, session.Id, shop.Id, CancellationToken.None);

        await _sessions.DidNotReceive().AddAsync(Arg.Any<AuthSession>(), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescopeAsync_WhenTheSessionBelongsToAnotherAccount_IsRefused()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        _sessions.FindAsync(user.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AuthSession?)null);

        var result = await Service().RescopeAsync(user.Id, Guid.NewGuid(), shop.Id, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.SessionInvalidated.Code, result.FirstError.Code);
    }

    // ── Listing and revoking ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_MarksTheRequestingDeviceAndHidesEndedOnes()
    {
        var user = VerifiedOwner(out var shop);
        var current = AuthSession.Start(user.Id, 1, Epoch, shop.Id, new DeviceMetadata("Counter tablet", "203.0.113.4"), Now, Now.AddDays(7));
        var other = AuthSession.Start(user.Id, 1, Epoch, null, DeviceMetadata.Unknown, Now.AddMinutes(-5), Now.AddDays(7));
        var ended = AuthSession.Start(user.Id, 1, Epoch, null, DeviceMetadata.Unknown, Now, Now.AddDays(7));
        ended.Revoke("user-revoked", Now);
        _sessions.ListAsync(user.Id, Arg.Any<CancellationToken>()).Returns([current, other, ended]);

        var result = await Service().ListAsync(user.Id, current.Id, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(current.Id, result.Value[0].SessionId);
        Assert.True(result.Value[0].IsCurrent);
        Assert.Equal("Counter tablet", result.Value[0].UserAgent);
        Assert.False(result.Value[1].IsCurrent);
    }

    /// <summary>
    /// Another account's session is reported missing rather than refused: saying "not yours" would
    /// confirm that the id exists.
    /// </summary>
    [Fact]
    public async Task RevokeAsync_WhenTheSessionBelongsToAnotherAccount_ReportsItMissing()
    {
        var userId = Guid.NewGuid();
        _sessions.FindAsync(userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AuthSession?)null);

        var result = await Service().RevokeAsync(userId, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.SessionNotFound.Code, result.FirstError.Code);
        await _sessions.DidNotReceive().RevokeAsync(Arg.Any<AuthSession>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_EndsTheNamedDevice()
    {
        var user = VerifiedOwner(out var shop);
        var session = LiveSession(user, shop.Id);

        var result = await Service().RevokeAsync(user.Id, session.Id, CancellationToken.None);

        Assert.False(result.IsError);
        await _sessions.Received(1).RevokeAsync(session, "user-revoked", Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAllAsync_SparesNoDeviceIncludingTheCaller()
    {
        var user = VerifiedOwner(out _);
        AccountIs(user);

        var result = await Service().RevokeAllAsync(user.Id, CancellationToken.None);

        Assert.False(result.IsError);
        await _sessions.Received(1).RevokeAllAsync(user.Id, null, "user-revoked-all", Now, Arg.Any<CancellationToken>());
    }

    private void GivenRefreshCredential(User user, Guid sessionId)
    {
        var held = RefreshToken.Create(user.Id, "held", Now.AddDays(7));
        held.BindSecurityVersion(user.SecurityVersion);
        held.BindSession(sessionId);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(held, user);
        _refreshTokens.GetActiveByTokenAsync("held", Arg.Any<CancellationToken>()).Returns(held);
    }

    /// <summary>
    /// A stub rather than a substitute: a ValueTask-returning member must be consumed once, which a
    /// configured substitute cannot promise across the several calls one operation makes.
    /// </summary>
    private sealed class FixedEpoch(string epoch) : IRecoveryEpochProvider
    {
        public ValueTask<string> GetCurrentAsync(CancellationToken ct = default) => ValueTask.FromResult(epoch);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
