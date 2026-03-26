using ErrorOr;
using InventoryAI.Application.Common.Errors;
using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Application.Features.Auth.DTOs;
using InventoryAI.Domain.Interfaces;
using InventoryAI.Domain.Interfaces.Repositories;

using FluentValidation;
using InventoryAI.Application.Common.Extensions;

namespace InventoryAI.Application.Features.Auth.Commands.LoginWithEmail;

public sealed class LoginWithEmailCommandHandler(
    IValidator<LoginWithEmailCommand> validator,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        LoginWithEmailCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        if (user is null || user.PasswordHash is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
            return Errors.Auth.InvalidCredentials;

        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            refreshToken.Token,
            accessTokenExpiry,
            refreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName));
    }
}
