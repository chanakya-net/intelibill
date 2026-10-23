using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Users;

/// <summary>
/// Shared parsing and labelling for the two shop-scoped authority values a shop administrator may
/// set. Owner is deliberately not parseable here: ownership changes only through plan 40's
/// dedicated transfer workflow.
/// </summary>
internal static class MembershipAuthority
{
    public static bool TryParseAssignableRole(string? value, out ShopRole role)
    {
        var normalized = Normalize(value);

        if (normalized == "manager")
        {
            role = ShopRole.Manager;
            return true;
        }

        if (normalized is "salesperson" or "staff")
        {
            role = ShopRole.Staff;
            return true;
        }

        role = default;
        return false;
    }

    public static bool TryParseStatus(string? value, out MembershipStatus status)
    {
        switch (Normalize(value))
        {
            case "active":
                status = MembershipStatus.Active;
                return true;
            case "suspended":
                status = MembershipStatus.Suspended;
                return true;
            case "removed":
                status = MembershipStatus.Removed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static string ToRoleLabel(ShopRole role) => role == ShopRole.Staff ? "Staff" : role.ToString();

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
}
