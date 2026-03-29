using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler(
    IValidator<RefreshTokenCommand> validator,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var existing = await refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, cancellationToken);
        if (existing is null)
            return Errors.Auth.InvalidRefreshToken;

        // Rotate: revoke old token and issue a new pair.
        existing.Revoke();
        refreshTokenRepository.Update(existing);

        var user = existing.User;
        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user);
        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user, activeShopId, activeShopRole);
        var newRefreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            newRefreshToken.Token,
            accessTokenExpiry,
            newRefreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName),
            activeShopId,
            shops);
    }
}
