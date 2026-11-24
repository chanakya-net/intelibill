using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Domain.Policies;

public enum RenewalStart { Fulfilment, PriorPaidThrough, RestoredAccess, Unresolved }

/// <summary>The paid term a renewal or first purchase would leave behind, with the evidence it relied on.</summary>
public sealed record RenewalInput(
    BillingPeriod Period,
    DateTimeOffset FulfilledAt,
    SubscriptionBasis? PriorBasis = null,
    DateTimeOffset? PriorPaidThrough = null,
    BillingAnchor? PriorAnchor = null,
    int? PriorOrdinal = null,
    DateTimeOffset? VerifiedPaymentAt = null,
    bool AcceptedQuote = false,
    DateTimeOffset? RestoredAccessAt = null,
    bool RoundFreshStartUp = false,
    DateTimeOffset? RefundAccessEndsAt = null);

public sealed record RenewalDecision(
    RenewalStart Start,
    BillingAnchor? Anchor,
    int? Ordinal,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int IncludedGraceDays)
{
    internal static RenewalDecision Unresolved { get; } = new(RenewalStart.Unresolved, null, null, null, null, 0);
}

/// <summary>
/// Pure decision seam for the term a fulfilled payment produces. First purchase and trial conversion start at
/// fulfilment with a fresh anchor. An on-time renewal (before the grace end, or verified as paid before it with
/// an accepted quote) continues the old anchor from the old paid-through and discloses the grace days it
/// includes, counted to the verified payment time when an accepted quote supplies one. After restriction the term starts at restored access with a fresh anchor and no charge for the gap.
/// Missing or contradictory evidence is unresolved, never guessed. No payment is verified or written here.
/// </summary>
public static class RenewalPolicy
{
    public const string Version = "renewal/1";

    public static RenewalDecision DetermineTerm(RenewalInput input)
    {
        if (input.PriorBasis is null or SubscriptionBasis.Trial)
            return Fresh(input.Period, input.FulfilledAt, input.RoundFreshStartUp);
        if (input.PriorBasis != SubscriptionBasis.Paid || input.PriorPaidThrough is not { } paidThrough
            || input.PriorAnchor is not { } anchor || input.PriorOrdinal is not { } ordinal
            || !BillingCalendar.IsSupported(anchor) || anchor.Period != input.Period
            || BillingCalendar.Boundary(anchor, ordinal + 1) != paidThrough)
            return RenewalDecision.Unresolved;

        if (input.RefundAccessEndsAt is { } end)
            return input.RestoredAccessAt is { } restart && restart >= end && input.FulfilledAt >= end
                && !(input.VerifiedPaymentAt < end)
                ? Fresh(input.Period, restart, input.RoundFreshStartUp) with { Start = RenewalStart.RestoredAccess }
                : RenewalDecision.Unresolved;

        var graceEnd = paidThrough + GracePolicy.EligiblePaidGrace;
        if (input.FulfilledAt < graceEnd)
            return Continue(anchor, ordinal, paidThrough, input.VerifiedPaymentAt is { } verified && input.AcceptedQuote
                && verified <= input.FulfilledAt ? verified : input.FulfilledAt);
        if (input.VerifiedPaymentAt is { } paid && paid < graceEnd)
            return input.AcceptedQuote && paid <= input.FulfilledAt
                ? Continue(anchor, ordinal, paidThrough, paid) : RenewalDecision.Unresolved;
        return input.RestoredAccessAt is { } restored && restored >= graceEnd
            ? Fresh(input.Period, restored, input.RoundFreshStartUp) with { Start = RenewalStart.RestoredAccess }
            : RenewalDecision.Unresolved;
    }

    private static RenewalDecision Fresh(BillingPeriod period, DateTimeOffset startsAt, bool roundUp)
    {
        if (roundUp && startsAt.Ticks % TimeSpan.TicksPerSecond is var fraction and > 0)
            startsAt = startsAt.AddTicks(TimeSpan.TicksPerSecond - fraction);
        var anchor = new BillingAnchor(BillingCalendar.LocalStart(startsAt), BillingCalendar.Zone, period);
        var (start, end) = BillingCalendar.Period(anchor, 0);
        return new RenewalDecision(RenewalStart.Fulfilment, anchor, 0, start, end, 0);
    }

    private static RenewalDecision Continue(BillingAnchor anchor, int ordinal, DateTimeOffset paidThrough, DateTimeOffset effective)
    {
        var (start, end) = BillingCalendar.Period(anchor, ordinal + 1);
        var elapsed = effective - paidThrough;
        var days = elapsed <= TimeSpan.Zero ? 0 : (int)Math.Min(7, Math.Ceiling(elapsed.TotalDays));
        return new RenewalDecision(RenewalStart.PriorPaidThrough, anchor, ordinal + 1, start, end, days);
    }
}
