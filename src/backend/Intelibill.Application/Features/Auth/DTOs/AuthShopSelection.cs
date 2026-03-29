using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Auth.DTOs;

internal static class AuthShopSelection
{
    public static (Guid? ActiveShopId, string? ActiveShopRole, IReadOnlyList<UserShopDto> Shops) Resolve(User user, Guid? preferredShopId = null)
    {
        var memberships = user.ShopMemberships
            .Where(sm => sm.Shop is not null)
            .ToList();

        if (memberships.Count == 0)
            return (null, null, []);

        if (memberships.Count == 1 && !memberships[0].IsDefault)
            memberships[0].SetDefault(true);

        ShopMembership? activeMembership = null;
        if (preferredShopId is not null)
            activeMembership = memberships.FirstOrDefault(sm => sm.ShopId == preferredShopId.Value);

        activeMembership ??= memberships
            .Where(sm => sm.LastUsedAt is not null)
            .OrderByDescending(sm => sm.LastUsedAt)
            .FirstOrDefault();

        activeMembership ??= memberships.FirstOrDefault(sm => sm.IsDefault);
        activeMembership ??= memberships[0];

        activeMembership.MarkUsed();

        var shops = memberships
            .OrderByDescending(sm => sm.IsDefault)
            .ThenByDescending(sm => sm.LastUsedAt)
            .Select(sm => new UserShopDto(
                sm.ShopId,
                sm.Shop.Name,
                sm.Role.ToString(),
                sm.IsDefault,
                sm.LastUsedAt))
            .ToList();

        return (activeMembership.ShopId, activeMembership.Role.ToString(), shops);
    }
}