using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Policies;

public enum LicenseProjectionDecision
{
    Incomplete,
    Restricted,
    Expired,
    Stale,
    VerifiedPermission,
    VerificationRequired,
}

/// <summary>Diagnostic evaluation only. Merchant write authorization does not depend on this decision.</summary>
public static class LicenseProjectionPolicy
{
    public static LicenseProjectionDecision Evaluate(ShopLicenseState? shop, AccountLicenseRestriction? account,
        DateTimeOffset now, TimeSpan freshness)
    {
        if (shop is null || account is null || shop.AccountId != account.AccountId ||
            now < shop.UpstreamVerifiedAt || now < account.UpstreamVerifiedAt)
            return LicenseProjectionDecision.Incomplete;
        if (account.Restriction != "Unrestricted")
            return LicenseProjectionDecision.Restricted;
        if (shop.State == "Expired" || shop.AccessEndsAt is { } end && now >= end)
            return LicenseProjectionDecision.Expired;
        if (now - account.UpstreamVerifiedAt >= freshness || now - shop.UpstreamVerifiedAt >= freshness)
            return LicenseProjectionDecision.Stale;
        if (shop.NextBoundary is { } boundary && now >= boundary)
            return LicenseProjectionDecision.VerificationRequired;
        if (shop.State is "Active" or "Grace" && shop.StartsAt <= now && shop.AccessEndsAt > now)
            return LicenseProjectionDecision.VerifiedPermission;
        return LicenseProjectionDecision.Incomplete;
    }
}
