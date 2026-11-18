using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Licensing;

public interface ILicenceActionPolicy
{
    LicenceDecision Decide(LicenceAction action, ShopLicenseState? shop, AccountLicenseRestriction? account, DateTimeOffset now);
}
