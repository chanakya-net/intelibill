using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Queries.GetShopDetails;

public sealed class GetShopDetailsQueryHandler(IUserRepository userRepository, IShopRepository shopRepository)
{
    public async Task<ErrorOr<ShopDetailsDto>> HandleAsync(GetShopDetailsQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var membership = user.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var shop = membership.Shop ?? await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        return new ShopDetailsDto(
            shop.Id,
            shop.Name,
            shop.Address,
            shop.City,
            shop.State,
            shop.Pincode,
            shop.ContactPerson,
            shop.MobileNumber);
    }
}
