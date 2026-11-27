namespace Intelibill.Licensing.Domain.Entities;

[Flags]
public enum AdminCapability
{
    None = 0,
    Support = 1,
    Billing = 2,
    LicensingManager = 4,
    SecurityAdministrator = 8,
    PlatformOwner = 16,
}
