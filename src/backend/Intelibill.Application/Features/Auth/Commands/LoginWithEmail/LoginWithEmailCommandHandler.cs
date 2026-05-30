using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.LoginWithEmail;

public sealed class LoginWithEmailCommandHandler(
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
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        if (user is null || user.PasswordHash is null || !passwordHasher.Verify(command.Password, user.PasswordHash))
            return Errors.Auth.InvalidCredentials;

        if (!user.IsLoginEnabled)
            return Errors.Auth.UserLoginDisabled;

        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user);
        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user, activeShopId, activeShopRole);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            refreshToken.Token,
            accessTokenExpiry,
            refreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language),
            activeShopId,
            shops);
    }
}
