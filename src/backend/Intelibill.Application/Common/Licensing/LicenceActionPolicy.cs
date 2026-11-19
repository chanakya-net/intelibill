using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Application.Common.Licensing;

public sealed class LicenceActionPolicy : ILicenceActionPolicy
{
    public LicenceDecision Decide(LicenceAction action, ShopLicenseState? shop, AccountLicenseRestriction? account,
        LicenseProofIssuance? proofs, DateTimeOffset now)
    {
        var phase = GetPhase(shop, account, proofs, now);
        var row = ForAction(action);
        var outcome = phase switch
        {
            Phase.Operating => row.Operating,
            Phase.Expired or Phase.TradingRestricted or Phase.Unverified => row.Restricted,
            Phase.SecuritySuspended => row.SecuritySuspended,
            _ => row.Pending,
        };

        var refusal = phase switch
        {
            Phase.Operating => (ErrorOr.Error?)null,
            Phase.Expired => LicenceErrors.Expired,
            Phase.TradingRestricted => LicenceErrors.TradingRestricted,
            Phase.SecuritySuspended => LicenceErrors.SecuritySuspended,
            Phase.Unverified or Phase.AwaitingVerification => LicenceErrors.VerificationUnavailable,
            _ => LicenceErrors.PendingActivation,
        };
        var denial = outcome == Blocked ? refusal : null;
        var decision = phase == Phase.Operating
            ? new LicenceDecision(outcome, shop!.EnforcementActivatedAt is not null, denial,
                LicenseProjectionPolicy.VerifiedAt(shop, account!),
                LicenseProjectionPolicy.ValidUntil(shop, account!, EntitlementSnapshotsV1.Freshness))
            : new LicenceDecision(outcome, shop?.EnforcementActivatedAt is not null, denial);
        return decision with
        {
            OperatingRefusal = refusal,
            Status = phase != Phase.Operating ? phase.ToString()
                : shop!.State == "Grace" ? "Grace"
                : shop.Basis == "Trial" ? "Trial" : "Active",
        };
    }

    private static Phase GetPhase(ShopLicenseState? shop, AccountLicenseRestriction? account,
        LicenseProofIssuance? proofs, DateTimeOffset now)
    {
        var suspension = LicenseProjectionPolicy.EffectiveSuspension(shop, account, now);
        if (suspension == "Security")
            return Phase.SecuritySuspended;
        if (suspension == "Trading" || shop?.State is "Suspended" or "Closed")
            return Phase.TradingRestricted;
        if (shop is null)
            return Phase.AwaitingVerification;

        var waiting = shop.State is "PendingActivation" or "Scheduled";
        var shopVerified = shop.UpstreamVerifiedAt <= now &&
            LicenseProjectionPolicy.PrecedesIssuance(shop.UpstreamVerifiedAt, proofs?.ShopIssuedAt);
        if (shopVerified && shop.State == "Expired")
            return Phase.Expired;
        if (!shopVerified || account is null || account.AccountId != shop.AccountId || account.UpstreamVerifiedAt > now ||
            !LicenseProjectionPolicy.PrecedesIssuance(account.UpstreamVerifiedAt, proofs?.AccountIssuedAt))
            return waiting ? Phase.AwaitingVerification : Phase.Unverified;
        if (waiting)
            return now < LicenseProjectionPolicy.ValidUntil(shop, account, EntitlementSnapshotsV1.Freshness)
                ? Phase.Pending : Phase.AwaitingVerification;
        if (shop.State is not ("Active" or "Grace"))
            return Phase.Unverified;

        return LicenseProjectionPolicy.Evaluate(shop, account, now, EntitlementSnapshotsV1.Freshness) switch
        {
            LicenseProjectionDecision.VerifiedPermission => Phase.Operating,
            LicenseProjectionDecision.Expired => Phase.Expired,
            _ => Phase.Unverified,
        };
    }

    private static Row ForAction(LicenceAction action) => action switch
    {
        // Conditional outcomes require the owning operation to verify the stated condition.
        // Pending activation, trial/active/grace, expired/trading restricted, security suspended.
        LicenceAction.Health              => new(Allowed, Allowed, Allowed, Allowed),
        LicenceAction.Authentication      => new(Allowed, Allowed, Allowed, Allowed),
        LicenceAction.Billing             => new(Allowed, Allowed, Allowed, Allowed),
        LicenceAction.BasicShopSetup      => new(RequiresOwnerSetup, Allowed, RequiresRecoveryNecessity, Blocked),
        LicenceAction.NewTrading          => new(Blocked, Allowed, Blocked, Blocked),
        LicenceAction.OperationalCapacity => new(Blocked, Allowed, Blocked, Blocked),
        LicenceAction.History             => new(Allowed, Allowed, Allowed, Blocked),
        LicenceAction.TillCompletion      => new(RequiresProvenSource, Allowed, RequiresProvenSource, Blocked),
        LicenceAction.DueSettlement       => new(RequiresProvenSource, Allowed, RequiresProvenSource, Blocked),
        LicenceAction.ExistingPayment     => new(RequiresProvenSource, Allowed, RequiresProvenSource, Blocked),
        LicenceAction.SourceCorrection     => new(RequiresProvenSource, Allowed, RequiresProvenSource, Blocked),
        LicenceAction.GeneralStock        => new(Blocked, Allowed, Blocked, Blocked),
        LicenceAction.SafetyDisposition   => new(RequiresProvenSource, Allowed, RequiresProvenSource, Blocked),
        LicenceAction.Recovery            => new(RequiresProvenSource, Allowed, RequiresProvenSource, Blocked),
        LicenceAction.MemberRemoval       => new(Allowed, Allowed, Allowed, Allowed),
        LicenceAction.InternalService     => new(Allowed, Allowed, Allowed, Allowed),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unclassified licence action."),
    };

    // AwaitingVerification keeps the pending matrix row: only fresh authority can change a shop that has not started.
    private enum Phase { Pending, AwaitingVerification, Unverified, Operating, Expired, TradingRestricted, SecuritySuspended }

    private readonly record struct Row(LicenceOutcome Pending, LicenceOutcome Operating, LicenceOutcome Restricted,
        LicenceOutcome SecuritySuspended);
}
