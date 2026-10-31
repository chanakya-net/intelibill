using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;

namespace Intelibill.Application.Features.Auth.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler(IAuthSessionService sessions)
{
    public Task<ErrorOr<AuthResult>> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken) =>
        sessions.RotateAsync(command.RefreshToken, command.PreferredShopId, cancellationToken);
}
