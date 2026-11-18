using Intelibill.Domain.Entities;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Licensing;

public static class EntitlementConsumer
{
    public static string? ReviewReason(EntitlementSnapshotV1 snapshot, AccountLicenseRestriction? account, ShopLicenseState? shop,
        LicenseProofIssuance? previousProofs, DateTimeOffset issuedAt)
    {
        if (snapshot.Account is not { } incomingAccount || snapshot.Shop is not { } incomingShop)
            return "MissingScope";
        if (EntitlementSnapshotsV1.ReadRestrictionError(incomingAccount) is { } invalid)
            return invalid;
        if (!incomingShop.IsValid())
            return "InvalidShopScope";
        if (account is not null && AccountConflicts(incomingAccount, account, previousProofs?.AccountIssuedAt, issuedAt))
            return "ConflictingAccountScope";
        if (shop is not null && ShopConflicts(incomingShop, shop, previousProofs?.ShopIssuedAt, issuedAt))
            return "ConflictingShopScope";
        return null;
    }

    private static bool AccountConflicts(AccountRestrictionSnapshotV1 incoming, AccountLicenseRestriction saved,
        DateTimeOffset? previousIssuedAt, DateTimeOffset issuedAt)
    {
        if (incoming.Revision < saved.Revision)
            return false;
        var suspension = SavedSuspension(saved.Restriction, saved.SuspensionEffectiveAt, saved.SuspensionExplanation);
        if (incoming.Revision == saved.Revision)
            return incoming != new AccountRestrictionSnapshotV1(saved.Revision, saved.UpstreamVerifiedAt,
                saved.Restriction, saved.ReleasedRevision, suspension);
        if (StampConflicts(incoming.UpstreamVerifiedAt, issuedAt, saved.UpstreamVerifiedAt, previousIssuedAt))
            return true;
        if (incoming.Restriction != "Unrestricted")
            return suspension is not null && incoming.Suspension != suspension;
        if (saved.Restriction == "Unrestricted" && incoming.ReleasedRevision == saved.ReleasedRevision)
            return false;
        return incoming.ReleasedRevision is not { } release || release < saved.Revision;
    }

    private static bool ShopConflicts(ShopEntitlementSnapshotV1 incoming, ShopLicenseState saved,
        DateTimeOffset? previousIssuedAt, DateTimeOffset issuedAt)
    {
        if (incoming.Revision < saved.Revision)
            return false;
        var suspension = SavedSuspension(saved.SuspensionMode, saved.SuspensionEffectiveAt, saved.SuspensionExplanation);
        if (incoming.Revision > saved.Revision)
            return (suspension is not null && incoming.Suspension != suspension) ||
                StampConflicts(incoming.UpstreamVerifiedAt, issuedAt, saved.UpstreamVerifiedAt, previousIssuedAt);
        return incoming != new ShopEntitlementSnapshotV1(saved.Revision, saved.UpstreamVerifiedAt, saved.State,
            saved.TermId, saved.Basis, saved.StartsAt, saved.AccessEndsAt, saved.NextBoundary, saved.ShopCapacity,
            saved.MemberAllowance, suspension);
    }

    private static CustomerSuspensionV1? SavedSuspension(string? mode, DateTimeOffset? effectiveAt, string? explanation) =>
        effectiveAt is { } at ? new(mode!, at, explanation!) : null;

    // Only a proven signing-time error can remove the previous head's timestamp floor.
    private static bool StampConflicts(DateTimeOffset incoming, DateTimeOffset issuedAt,
        DateTimeOffset previous, DateTimeOffset? previousIssuedAt) =>
        incoming < previous && (previousIssuedAt is null || LicenseProjectionPolicy.PrecedesIssuance(previous, previousIssuedAt) ||
            !LicenseProjectionPolicy.PrecedesIssuance(incoming, issuedAt));
}
