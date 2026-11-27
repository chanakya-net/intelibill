using Intelibill.Domain.Entities;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Licensing;

public static class EntitlementConsumer
{
    public static string? ReviewReason(EntitlementSnapshotV1 snapshot, AccountLicenseRestriction? account, ShopLicenseState? shop,
        LicenseProofIssuance? previousProofs, DateTimeOffset issuedAt, Guid? savedSlotAssignmentId = null,
        Guid? savedReplacementId = null, Guid? savedReleasedReplacementId = null,
        Guid? savedExceptionId = null, string? savedExceptionLeg = null, long? savedExceptionGeneration = null)
    {
        if (snapshot.Account is not { } incomingAccount || snapshot.Shop is not { } incomingShop)
            return "MissingScope";
        if (EntitlementSnapshotsV1.ReadRestrictionError(incomingAccount) is { } invalid)
            return invalid;
        if (!incomingShop.IsValid())
            return "InvalidShopScope";
        if (account is not null && AccountConflicts(incomingAccount, account, previousProofs?.AccountIssuedAt, issuedAt))
            return "ConflictingAccountScope";
        if (shop is not null && ShopConflicts(incomingShop, shop, previousProofs?.ShopIssuedAt, issuedAt,
                savedSlotAssignmentId, savedReplacementId, savedReleasedReplacementId,
                savedExceptionId, savedExceptionLeg, savedExceptionGeneration))
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
                saved.Restriction, saved.ReleasedRevision, suspension, saved.PreviousReleaseRevision);
        var previous = saved.ReleasedRevision ?? saved.PreviousReleaseRevision;
        var release = incoming.ReleasedRevision ?? incoming.PreviousReleaseRevision;
        if (StampConflicts(incoming.UpstreamVerifiedAt, issuedAt, saved.UpstreamVerifiedAt, previousIssuedAt))
            return true;
        // Older publishers omit the cutoff when reinstating a restriction. This transition only adds a restriction.
        if (release is null && saved.Restriction == "Unrestricted" && incoming.Restriction is "Trading" or "Security")
            return false;
        if (ReleaseConflicts(release, previous, saved.Revision))
            return true;
        if (release >= saved.Revision) return false;
        if (incoming.Restriction == "Unrestricted") return saved.Restriction != "Unrestricted";
        return suspension is not null ? incoming.Suspension != suspension
            : saved.Restriction is "Security" or "Trading" &&
                (incoming.Suspension is not null || saved.Restriction == "Security" && incoming.Restriction != "Security");
    }

    private static bool ShopConflicts(ShopEntitlementSnapshotV1 incoming, ShopLicenseState saved,
        DateTimeOffset? previousIssuedAt, DateTimeOffset issuedAt, Guid? savedSlotAssignmentId, Guid? savedReplacementId,
        Guid? savedReleasedReplacementId, Guid? savedExceptionId, string? savedExceptionLeg, long? savedExceptionGeneration)
    {
        if (incoming.Revision < saved.Revision)
            return false;
        var suspension = SavedSuspension(saved.SuspensionMode, saved.SuspensionEffectiveAt, saved.SuspensionExplanation);
        if (incoming.Revision > saved.Revision)
            return (saved.State == ShopClosuresV1.Closed && incoming.State != ShopClosuresV1.Closed
                    && !(incoming.ReopenedRevision >= saved.Revision)) ||
                (incoming.ClosureRevision != saved.ClosureRevision &&
                    !(incoming.ClosureRevision > saved.Revision)) ||
                ReleaseConflicts(incoming.ReopenedRevision, saved.ReopenedRevision, saved.Revision) ||
                (suspension is not null && incoming.Suspension != suspension && !(incoming.ReleasedRevision >= saved.Revision)) ||
                ReleaseConflicts(incoming.ReleasedRevision, saved.ReleasedRevision, saved.Revision) ||
                StampConflicts(incoming.UpstreamVerifiedAt, issuedAt, saved.UpstreamVerifiedAt, previousIssuedAt);
        return incoming != new ShopEntitlementSnapshotV1(saved.Revision, saved.UpstreamVerifiedAt, saved.State,
            saved.TermId, saved.Basis, saved.StartsAt, saved.AccessEndsAt, saved.NextBoundary, saved.ShopCapacity,
            saved.MemberAllowance, suspension, saved.ReleasedRevision, saved.ReopenedRevision, saved.ClosureRevision,
            savedSlotAssignmentId, savedReplacementId, savedReleasedReplacementId,
            savedExceptionId, savedExceptionLeg, savedExceptionGeneration);
    }

    // A repeated cutoff preserves the previously accepted release. An advancing cutoff must cover the retained head.
    private static bool ReleaseConflicts(long? incoming, long? saved, long revision) =>
        incoming != saved && !(incoming >= revision);

    private static CustomerSuspensionV1? SavedSuspension(string? mode, DateTimeOffset? effectiveAt, string? explanation) =>
        effectiveAt is { } at ? new(mode!, at, explanation!) : null;

    // Only a proven signing-time error can remove the previous head's timestamp floor.
    private static bool StampConflicts(DateTimeOffset incoming, DateTimeOffset issuedAt,
        DateTimeOffset previous, DateTimeOffset? previousIssuedAt) =>
        incoming < previous && (previousIssuedAt is null || LicenseProjectionPolicy.PrecedesIssuance(previous, previousIssuedAt) ||
            !LicenseProjectionPolicy.PrecedesIssuance(incoming, issuedAt));
}
