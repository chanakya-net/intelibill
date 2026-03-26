using ErrorOr;
using InventoryAI.Application.Common.Errors;
using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Application.Features.Auth.DTOs;
using InventoryAI.Domain.Interfaces;
using InventoryAI.Domain.Interfaces.Repositories;

using FluentValidation;
using InventoryAI.Application.Common.Extensions;

namespace InventoryAI.Application.Features.Auth.Commands.RefreshToken;

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
        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user);
        var newRefreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            newRefreshToken.Token,
            accessTokenExpiry,
            newRefreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName));
    }
}
