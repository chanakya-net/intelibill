using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RevokeToken;

/// <summary>
/// Ordinary sign-out. It ends the device's session, not just the credential that renews it:
/// otherwise the session row would linger until it expired, the owner's device list would fill with
/// devices nobody is signed in on, and the access token already in hand would keep working for the
/// rest of its life — which is exactly what someone signing out of a shared terminal is avoiding.
/// </summary>
public sealed class RevokeTokenCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionRepository sessions,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RevokeTokenCommand command,
        CancellationToken cancellationToken)
    {
        var token = await refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, cancellationToken);
        if (token is null)
            return Errors.Auth.InvalidRefreshToken;

        token.Revoke();
        refreshTokenRepository.Update(token);

        // A row written before sessions existed names none; revoking the credential is all there is
        // to do for it.
        if (token.AuthSessionId is Guid sessionId)
        {
            var session = await sessions.FindAsync(token.UserId, sessionId, cancellationToken);
            if (session is not null)
                await sessions.RevokeAsync(session, "signed-out", clock.GetUtcNow(), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
