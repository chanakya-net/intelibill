using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Queries.GetMyShops;

public sealed class GetMyShopsQueryHandler(IUserRepository userRepository)
{
    public async Task<ErrorOr<IReadOnlyList<UserShopDto>>> HandleAsync(GetMyShopsQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.Shop.UserNotFound;

        var shops = user.ShopMemberships
            .Where(sm => sm.Shop is not null)
            .OrderByDescending(sm => sm.IsDefault)
            .ThenByDescending(sm => sm.LastUsedAt)
            .Select(sm => new UserShopDto(sm.ShopId, sm.Shop.Name, sm.Role.ToString(), sm.IsDefault, sm.LastUsedAt))
            .ToList();

        return shops;
    }
}