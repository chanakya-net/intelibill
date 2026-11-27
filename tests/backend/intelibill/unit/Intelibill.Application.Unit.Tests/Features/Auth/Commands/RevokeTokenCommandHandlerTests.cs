using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RevokeToken;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RevokeToken;

/// <summary>
/// Sign-out is a session operation, taken under the same locks as a renewal so the two cannot
/// interleave. The handler forwards the credential and decides nothing itself; what sign-out ends
/// is covered in <c>AuthSessionServiceTests</c> and against a real database.
/// </summary>
public class RevokeTokenCommandHandlerTests
{
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        _handler = new RevokeTokenCommandHandler(_sessions);
    }

    [Fact]
    public async Task HandleAsync_SignsOutTheSessionThatHoldsTheCredential()
    {
        _sessions.SignOutAsync("valid", Arg.Any<CancellationToken>()).Returns(Result.Success);

        var result = await _handler.HandleAsync(new RevokeTokenCommand("valid"), CancellationToken.None);

        Assert.False(result.IsError);
        await _sessions.Received(1).SignOutAsync("valid", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassesTheRefusalBackUnchanged()
    {
        _sessions.SignOutAsync("invalid", Arg.Any<CancellationToken>()).Returns(Errors.Auth.InvalidRefreshToken);

        var result = await _handler.HandleAsync(new RevokeTokenCommand("invalid"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.InvalidRefreshToken.Code, result.FirstError.Code);
    }
}
