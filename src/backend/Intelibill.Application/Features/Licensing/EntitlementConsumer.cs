using Intelibill.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Licensing;

public static class EntitlementConsumer
{
    public static string? ReviewReason(EntitlementSnapshotV1 snapshot, AccountLicenseRestriction? account, ShopLicenseState? shop)
    {
        if (snapshot.Account is not { } incomingAccount || snapshot.Shop is not { } incomingShop)
            return "MissingScope";
        if (EntitlementSnapshotsV1.ReadRestrictionError(incomingAccount) is { } invalid)
            return invalid;
        if (!incomingShop.IsValid())
            return "InvalidShopScope";
        if (account is not null && AccountConflicts(incomingAccount, account))
            return "ConflictingAccountScope";
        if (shop is not null && ShopConflicts(incomingShop, shop))
            return "ConflictingShopScope";
        return null;
    }

    private static bool AccountConflicts(AccountRestrictionSnapshotV1 incoming, AccountLicenseRestriction saved)
    {
        if (incoming.Revision < saved.Revision)
            return false;
        if (incoming.Revision == saved.Revision)
            return incoming != new AccountRestrictionSnapshotV1(saved.Revision, saved.UpstreamVerifiedAt,
                saved.Restriction, saved.ReleasedRevision);
        if (incoming.UpstreamVerifiedAt < saved.UpstreamVerifiedAt)
            return true;
        if (incoming.Restriction != "Unrestricted")
            return false;
        if (saved.Restriction == "Unrestricted" && incoming.ReleasedRevision == saved.ReleasedRevision)
            return false;
        return incoming.ReleasedRevision is not { } release || release < saved.Revision;
    }

    private static bool ShopConflicts(ShopEntitlementSnapshotV1 incoming, ShopLicenseState saved)
    {
        if (incoming.Revision < saved.Revision)
            return false;
        if (incoming.Revision > saved.Revision)
            return incoming.UpstreamVerifiedAt < saved.UpstreamVerifiedAt;
        return incoming != new ShopEntitlementSnapshotV1(saved.Revision, saved.UpstreamVerifiedAt, saved.State,
            saved.TermId, saved.Basis, saved.StartsAt, saved.AccessEndsAt, saved.NextBoundary, saved.ShopCapacity,
            saved.MemberAllowance);
    }
}
