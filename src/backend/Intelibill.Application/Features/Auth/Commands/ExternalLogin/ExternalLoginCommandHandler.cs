using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.ExternalLogin;

public sealed class ExternalLoginCommandHandler(
    IValidator<ExternalLoginCommand> validator,
    IEnumerable<IExternalAuthProvider> authProviders,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        ExternalLoginCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var provider = authProviders.FirstOrDefault(p => p.Provider == command.Provider);
        if (provider is null)
            return Errors.Auth.UnsupportedProvider;

        var userInfoResult = await provider.ValidateTokenAsync(command.Token, cancellationToken);
        if (userInfoResult.IsError)
            return userInfoResult.Errors;

        var userInfo = userInfoResult.Value;

        // Check if this external account is already linked to a user.
        var user = await userRepository.GetByExternalLoginAsync(command.Provider, userInfo.ProviderKey, cancellationToken);

        if (user is null)
        {
            // No link found. Check if a user exists with the same email (automatic linking).
            if (userInfo.Email is not null)
                user = await userRepository.GetByEmailAsync(userInfo.Email, cancellationToken);

            if (user is null)
            {
                // New user — create account.
                var firstName = string.IsNullOrWhiteSpace(userInfo.FirstName)
                    ? command.FirstName ?? string.Empty
                    : userInfo.FirstName;
                var lastName = string.IsNullOrWhiteSpace(userInfo.LastName)
                    ? command.LastName ?? string.Empty
                    : userInfo.LastName;

                user = User.CreateFromExternalProvider(userInfo.Email, firstName, lastName);
                await userRepository.AddAsync(user, cancellationToken);
            }

            // Link the external login to the user.
            var externalLogin = UserExternalLogin.Create(user.Id, command.Provider, userInfo.ProviderKey, userInfo.Email);
            user.AddExternalLogin(externalLogin);
        }

        var (activeShopId, shops) = AuthShopSelection.Resolve(user);
        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user, activeShopId);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

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
