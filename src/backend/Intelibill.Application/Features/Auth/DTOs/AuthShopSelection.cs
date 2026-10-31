using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Auth.DTOs;

internal static class AuthShopSelection
{
    /// <summary>
    /// The caller's live shops, in the order a picker should show them, with nothing changed. Used
    /// where the answer is a list rather than a selection — saving a default must not decide which
    /// shop a device is working in.
    /// </summary>
    public static IReadOnlyList<UserShopDto> Describe(User user) =>
        user.ShopMemberships
            .Where(sm => sm.IsActive)
            .OrderByDescending(sm => sm.IsDefault)
            .ThenByDescending(sm => sm.LastUsedAt)
            .Select(sm => new UserShopDto(
                sm.ShopId,
                sm.Shop?.Name ?? "Unknown Shop",
                sm.Role.ToString(),
                sm.IsDefault,
                sm.LastUsedAt))
            .ToList();

    public static (Guid? ActiveShopId, string? ActiveShopRole, IReadOnlyList<UserShopDto> Shops) Resolve(User user, Guid? preferredShopId = null)
    {
        // Only live memberships may be selected or offered. A suspended shop disappears from the
        // session without touching the account's other shops.
        var memberships = user.ShopMemberships.Where(sm => sm.IsActive).ToList();

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
                sm.Shop?.Name ?? "Unknown Shop",
                sm.Role.ToString(),
                sm.IsDefault,
                sm.LastUsedAt))
            .ToList();

        return (activeMembership.ShopId, activeMembership.Role.ToString(), shops);
    }
}
