using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Auth.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

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
        _tokens.CreateRefreshSecret().Returns(_ =>
        {
            var token = $"refresh-{Guid.NewGuid():N}";
            return new RefreshSecret(token, RefreshTokenSecrets.Hash(token), Now.AddDays(7));
        });
        _tokens.GenerateAccessTokenAsync(Arg.Any<User>(), Arg.Any<AuthSession>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(("access-token", Now.AddMinutes(15)));

        return new AuthSessionService(
            _sessions,
            _users,
            _refreshTokens,
            _tokens,
            new FixedEpoch(Epoch),
            _devices,
            new FixedClock(Now),
            NullLogger<AuthSessionService>.Instance);
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
        _sessions.LockAsync(user.Id, session.Id, Arg.Any<CancellationToken>()).Returns(session);
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
            Arg.Is<RefreshToken>(t =>
                t.AuthSessionId == result.Value.SessionId &&
                t.ParentTokenId == null &&
                t.SecurityVersion == user.SecurityVersion &&
                t.TokenHash == RefreshTokenSecrets.Hash(result.Value.RefreshToken)),
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

    /// <summary>
    /// The presented credential is used up and exactly one successor joins the chain behind it, in
    /// the same session. What the client receives is the secret; what is stored is only its hash.
    /// </summary>
    [Fact]
    public async Task RotateAsync_ConsumesThePresentedCredentialAndAddsOneChild()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(Now, held.ConsumedAt);
        await _refreshTokens.Received(1).AddAsync(
            Arg.Is<RefreshToken>(t =>
                t.ParentTokenId == held.Id &&
                t.AuthSessionId == session.Id &&
                t.TokenHash == RefreshTokenSecrets.Hash(result.Value.RefreshToken) &&
                t.TokenHash != result.Value.RefreshToken),
            Arg.Any<CancellationToken>());
        await _sessions.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

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
    /// The lock order every security transition shares: account, then session, then credential.
    /// Taking them any other way round is how a renewal and a sign-out deadlock.
    /// </summary>
    [Fact]
    public async Task RotateAsync_TakesTheLocksInOrder()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);

        await Service().RotateAsync("held", null, CancellationToken.None);

        Received.InOrder(() =>
        {
            _ = _sessions.LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>());
            _ = _sessions.LockAsync(user.Id, session.Id, Arg.Any<CancellationToken>());
            _ = _refreshTokens.LockAsync(held.Id, Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// A credential that was already exchanged has come back. The server cannot tell a lost
    /// response from a stolen copy, so the session ends — and that revocation is committed even
    /// though the request itself is refused.
    /// </summary>
    [Fact]
    public async Task RotateAsync_ConsumedCredential_EndsItsSessionAndCommitsThat()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);
        held.Rotate(RefreshTokenSecrets.Hash("successor"), Now.AddDays(7), Now.AddMinutes(-1));

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
        await _sessions.Received(1).RevokeAsync(session, "refresh-reuse", Now, Arg.Any<CancellationToken>());
        await _sessions.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _tokens.DidNotReceive().GenerateAccessTokenAsync(
            Arg.Any<User>(), Arg.Any<AuthSession>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The replay may be the victim presenting the credential an attacker already exchanged. If the
    /// victim closes the tab mid-request, the revocation must still commit, or the attacker's
    /// successor stays live.
    /// </summary>
    [Fact]
    public async Task RotateAsync_ConsumedCredential_RevocationIgnoresTheRequestAbort()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);
        held.Rotate(RefreshTokenSecrets.Hash("successor"), Now.AddDays(7), Now.AddMinutes(-1));
        using var requestAborted = new CancellationTokenSource();

        await Service().RotateAsync("held", null, requestAborted.Token);

        await _sessions.Received(1).RevokeAsync(session, "refresh-reuse", Now, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        await _sessions.Received(1).CommitAsync(Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
    }

    /// <summary>
    /// A value that was never issued says nothing about any session, so it ends nothing — otherwise
    /// guessing would be a way to sign other people out.
    /// </summary>
    [Fact]
    public async Task RotateAsync_UnknownCredential_RevokesNothing()
    {
        _refreshTokens.FindOwnerByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshTokenOwner?)null);

        var result = await Service().RotateAsync("never-issued", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
        await _sessions.DidNotReceive().LockUserAuthorityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().RevokeAsync(Arg.Any<AuthSession>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateAsync_AtExactExpiry_IsRefused()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id, expiresAt: Now);

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
        Assert.Null(held.ConsumedAt);
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RotateAsync_RevokedCredential_IsRefusedWithoutEndingAnything()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);
        held.Revoke("signed-out", Now.AddMinutes(-1));

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        await _sessions.DidNotReceive().RevokeAsync(Arg.Any<AuthSession>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The access token is signed inside the transaction. If signing fails nothing commits, so the
    /// presented credential is still the live one and no successor exists to be orphaned.
    /// </summary>
    [Fact]
    public async Task RotateAsync_WhenSigningFails_DoesNotCommit()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        GivenRefreshCredential(user, session.Id);
        var service = Service();
        _tokens.GenerateAccessTokenAsync(Arg.Any<User>(), Arg.Any<AuthSession>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Signing key unavailable."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateAsync("held", null, CancellationToken.None));

        await _sessions.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refresh row written before sessions existed names none. It is refused rather than adopted
    /// by a session invented for it, which is what makes the cutover a fresh sign-in.
    /// </summary>
    [Fact]
    public async Task RotateAsync_WhenTheCredentialNamesNoSession_RefusesItWithoutLocking()
    {
        _refreshTokens.FindOwnerByHashAsync(RefreshTokenSecrets.Hash("held"), Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenOwner(Guid.NewGuid(), Guid.NewGuid(), AuthSessionId: null));

        var result = await Service().RotateAsync("held", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
        await _sessions.DidNotReceive().LockUserAuthorityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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

    // ── Signing out ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignOutAsync_EndsTheSessionTheCredentialBelongsTo()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);

        var result = await Service().SignOutAsync("held", CancellationToken.None);

        Assert.False(result.IsError);
        Received.InOrder(() =>
        {
            _ = _sessions.LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>());
            _ = _sessions.LockAsync(user.Id, session.Id, Arg.Any<CancellationToken>());
            _ = _refreshTokens.LockAsync(held.Id, Arg.Any<CancellationToken>());
            _ = _sessions.RevokeAsync(session, "signed-out", Now, Arg.Any<CancellationToken>());
            _ = _sessions.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// Signing out with a credential that was already exchanged is the same signal as renewing with
    /// one, and gets the same answer: the session ends as a replay, and the caller is refused.
    /// </summary>
    [Fact]
    public async Task SignOutAsync_WithAConsumedCredential_EndsTheSessionAsAReplay()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);
        var held = GivenRefreshCredential(user, session.Id);
        held.Rotate(RefreshTokenSecrets.Hash("successor"), Now.AddDays(7), Now.AddMinutes(-1));

        var result = await Service().SignOutAsync("held", CancellationToken.None);

        Assert.True(result.IsError);
        await _sessions.Received(1).RevokeAsync(session, "refresh-reuse", Now, Arg.Any<CancellationToken>());
        await _sessions.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignOutAsync_UnknownCredential_RevokesNothing()
    {
        _refreshTokens.FindOwnerByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshTokenOwner?)null);

        var result = await Service().SignOutAsync("never-issued", CancellationToken.None);

        Assert.True(result.IsError);
        await _sessions.DidNotReceive().RevokeAsync(Arg.Any<AuthSession>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
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
        var user = VerifiedOwner(out _);
        AccountIs(user);
        _sessions.LockAsync(user.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AuthSession?)null);

        var result = await Service().RevokeAsync(user.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.SessionNotFound.Code, result.FirstError.Code);
        await _sessions.DidNotReceive().RevokeAsync(Arg.Any<AuthSession>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_EndsTheNamedDeviceUnderTheAccountLock()
    {
        var user = VerifiedOwner(out var shop);
        AccountIs(user);
        var session = LiveSession(user, shop.Id);

        var result = await Service().RevokeAsync(user.Id, session.Id, CancellationToken.None);

        Assert.False(result.IsError);
        Received.InOrder(() =>
        {
            _ = _sessions.LockUserAuthorityAsync(user.Id, Arg.Any<CancellationToken>());
            _ = _sessions.LockAsync(user.Id, session.Id, Arg.Any<CancellationToken>());
            _ = _sessions.RevokeAsync(session, "user-revoked", Now, Arg.Any<CancellationToken>());
        });
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

    private RefreshToken GivenRefreshCredential(User user, Guid sessionId, DateTimeOffset? expiresAt = null)
    {
        var held = RefreshToken.Issue(user.Id, sessionId, user.SecurityVersion, RefreshTokenSecrets.Hash("held"), expiresAt ?? Now.AddDays(7));
        _refreshTokens.FindOwnerByHashAsync(RefreshTokenSecrets.Hash("held"), Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenOwner(held.Id, user.Id, sessionId));
        _refreshTokens.LockAsync(held.Id, Arg.Any<CancellationToken>()).Returns(held);
        return held;
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
