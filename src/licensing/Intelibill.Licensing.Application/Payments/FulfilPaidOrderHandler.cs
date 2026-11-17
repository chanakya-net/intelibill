using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public enum FulfilmentOutcome { AlreadyRecorded, Rejected, NeedsReview, Fulfilled }

/// <summary>
/// System act on verified funds, never on a caller: under the billing-account lock it commits the verified payment,
/// one paid term for the complete frozen package, its shop links, audit event and entitlement outbox snapshots in
/// one transaction. A payment already recorded for the attempt returns without writing again.
/// </summary>
public sealed class FulfilPaidOrderHandler(IPaymentStore store, ICheckoutOrderStore orders, TimeProvider lifecycle)
{
    public Task<ErrorOr<FulfilmentOutcome>> HandleAsync(StoredPaymentAttempt attempt, PaymentObservation payment,
        CancellationToken ct) =>
        store.InAttemptScopeAsync<FulfilmentOutcome>(attempt.AccountId, attempt.AttemptId, true,
            token => ApplyAsync(attempt, payment, token), ct);

    internal async Task<ErrorOr<FulfilmentOutcome>> ApplyAsync(StoredPaymentAttempt attempt, PaymentObservation payment,
        CancellationToken ct)
    {
        if (await store.HasVerifiedPaymentAsync(attempt.AttemptId, ct))
            return FulfilmentOutcome.AlreadyRecorded;
        // Wrong amount, currency, account or environment fails closed; durable unmatched-funds records are #870.
        if (!PaymentFulfilmentPolicy.Funds(attempt, payment))
            return FulfilmentOutcome.Rejected;
        if (await orders.FindAsync(attempt.AccountId, attempt.OrderId, false, ct) is not { Quote: var quote })
            return FulfilmentOutcome.Rejected;

        // A proven capture time is the payment's effective time. A creation-only time proves just a lower bound, so the
        // verification instant bounds it from above and a renewal needs both bounds to agree.
        var proven = payment.CapturedAt is not null;
        var effective = BillingAccountRules.StorageInstant(payment.CapturedAt ?? payment.CreatedAt!.Value);
        var notBefore = proven ? attempt.CreatedAt : new DateTimeOffset(attempt.CreatedAt.Ticks / TimeSpan.TicksPerSecond *
            TimeSpan.TicksPerSecond, attempt.CreatedAt.Offset);
        var decision = await DecideAsync(quote, effective, ct, notBefore, upperBound: !proven);
        await store.RecordVerifiedAsync(attempt, payment, decision.At, ct);
        if (decision.Term is null)
        {
            await store.AppendEventAsync(attempt, PaymentEventKinds.PaidNeedsReview, null, decision.At, ct);
            return FulfilmentOutcome.NeedsReview;
        }

        await FulfilAsync(quote, decision, attempt, null, ct);
        return FulfilmentOutcome.Fulfilled;
    }

    /// <summary>
    /// Decides under the account lock. A renewal is judged against its accepted quote and the payment's verified
    /// effective time: a stale or superseded source, or a period that no longer matches the quote, needs review.
    /// </summary>
    internal async Task<PaidOrderDecision> DecideAsync(CheckoutQuoteV1 quote, DateTimeOffset effectiveAt, CancellationToken ct,
        DateTimeOffset? notBefore = null, bool upperBound = false)
    {
        var now = BillingAccountRules.StorageInstant(lifecycle.GetUtcNow());
        if (await store.HasFulfilmentAsync(quote.AccountId, quote.OrderId, ct))
            return new(null, [], now, "DuplicateFunds");
        var ids = await orders.ShopIdsAsync(quote.AccountId, quote.OrderId, ct);
        var source = await CheckoutRenewalPolicy.SourceAsync(orders, quote, ct);
        if (source?.Hash() != quote.SourceHash || await store.AnyTrialAsync(quote.AccountId, ids, ct))
            return new(null, [], now, "CoverageChanged");
        if (CheckoutRenewalPolicy.IsRenewal(quote))
            return RenewalDecision(quote, source, effectiveAt, notBefore, upperBound, now, ids);

        var period = quote.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        if (RenewalPolicy.DetermineTerm(new(period, now)) is not { Anchor: { } anchor, Ordinal: { } ordinal,
                StartsAt: { } starts, EndsAt: { } ends })
            return new(null, [], now, "TermUnavailable");
        var term = new PaidTerm(Guid.NewGuid(), quote.Capacity == 1 ? "Independent" : "Grouped", quote.Capacity,
            starts, ends, quote.Period, anchor.LocalStart, ordinal);
        return new(term, ids, now, null);
    }

    private static PaidOrderDecision RenewalDecision(CheckoutQuoteV1 quote, CheckoutSource source, DateTimeOffset effectiveAt,
        DateTimeOffset? notBefore, bool upperBound, DateTimeOffset now, Guid[] ids)
    {
        if (effectiveAt < (notBefore ?? DateTimeOffset.MinValue) || source.Prior is not { } prior ||
            CheckoutRenewalPolicy.Successor(quote, prior, effectiveAt, now, out var grace) is not { } successor)
            return new(null, [], now, "TermUnavailable");
        // Capture happened somewhere in [effectiveAt, now]; both ends must classify to the same renewal or it is unproved.
        if (upperBound && (CheckoutRenewalPolicy.Successor(quote, prior, now, now, out var late) is null || late != grace))
            return new(null, [], now, "CaptureTimeUnproven");
        return new(successor, ids, now, null, new(quote.TermId!.Value, upperBound ? now : effectiveAt, grace));
    }

    internal Task FulfilAsync(CheckoutQuoteV1 quote, PaidOrderDecision decision, StoredPaymentAttempt? attempt,
        Guid? manualReceipt, CancellationToken ct) =>
        store.FulfilAsync(new(quote.AccountId, quote.OrderId, attempt, manualReceipt, quote.AmountPaise, quote.Currency,
            decision.Term!, quote.PlanVersionId, quote.MembersPerShop, quote.DevelopmentFixture, decision.Shops, decision.At,
            decision.Renewal), ct);

    internal sealed record PaidOrderDecision(PaidTerm? Term, Guid[] Shops, DateTimeOffset At, string? ReviewReason,
        RenewalProvenance? Renewal = null);
}

public static class PaymentFulfilmentPolicy
{
    public const string Version = "payment-fulfilment/1";

    /// <summary>Exactly the full captured amount, in the expected currency, on the pinned account and order.</summary>
    public static bool Funds(StoredPaymentAttempt attempt, PaymentObservation payment) =>
        payment.State == ObservedPaymentState.Captured && payment.Pin == attempt.Pin &&
        payment.AmountPaise == attempt.AmountPaise && payment.Currency == attempt.Currency &&
        payment.ProviderOrderReference == attempt.ProviderOrderReference &&
        payment.ProviderPaymentReference is { Length: > 0 and <= 120 } && (payment.CapturedAt ?? payment.CreatedAt) is not null;
}
