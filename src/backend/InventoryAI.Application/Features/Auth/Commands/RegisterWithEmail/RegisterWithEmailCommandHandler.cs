using ErrorOr;
using InventoryAI.Application.Common.Errors;
using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Application.Features.Auth.DTOs;
using InventoryAI.Domain.Entities;
using InventoryAI.Domain.Interfaces;
using InventoryAI.Domain.Interfaces.Repositories;

using FluentValidation;
using InventoryAI.Application.Common.Extensions;

namespace InventoryAI.Application.Features.Auth.Commands.RegisterWithEmail;

public sealed class RegisterWithEmailCommandHandler(
    IValidator<RegisterWithEmailCommand> validator,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        RegisterWithEmailCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        if (await userRepository.ExistsByEmailAsync(command.Email, cancellationToken))
            return Errors.Auth.EmailAlreadyInUse;

        var passwordHash = passwordHasher.Hash(command.Password);
        var user = User.CreateWithEmail(command.Email, passwordHash, command.FirstName, command.LastName);

        await userRepository.AddAsync(user, cancellationToken);

        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildResult(user, accessToken, accessTokenExpiry, refreshToken);
    }

    private static AuthResult BuildResult(
        User user,
        string accessToken,
        DateTimeOffset accessTokenExpiry,
        Domain.Entities.RefreshToken refreshToken)
    {
        return new AuthResult(
            accessToken,
            refreshToken.Token,
            accessTokenExpiry,
            refreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName));
    }
}
