using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.SetDefaultShop;

public sealed class SetDefaultShopCommandHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(SetDefaultShopCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var targetMembership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (targetMembership is null)
            return Errors.Shop.MembershipNotFound;

        foreach (var membership in user.ShopMemberships)
            membership.SetDefault(membership.ShopId == command.ShopId);

        targetMembership.MarkUsed();

        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user, targetMembership.ShopId);
        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user, activeShopId, activeShopRole);
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