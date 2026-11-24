using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Domain.Policies;

/// <summary>
/// Pure, versioned access evaluation at an explicit instant. State advances at exact <c>[start, end)</c>
/// boundaries without any scheduler; suspension overrides the state but never moves a deadline.
/// </summary>
public static class SubscriptionAccessPolicy
{
    public const string Version = "subscription-access/1";

    public static SubscriptionAccessEvaluation Evaluate(SubscriptionAccessFacts facts, DateTimeOffset at)
    {
        var cancelledAt = facts.Cancellation is { IsEffective: true } cancellation && cancellation.EffectiveAt < facts.EndsAt
            ? cancellation.EffectiveAt : (DateTimeOffset?)null;
        var paidThrough = cancelledAt is { } early && early > facts.StartsAt ? early : facts.EndsAt;
        var grace = facts.Evidence == TermEvidenceState.Complete ? GracePolicy.GraceEndsAt(facts) : null;
        var decided = facts is { Evidence: TermEvidenceState.Complete, Basis: SubscriptionBasis.Paid } ? facts.RefundBoundary : null;
        if (decided is not null)
            (paidThrough, grace) = Apply(decided, facts, paidThrough, grace);
        var accessEnds = facts.Evidence == TermEvidenceState.Complete ? grace ?? paidThrough : (DateTimeOffset?)null;
        var state = facts.Evidence switch
        {
            TermEvidenceState.Incomplete => SubscriptionAccessState.EvidenceIncomplete,
            TermEvidenceState.NeedsReview => SubscriptionAccessState.NeedsReview,
            _ => Underlying(facts, at, paidThrough, grace),
        };
        if (facts.Evidence == TermEvidenceState.Complete && facts.Suspensions?.Any(s => s.Covers(at)) == true)
            state = SubscriptionAccessState.Suspended;

        return new SubscriptionAccessEvaluation(state, facts.StartsAt, paidThrough, grace, accessEnds,
            NextDeadline(facts, at, paidThrough, grace), decided is null ? NextPeriodEnd(facts, paidThrough) : null);
    }

    // A decided term is paid through its decided boundary and keeps access (as grace) only up to its decided end.
    private static (DateTimeOffset PaidThrough, DateTimeOffset? Grace) Apply(RefundAccessBoundary decided,
        SubscriptionAccessFacts facts, DateTimeOffset paidThrough, DateTimeOffset? grace)
    {
        var paid = decided.PaidThrough > facts.StartsAt ? decided.PaidThrough : facts.StartsAt;
        if (paid > paidThrough) paid = paidThrough;
        var limit = grace ?? paidThrough;
        var ends = decided.AccessEndsAt < limit ? decided.AccessEndsAt : limit;
        return (paid, ends > paid ? ends : null);
    }

    private static SubscriptionAccessState Underlying(SubscriptionAccessFacts facts, DateTimeOffset at,
        DateTimeOffset paidThrough, DateTimeOffset? grace)
    {
        if (at < facts.StartsAt) return SubscriptionAccessState.Scheduled;
        if (at < paidThrough) return SubscriptionAccessState.Active;
        return grace is { } end && at < end ? SubscriptionAccessState.Grace : SubscriptionAccessState.Expired;
    }

    private static DateTimeOffset? NextDeadline(SubscriptionAccessFacts facts, DateTimeOffset at,
        DateTimeOffset paidThrough, DateTimeOffset? grace)
    {
        if (at < facts.StartsAt) return facts.StartsAt;
        if (at < paidThrough) return paidThrough;
        return grace is { } end && at < end ? end : null;
    }

    // The boundary a renewal of this term would run to: the anchor's next ordinal, never a chained clipped date.
    private static DateTimeOffset? NextPeriodEnd(SubscriptionAccessFacts facts, DateTimeOffset paidThrough) =>
        facts is { Evidence: TermEvidenceState.Complete, Basis: SubscriptionBasis.Paid, Anchor: { } anchor, PeriodOrdinal: { } ordinal }
        && paidThrough == facts.EndsAt && ordinal is >= 0 and <= BillingCalendar.MaxPeriodOrdinal
            ? BillingCalendar.TryBoundary(anchor, ordinal + 2L) : null;
}
