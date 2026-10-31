using ErrorOr;
using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Application.Features.Auth.Commands.RevokeToken;

/// <summary>
/// Ordinary sign-out. It ends the device's session, not just the credential that renews it:
/// otherwise the session row would linger until it expired, the owner's device list would fill with
/// devices nobody is signed in on, and the access token already in hand would keep working for the
/// rest of its life — which is exactly what someone signing out of a shared terminal is avoiding.
/// </summary>
public sealed class RevokeTokenCommandHandler(IAuthSessionService sessions)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RevokeTokenCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sessions.SignOutAsync(command.RefreshToken, cancellationToken);
        return result.IsError ? result.Errors : true;
    }
}
