using ErrorOr;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Application.Common.Licensing;

/// <summary>
/// Decides whether one more Active membership (an acceptance or a reinstatement) fits the shop's
/// member allowance. Only seat-consuming transitions ask; role-only edits, removal, suspension and
/// pending invitations never do, and nothing here removes or demotes anyone.
/// </summary>
/// <remarks>
/// Callers hold the shared licence fence (so the allowance cannot change) and the shop root row
/// (so a concurrent admission cannot count the same free seat), then count Active memberships.
/// </remarks>
public static class MemberSeatPolicy
{
    /// <summary>Whether the caller has to count seats at all: observe-only shops stay advisory.</summary>
    public static bool Applies(ShopLicenceLease lease) => lease.CurrentDecision().Enforced;

    public static Error? AdmissionDenial(ShopLicenceLease lease, int activeMembers, bool discloseUsage)
    {
        // Time may have advanced while the caller waited for its authority locks.
        var decision = lease.CurrentDecision();
        if (!decision.Enforced)
            return null;
        if (decision.EnforcedDenial() is { } restricted)
            return restricted;

        // An unknown allowance proves no free seat.
        if (lease.MemberAllowance is not { } allowance)
            return LicenceErrors.VerificationUnavailable;
        if (activeMembers < allowance)
            return null;

        return discloseUsage
            ? LicenceErrors.MemberCapacityReached(allowance, activeMembers)
            : LicenceErrors.MemberCapacityReached();
    }
}
