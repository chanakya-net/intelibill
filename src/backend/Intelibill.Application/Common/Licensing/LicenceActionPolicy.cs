using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;
using Intelibill.Domain.Entities;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Application.Common.Licensing;

public sealed class LicenceActionPolicy : ILicenceActionPolicy
{
    public LicenceDecision Decide(LicenceAction action, ShopLicenseState? shop, AccountLicenseRestriction? account, DateTimeOffset now)
    {
        var phase = GetPhase(shop, account, now);
        var row = ForAction(action);
        var outcome = phase switch
        {
            Phase.Operating => row.Operating,
            Phase.Expired or Phase.TradingRestricted => row.Restricted,
            Phase.SecuritySuspended => row.SecuritySuspended,
            _ => row.Pending,
        };

        return new(outcome, shop?.EnforcementActivatedAt is not null, outcome != Blocked ? null : phase switch
        {
            Phase.Expired => LicenceErrors.Expired,
            Phase.TradingRestricted => LicenceErrors.TradingRestricted,
            Phase.SecuritySuspended => LicenceErrors.SecuritySuspended,
            Phase.Unverified => LicenceErrors.EvidenceRequired,
            _ => LicenceErrors.PendingActivation,
        });
    }

    private static Phase GetPhase(ShopLicenseState? shop, AccountLicenseRestriction? account, DateTimeOffset now)
    {
        if (account?.Restriction == "Security")
            return Phase.SecuritySuspended;
        if (account?.Restriction == "Trading" || shop?.State is "Suspended" or "Closed")
            return Phase.TradingRestricted;
        if (shop is null || shop.State is "PendingActivation" or "Scheduled")
            return Phase.Pending;
        if (shop.State == "Expired")
            return Phase.Expired;
        if (shop.State is not ("Active" or "Grace") || account?.Restriction != "Unrestricted" ||
            shop.StartsAt is not { } start || shop.AccessEndsAt is not { } end || end <= start)
            return Phase.Unverified;

        if (now < start)
            return Phase.Pending;
        return now < end ? Phase.Operating : Phase.Expired;
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

    private enum Phase { Pending, Unverified, Operating, Expired, TradingRestricted, SecuritySuspended }

    private readonly record struct Row(LicenceOutcome Pending, LicenceOutcome Operating, LicenceOutcome Restricted,
        LicenceOutcome SecuritySuspended);
}
