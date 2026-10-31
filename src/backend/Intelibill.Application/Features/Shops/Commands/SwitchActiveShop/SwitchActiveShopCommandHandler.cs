using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;

namespace Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;

/// <summary>
/// Moves the device that asked into another of its shops. It reissues that device's access token
/// and nothing else: no refresh credential is created, and no other device's scope moves.
/// </summary>
public sealed class SwitchActiveShopCommandHandler(IAuthSessionService sessions)
{
    public Task<ErrorOr<AccessScopeResult>> HandleAsync(SwitchActiveShopCommand command, CancellationToken cancellationToken) =>
        sessions.RescopeAsync(command.UserId, command.SessionId, command.ShopId, cancellationToken);
}
