using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.CreateShop;

public sealed class CreateShopCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(CreateShopCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Errors.Shop.NameRequired;

        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var isFirstShop = user.ShopMemberships.Count == 0;
        var shop = Shop.Create(command.Name);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, isFirstShop);
        membership.MarkUsed();

        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        await shopRepository.AddAsync(shop, cancellationToken);

        var (activeShopId, shops) = AuthShopSelection.Resolve(user, shop.Id);
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