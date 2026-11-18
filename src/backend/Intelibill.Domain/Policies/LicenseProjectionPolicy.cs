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

/// <summary>When licensing signed the retained evidence behind each head. Absent evidence proves nothing.</summary>
public sealed record LicenseProofIssuance(DateTimeOffset? ShopIssuedAt, DateTimeOffset? AccountIssuedAt);

/// <summary>Evaluates the retained account and shop proofs at a single instant.</summary>
public static class LicenseProjectionPolicy
{
    // A v1 service assertion records its issue time in whole seconds, truncated.
    private static readonly TimeSpan AssertionPrecision = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A verification cannot postdate the signature carrying it. Both instants come from the licensing clock,
    /// so a later stamp stays incoherent however far the merchant clock advances.
    /// </summary>
    public static bool PrecedesIssuance(DateTimeOffset verifiedAt, DateTimeOffset? issuedAt) =>
        issuedAt.HasValue && verifiedAt - issuedAt.Value < AssertionPrecision;

    public static LicenseProjectionDecision Evaluate(ShopLicenseState? shop, AccountLicenseRestriction? account,
        DateTimeOffset now, TimeSpan freshness)
    {
        if (shop is null || account is null || shop.AccountId != account.AccountId ||
            now < shop.UpstreamVerifiedAt || now < account.UpstreamVerifiedAt)
            return LicenseProjectionDecision.Incomplete;
        if (account.Restriction != "Unrestricted")
            return LicenseProjectionDecision.Restricted;
        if (shop.State == "Expired")
            return LicenseProjectionDecision.Expired;

        var cutoff = ValidUntil(shop, account, freshness);
        if (now >= cutoff)
        {
            // An earlier cutoff leaves the later commercial state unknown until a new proof arrives.
            if (shop.AccessEndsAt == cutoff)
                return LicenseProjectionDecision.Expired;
            return shop.NextBoundary == cutoff
                ? LicenseProjectionDecision.VerificationRequired
                : LicenseProjectionDecision.Stale;
        }
        if (shop.State is "Active" or "Grace" && shop.StartsAt <= now && shop.AccessEndsAt > now)
            return LicenseProjectionDecision.VerifiedPermission;
        return LicenseProjectionDecision.Incomplete;
    }

    public static DateTimeOffset VerifiedAt(ShopLicenseState shop, AccountLicenseRestriction account) =>
        shop.UpstreamVerifiedAt < account.UpstreamVerifiedAt ? shop.UpstreamVerifiedAt : account.UpstreamVerifiedAt;

    public static DateTimeOffset ValidUntil(ShopLicenseState shop, AccountLicenseRestriction account, TimeSpan freshness)
    {
        var deadline = VerifiedAt(shop, account) + freshness;
        if (shop.AccessEndsAt is { } end && end < deadline)
            deadline = end;
        return shop.NextBoundary is { } boundary && boundary < deadline ? boundary : deadline;
    }
}
