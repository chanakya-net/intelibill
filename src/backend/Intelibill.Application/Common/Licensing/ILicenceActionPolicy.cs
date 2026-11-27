using Intelibill.Domain.Policies;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Common.Licensing;

public interface ILicenceActionPolicy
{
    /// <param name="activationPending">
    /// The verified head names an assigned slot whose live member check has not succeeded for this shop yet.
    /// </param>
    LicenceDecision Decide(LicenceAction action, ShopLicenseState? shop, AccountLicenseRestriction? account,
        LicenseProofIssuance? proofs, DateTimeOffset now, bool activationPending = false);
}
