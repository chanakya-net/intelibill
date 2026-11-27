using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Licensing;

public static class MembershipLicenceActions
{
    public static LicenceAction For(MembershipStatus currentStatus, MembershipStatus requestedStatus, bool roleChanged)
    {
        // A role edit still needs operating access when bundled with a status change.
        if (roleChanged)
            return LicenceAction.OperationalCapacity;

        return (currentStatus, requestedStatus) switch
        {
            (MembershipStatus.Active, MembershipStatus.Suspended or MembershipStatus.Removed) or
            (MembershipStatus.Suspended, MembershipStatus.Removed) or
            (MembershipStatus.Removed, MembershipStatus.Suspended) => LicenceAction.MemberRemoval,
            _ => LicenceAction.OperationalCapacity,
        };
    }
}
