using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public static class AdminCapabilityNames
{
    private static readonly (AdminCapability Grant, string Name)[] Known =
    [
        (AdminCapability.Support, "support"),
        (AdminCapability.Billing, "billing"),
        (AdminCapability.LicensingManager, "licensing-manager"),
        (AdminCapability.SecurityAdministrator, "security-administrator"),
        (AdminCapability.PlatformOwner, "platform-owner"),
    ];

    public static bool TryParse(string?[]? names, out AdminCapability grants)
    {
        grants = AdminCapability.None;
        if (names is null || names.Length > Known.Length) return false;
        foreach (var name in names)
        {
            var (grant, _) = Array.Find(Known, item => item.Name == name);
            if (grant == AdminCapability.None || grants.HasFlag(grant)) return false;
            grants |= grant;
        }
        return true;
    }

    public static string[] ToNames(AdminCapability grants) =>
        Known.Where(item => grants.HasFlag(item.Grant)).Select(item => item.Name).ToArray();

    public static string[] ToClaims(AdminCapability grants) => grants.HasFlag(AdminCapability.PlatformOwner)
        ? [.. ToNames(grants), ProviderActor.AccessManagementCapability, ProviderActor.InvitationCapability,
            ProviderActor.AuditIdentityReadCapability, ProviderActor.RecoveryReviewCapability]
        : ToNames(grants);
}
