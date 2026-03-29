using ErrorOr;
using System.Text.RegularExpressions;
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
    private static readonly Regex IndiaGstRegex = new(
        "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public async Task<ErrorOr<AuthResult>> HandleAsync(CreateShopCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Errors.Shop.NameRequired;

        if (string.IsNullOrWhiteSpace(command.Address))
            return Errors.Shop.AddressRequired;

        if (string.IsNullOrWhiteSpace(command.City))
            return Errors.Shop.CityRequired;

        if (string.IsNullOrWhiteSpace(command.State))
            return Errors.Shop.StateRequired;

        if (string.IsNullOrWhiteSpace(command.Pincode))
            return Errors.Shop.PincodeRequired;

        var gstNumber = command.GstNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(gstNumber) && !IndiaGstRegex.IsMatch(gstNumber))
            return Errors.Shop.GstNumberInvalid;

        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var isFirstShop = user.ShopMemberships.Count == 0;
        var shop = Shop.Create(
            command.Name,
            command.Address,
            command.City,
            command.State,
            command.Pincode,
            command.ContactPerson,
            command.MobileNumber,
            command.GstNumber);
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