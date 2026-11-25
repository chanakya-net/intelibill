using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>Immutable evidence that a fulfilment extended its quoted paid term.</summary>
public sealed record RenewalProvenance(Guid PredecessorTermId, DateTimeOffset PaymentEffectiveAt, int GraceDaysIncluded);

/// <summary>
/// The payable forms of an accepted quote and the end-exclusive window in which a prior-paid-through renewal may
/// still be accepted or started: <c>[quote created, prior paid-through + 7 days)</c>, independent of the 15-minute
/// quote lifetime. Proof of a payment made inside the window may arrive later; it is judged against the accepted
/// quote and its verified effective time, never the observation time.
/// </summary>
public static class CheckoutRenewalPolicy
{
    public const string FirstPurchase = nameof(RenewalStart.Fulfilment);
    public const string Renewal = nameof(RenewalStart.PriorPaidThrough);

    public static bool IsRenewal(CheckoutQuoteV1 quote) => quote.TermId is not null && quote.StartRule == Renewal;

    public static bool IsRestoredAccess(CheckoutQuoteV1 quote) =>
        quote.TermId is not null && quote.StartRule == nameof(RenewalStart.RestoredAccess) &&
        quote.FreshStart?.Policy == CheckoutOrdersV1.FreshStartPolicy;

    public static bool Payable(CheckoutQuoteV1 quote) =>
        quote.Acceptance is not null && (quote.TermId is null ? quote.StartRule == FirstPurchase
            : IsRenewal(quote) || IsRestoredAccess(quote) || quote.Increase is not null);

    public static bool WindowOpen(CheckoutQuoteV1 quote, DateTimeOffset now) =>
        quote.Increase is not null ? now < quote.PreviewEndsAt : !IsRenewal(quote) || now < quote.PreviewStartsAt + GracePolicy.EligiblePaidGrace;

    /// <summary>The live source the quote was frozen from: its paid term, or its named open shops.</summary>
    public static async Task<CheckoutSource?> SourceAsync(ICheckoutOrderStore orders, CheckoutQuoteV1 quote, CancellationToken ct) =>
        quote.TermId is { } term ? await orders.SourceAsync(quote.AccountId, term, [], ct)
            : await orders.SourceAsync(quote.AccountId, null, await orders.ShopIdsAsync(quote.AccountId, quote.OrderId, ct), ct);

    /// <summary>The source and named coverage window must still support the quoted purchase at decision time.</summary>
    internal static async Task<bool> SourceIsCurrentAsync(ICheckoutOrderStore orders, CheckoutQuoteV1 quote,
        DateTimeOffset now, CancellationToken ct)
    {
        var source = await SourceAsync(orders, quote, ct);
        if (source?.Matches(quote) != true || !await orders.IncreaseStillCurrentAsync(quote, now, ct) ||
            !await orders.ReductionOrderCurrentAsync(quote, now, ct))
            return false;
        if (quote.Increase is not null)
            return true; // An increase changes capacity within an existing term.
        var start = IsRestoredAccess(quote) && now > quote.PreviewStartsAt ? now : quote.PreviewStartsAt;
        var end = quote.PreviewEndsAt > start ? quote.PreviewEndsAt : start.AddDays(1);
        return !await orders.HasConflictingPaidCoverageAsync(quote.AccountId, [.. source.Shops.Select(s => s.ShopId)],
            quote.TermId, start, end, ct);
    }

