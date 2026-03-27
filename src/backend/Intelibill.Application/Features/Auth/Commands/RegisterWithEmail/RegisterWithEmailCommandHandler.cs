using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;

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
        var (activeShopId, shops) = AuthShopSelection.Resolve(user);

        await userRepository.AddAsync(user, cancellationToken);

        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user, activeShopId);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BuildResult(user, accessToken, accessTokenExpiry, refreshToken, activeShopId, shops);
    }

    private static AuthResult BuildResult(
        User user,
        string accessToken,
        DateTimeOffset accessTokenExpiry,
        Domain.Entities.RefreshToken refreshToken,
        Guid? activeShopId,
        IReadOnlyList<UserShopDto> shops)
    {
        return new AuthResult(
            accessToken,
            refreshToken.Token,
            accessTokenExpiry,
            refreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName),
            activeShopId,
            shops);
    }
}
