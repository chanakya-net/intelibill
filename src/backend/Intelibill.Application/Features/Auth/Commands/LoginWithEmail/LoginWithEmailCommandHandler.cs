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

        if (!user.IsLoginEnabled) return Errors.Auth.UserLoginDisabled;
        if (user.Email is not null && !user.IsEmailVerified)
            return Error.Conflict("Auth.EmailVerificationRequired", "Use mailbox recovery to verify your email before signing in.");

        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user);
        var (accessToken, accessTokenExpiry) = await tokenService.GenerateAccessTokenAsync(user, activeShopId, activeShopRole, cancellationToken);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);
        refreshToken.BindSecurityVersion(user.SecurityVersion);

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
