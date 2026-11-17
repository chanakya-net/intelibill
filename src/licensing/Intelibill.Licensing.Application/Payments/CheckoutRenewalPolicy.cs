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

    /// <summary>First purchase or prior-paid-through renewal; trial conversion and restored access (#872) are not.</summary>
    public static bool Payable(CheckoutQuoteV1 quote) =>
        quote.Acceptance is not null && (quote.TermId is null ? quote.StartRule == FirstPurchase : IsRenewal(quote));

    public static bool WindowOpen(CheckoutQuoteV1 quote, DateTimeOffset now) =>
        !IsRenewal(quote) || now < quote.PreviewStartsAt + GracePolicy.EligiblePaidGrace;

    /// <summary>The live source the quote was frozen from: its paid term, or its named open shops.</summary>
    public static async Task<CheckoutSource?> SourceAsync(ICheckoutOrderStore orders, CheckoutQuoteV1 quote, CancellationToken ct) =>
        quote.TermId is { } term ? await orders.SourceAsync(quote.AccountId, term, [], ct)
            : await orders.SourceAsync(quote.AccountId, null, await orders.ShopIdsAsync(quote.AccountId, quote.OrderId, ct), ct);

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
}
