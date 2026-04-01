using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Queries.GetShopUsers;

public sealed class GetShopUsersQueryHandler(IUserRepository userRepository, IShopRepository shopRepository)
{
    public async Task<ErrorOr<IReadOnlyList<ShopUserDto>>> HandleAsync(GetShopUsersQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var membership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var shop = await shopRepository.GetByIdWithMembersAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var actorOwnedShopIds = caller.ShopMemberships
            .Where(sm => sm.Role == ShopRole.Owner)
            .Select(sm => sm.ShopId)
            .ToList();

        var shopMembers = shop.Memberships
            .Where(sm => sm.User is not null)
            .ToList();

        var memberUserIds = shopMembers.Select(sm => sm.UserId).ToList();

        var allMemberships = actorOwnedShopIds.Count > 0
            ? await shopRepository.GetMembershipsForUsersInShopsAsync(memberUserIds, actorOwnedShopIds, cancellationToken)
            : [];

        var membershipsByUser = allMemberships
            .GroupBy(sm => sm.UserId)
            .ToDictionary(g => g.Key, g => g.Select(sm => sm.ShopId).ToList() as IReadOnlyList<Guid>);

        var users = shopMembers
            .OrderBy(sm => GetRoleOrder(sm.Role))
            .ThenBy(sm => sm.User.FirstName)
            .ThenBy(sm => sm.User.LastName)
            .Select(sm => new ShopUserDto(
                sm.UserId,
                sm.User.FirstName,
                sm.User.LastName,
                sm.User.Email,
                sm.User.PhoneNumber,
                sm.Role == ShopRole.Staff ? "Staff" : sm.Role.ToString(),
                sm.User.IsLoginEnabled,
                membershipsByUser.TryGetValue(sm.UserId, out var shopIds) ? shopIds : [],
                sm.User.Language))
            .ToList();

        return users;
    }

    private static int GetRoleOrder(ShopRole role)
    {
        return role switch
        {
            ShopRole.Owner => 0,
            ShopRole.Manager => 1,
            _ => 2,
        };
    }
}
