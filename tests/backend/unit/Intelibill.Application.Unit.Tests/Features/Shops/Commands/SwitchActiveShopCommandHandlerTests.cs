using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.SwitchActiveShop;

/// <summary>
/// Switching shops is a change to one device's session scope, so the handler hands the session
/// service the device that asked and nothing else. Which memberships permit a switch, and the fact
/// that no other device moves, are covered in <see cref="Auth.Services.AuthSessionServiceTests"/>.
/// </summary>
public class SwitchActiveShopCommandHandlerTests
{
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();

    [Fact]
    public async Task HandleAsync_RescopesTheRequestingSessionOnly()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _sessions.RescopeAsync(userId, sessionId, shopId, Arg.Any<CancellationToken>())
            .Returns(AuthResults.Scope(shopId));

        var result = await new SwitchActiveShopCommandHandler(_sessions)
            .HandleAsync(new SwitchActiveShopCommand(userId, sessionId, shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shopId, result.Value.ActiveShopId);
        await _sessions.Received(1).RescopeAsync(userId, sessionId, shopId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PassesTheRefusalBackUnchanged()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _sessions.RescopeAsync(userId, sessionId, shopId, Arg.Any<CancellationToken>())
            .Returns(Errors.Shop.MembershipNotFound);

        var result = await new SwitchActiveShopCommandHandler(_sessions)
            .HandleAsync(new SwitchActiveShopCommand(userId, sessionId, shopId), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }
}