    /// <summary>
    /// The successor period for verified funds effective at <paramref name="effectiveAt"/> and observed at
    /// <paramref name="now"/>: exactly the accepted quote's period on the prior anchor, or null.
    /// </summary>
    public static PaidTerm? Successor(CheckoutQuoteV1 quote, CheckoutRenewalSource prior, DateTimeOffset effectiveAt,
        DateTimeOffset now, out int graceDays)
    {
        graceDays = 0;
        var period = quote.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        if (effectiveAt > now || prior.Basis != "Paid" || prior.Period != quote.Period || prior.Capacity != quote.Capacity ||
            prior.CancellationRevision % 2 == 1 || prior.EndsAt != quote.PreviewStartsAt)
            return null;
        var anchor = new BillingAnchor(prior.AnchorLocal, BillingCalendar.Zone, period);
        var decision = RenewalPolicy.DetermineTerm(new(period, now, SubscriptionBasis.Paid, prior.EndsAt, anchor, prior.Ordinal,
            VerifiedPaymentAt: effectiveAt, AcceptedQuote: true));
        if (decision is not { Start: RenewalStart.PriorPaidThrough, Ordinal: { } ordinal, StartsAt: { } starts, EndsAt: { } ends }
            || starts != quote.PreviewStartsAt || ends != quote.PreviewEndsAt || ordinal != quote.Ordinal)
            return null;
        graceDays = decision.IncludedGraceDays;
        return new PaidTerm(Guid.NewGuid(), prior.Mode, prior.Capacity, starts, ends, quote.Period, prior.AnchorLocal, ordinal);
    }

    public static bool ReductionTiming(CheckoutQuoteV1 quote, CheckoutRenewalSource prior, DateTimeOffset effectiveAt,
        DateTimeOffset now, out int graceDays)
    {
        graceDays = 0;
        var period = quote.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        if (quote.Reduction is null || effectiveAt > now || prior.Basis != "Paid" || prior.Period != quote.Period ||
            prior.CancellationRevision % 2 == 1 || prior.EndsAt != quote.PreviewStartsAt)
            return false;
        var anchor = new BillingAnchor(prior.AnchorLocal, BillingCalendar.Zone, period);
        var decision = RenewalPolicy.DetermineTerm(new(period, now, SubscriptionBasis.Paid, prior.EndsAt, anchor, prior.Ordinal,
            VerifiedPaymentAt: effectiveAt, AcceptedQuote: true));
        if (decision is not { Start: RenewalStart.PriorPaidThrough, Ordinal: { } ordinal, StartsAt: { } starts, EndsAt: { } ends }
            || starts != quote.PreviewStartsAt || ends != quote.PreviewEndsAt || ordinal != quote.Ordinal)
            return false;
        graceDays = decision.IncludedGraceDays;
        return true;
    }

    public static (PaidTerm Term, string Kind)? FreshTerm(CheckoutQuoteV1 quote, CheckoutSource source,
        DateTimeOffset effectiveAt, DateTimeOffset now)
    {
        var period = quote.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        var prior = source.Prior;
        var restored = IsRestoredAccess(quote);
        if (!Payable(quote) || effectiveAt > now || restored != (prior is not null))
            return null;
        var input = prior is null
            ? new RenewalInput(period, now, source.Trials is { Length: > 0 } ? SubscriptionBasis.Trial : null,
                RoundFreshStartUp: quote.FreshStart is not null)
            : new RenewalInput(period, now, SubscriptionBasis.Paid, prior.EndsAt,
                new BillingAnchor(prior.AnchorLocal, BillingCalendar.Zone, period), prior.Ordinal,
                VerifiedPaymentAt: effectiveAt, AcceptedQuote: true, RestoredAccessAt: now, RoundFreshStartUp: true, RefundAccessEndsAt: prior.RefundAccessEndsAt);
        var rule = restored ? RenewalStart.RestoredAccess : RenewalStart.Fulfilment;
        if (RenewalPolicy.DetermineTerm(input) is not { Anchor: { } anchor, Ordinal: 0,
            StartsAt: { } starts, EndsAt: { } ends } decision || decision.Start != rule)
            return null;
        var mode = prior?.Mode ?? (quote.Capacity == 1 ? "Independent" : "Grouped");
        var kind = restored ? "RestoredAccess" : source.Trials is { Length: > 0 } ? "TrialConversion" : "FirstPurchase";
        return (new(Guid.NewGuid(), mode, quote.Capacity, starts, ends, quote.Period, anchor.LocalStart, 0), kind);
    }
}
