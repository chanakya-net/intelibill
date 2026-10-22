using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await refreshTokenRepository.GetActiveByTokenAsync(command.RefreshToken, cancellationToken);
        if (existing is null || !existing.User.IsLoginEnabled || existing.SecurityVersion != existing.User.SecurityVersion ||
            (existing.User.Email is not null && !existing.User.IsEmailVerified && existing.User.ExternalLogins.Count == 0))
            return Errors.Auth.InvalidRefreshToken;

        // Rotate: revoke old token and issue a new pair.
        existing.Revoke();
        refreshTokenRepository.Update(existing);

        var user = existing.User;
        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user);
        var (accessToken, accessTokenExpiry) = await tokenService.GenerateAccessTokenAsync(user, activeShopId, activeShopRole, cancellationToken);
        var newRefreshToken = tokenService.CreateRefreshToken(user.Id);
        newRefreshToken.BindSecurityVersion(user.SecurityVersion);

        await refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            newRefreshToken.Token,
            accessTokenExpiry,
            newRefreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language),
            activeShopId,
            shops);
    }
}
