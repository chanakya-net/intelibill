using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;

public sealed class SwitchActiveShopCommandHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(SwitchActiveShopCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;
        if (user.SecurityVersion != command.SecurityVersion || !user.IsLoginEnabled)
            return Error.Unauthorized("Auth.SessionInvalidated", "Sign in again to continue.");

        // Any active member may select a shop they are assigned to; a regional manager works
        // across several stores. Role still governs what they may do once inside it.
        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId && sm.IsActive);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        membership.MarkUsed();

        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user, membership.ShopId);
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
