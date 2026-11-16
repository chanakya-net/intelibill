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

        var decision = await DecideAsync(quote, ct);
        await store.RecordVerifiedAsync(attempt, payment, decision.At, ct);
        if (decision.Term is null)
        {
            await store.AppendEventAsync(attempt, PaymentEventKinds.PaidNeedsReview, null, decision.At, ct);
            return FulfilmentOutcome.NeedsReview;
        }

        await FulfilAsync(quote, decision, attempt, null, ct);
        return FulfilmentOutcome.Fulfilled;
    }

    internal async Task<PaidOrderDecision> DecideAsync(CheckoutQuoteV1 quote, CancellationToken ct)
    {
        var now = BillingAccountRules.StorageInstant(lifecycle.GetUtcNow());
        if (await store.HasFulfilmentAsync(quote.AccountId, quote.OrderId, ct))
            return new(null, [], now, "DuplicateFunds");
        var ids = await orders.ShopIdsAsync(quote.AccountId, quote.OrderId, ct);
        var source = await orders.SourceAsync(quote.AccountId, null, ids, ct);
        if (source?.Hash() != quote.SourceHash || await store.AnyTrialAsync(quote.AccountId, ids, ct))
            return new(null, [], now, "CoverageChanged");

        var period = quote.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        if (RenewalPolicy.DetermineTerm(new(period, now)) is not { Anchor: { } anchor, Ordinal: { } ordinal,
                StartsAt: { } starts, EndsAt: { } ends })
            return new(null, [], now, "TermUnavailable");
        var term = new PaidTerm(Guid.NewGuid(), quote.Capacity == 1 ? "Independent" : "Grouped", quote.Capacity,
            starts, ends, quote.Period, anchor.LocalStart, ordinal);
        return new(term, ids, now, null);
    }

    internal Task FulfilAsync(CheckoutQuoteV1 quote, PaidOrderDecision decision, StoredPaymentAttempt? attempt,
        Guid? manualReceipt, CancellationToken ct) =>
        store.FulfilAsync(new(quote.AccountId, quote.OrderId, attempt, manualReceipt, quote.AmountPaise, quote.Currency,
            decision.Term!, quote.PlanVersionId, quote.MembersPerShop, quote.DevelopmentFixture, decision.Shops, decision.At), ct);

    internal sealed record PaidOrderDecision(PaidTerm? Term, Guid[] Shops, DateTimeOffset At, string? ReviewReason);
}

public static class PaymentFulfilmentPolicy
{
    public const string Version = "payment-fulfilment/1";

    /// <summary>Exactly the full captured amount, in the expected currency, on the pinned account and order.</summary>
    public static bool Funds(StoredPaymentAttempt attempt, PaymentObservation payment) =>
        payment.State == ObservedPaymentState.Captured && payment.Pin == attempt.Pin &&
        payment.AmountPaise == attempt.AmountPaise && payment.Currency == attempt.Currency &&
        payment.ProviderOrderReference == attempt.ProviderOrderReference &&
        payment.ProviderPaymentReference is { Length: > 0 and <= 120 } && payment.CapturedAt is not null;
}
