using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RevokeToken;

public sealed class RevokeTokenCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork)
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
