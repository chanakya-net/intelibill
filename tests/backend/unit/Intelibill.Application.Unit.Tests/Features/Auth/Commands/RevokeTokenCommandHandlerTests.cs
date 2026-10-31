using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.Commands.RevokeToken;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RevokeToken;

public class RevokeTokenCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuthSessionRepository _sessions = Substitute.For<IAuthSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        _handler = new RevokeTokenCommandHandler(_refreshTokenRepository, _sessions, _unitOfWork, new FixedClock(Now));
    }

    [Fact]
    public async Task HandleAsync_InvalidToken_ReturnsError()
    {
        var command = new RevokeTokenCommand("invalid");
        _refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, Arg.Any<CancellationToken>())
                               .Returns((Domain.Entities.RefreshToken?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidRefreshToken.Code);
    }

    /// <summary>
    /// Signing out ends the device, not just the credential that renews it. Otherwise the session
    /// row lingers until it expires, the owner's device list fills with devices nobody is signed in
    /// on, and the access token in hand keeps working for the rest of its life.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ValidToken_EndsTheDeviceSessionAndItsCredential()
    {
        var userId = Guid.NewGuid();
        var session = AuthSession.Start(userId, 1, "1", null, DeviceMetadata.Unknown, Now, Now.AddDays(7));
        var token = Domain.Entities.RefreshToken.Create(userId, "valid", Now.AddDays(7));
        token.BindSession(session.Id);

        _refreshTokenRepository.GetActiveByTokenAsync("valid", Arg.Any<CancellationToken>()).Returns(token);
        _sessions.FindAsync(userId, session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var result = await _handler.HandleAsync(new RevokeTokenCommand("valid"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(token.IsRevoked);
        await _sessions.Received(1).RevokeAsync(session, "signed-out", Now, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A credential written before sessions existed names none. Revoking it is all there is to do;
    /// no session is invented for it.
    /// </summary>
    [Fact]
    public async Task HandleAsync_CredentialWithNoSession_RevokesItAndLooksForNothingElse()
    {
        var token = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "legacy", Now.AddDays(7));
        _refreshTokenRepository.GetActiveByTokenAsync("legacy", Arg.Any<CancellationToken>()).Returns(token);

        var result = await _handler.HandleAsync(new RevokeTokenCommand("legacy"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(token.IsRevoked);
        await _sessions.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
