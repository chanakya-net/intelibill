using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.RevokeToken;

public sealed class RevokeTokenCommandHandler(
    IValidator<RevokeTokenCommand> validator,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<bool>> HandleAsync(
        RevokeTokenCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var token = await refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, cancellationToken);
        if (token is null)
            return Errors.Auth.InvalidRefreshToken;

        token.Revoke();
        refreshTokenRepository.Update(token);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
