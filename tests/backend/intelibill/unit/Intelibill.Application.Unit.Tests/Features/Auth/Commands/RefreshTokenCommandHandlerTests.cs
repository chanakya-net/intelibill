using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RefreshToken;
using Intelibill.Application.Features.Auth.DTOs;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Rotation is a session operation: the handler forwards the credential and the shop the expiring
/// token was scoped to, and decides nothing itself. What rotation accepts and refuses is covered
/// against a real database in the integration suite.
/// </summary>
public class RefreshTokenCommandHandlerTests
{
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        _handler = new RefreshTokenCommandHandler(_sessions);
    }

    [Fact]
    public async Task HandleAsync_RotatesWithinTheSessionThatHoldsTheCredential()
    {
        var preferredShop = Guid.NewGuid();
        var command = new RefreshTokenCommand("valid", preferredShop);
        var user = VerifiedUsers.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        _sessions.RotateAsync("valid", preferredShop, Arg.Any<CancellationToken>()).Returns(AuthResults.For(user));

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        await _sessions.Received(1).RotateAsync("valid", preferredShop, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassesTheRefusalBackUnchanged()
    {
        var command = new RefreshTokenCommand("invalid");
        _sessions.RotateAsync("invalid", null, Arg.Any<CancellationToken>())
            .Returns(Errors.Auth.InvalidRefreshToken);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.InvalidRefreshToken.Code);
    }
}
