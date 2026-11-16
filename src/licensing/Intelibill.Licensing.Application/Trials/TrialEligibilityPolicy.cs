using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Trials;

public static class TrialEligibilityPolicy
{
    public static string? Check(TrialAffiliation? affiliation, bool hasHistory, ShopTrialPolicyV1? policy) =>
        hasHistory ? "AlreadyUsed" : affiliation is null ? "AffiliationRequired" :
        affiliation.TermId is not null ? "CoverageExists" : policy is null ? "PolicyUnavailable" : null;

    public static string? CheckConsent(ShopTrialConsentV1 consent, ShopTrialPolicyV1 policy, TrialInterval term, DateTimeOffset at)
    {
        if (consent.AcceptedAt > at || at >= term.EndsAt)
            return "ConsentOutsideWindow";
        if (consent.Acceptance.PolicyId != policy.PolicyId || consent.Acceptance.PolicyRevision != policy.PolicyRevision ||
            consent.Acceptance.TermsVersion != policy.TermsVersion)
            return "PolicyChanged";
        return consent.ActiveMembers > policy.MemberCap ? "MemberCap" : null;
    }

    // End events sort before start events at the same instant, so adjacent intervals never overlap.
    public static bool HasCapacity(IReadOnlyList<TrialInterval> existing, TrialInterval candidate, int cap)
    {
        var events = existing.SelectMany(term => new[]
        {
            (At: term.StartsAt < candidate.StartsAt ? candidate.StartsAt : term.StartsAt, Delta: 1),
            (At: term.EndsAt > candidate.EndsAt ? candidate.EndsAt : term.EndsAt, Delta: -1),
        });
        var active = 0;
        foreach (var edge in events.OrderBy(edge => edge.At).ThenBy(edge => edge.Delta))
        {
            active += edge.Delta;
            if (active >= cap)
                return false;
        }
        return true;
    }
}
