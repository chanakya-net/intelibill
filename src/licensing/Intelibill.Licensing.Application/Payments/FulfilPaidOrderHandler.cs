using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Policies;

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

        var ids = await orders.ShopIdsAsync(attempt.AccountId, attempt.OrderId, ct);
        var source = await orders.SourceAsync(attempt.AccountId, null, ids, ct);
        var now = BillingAccountRules.StorageInstant(lifecycle.GetUtcNow());
        await store.RecordVerifiedAsync(attempt, payment, now, ct);
        if (source?.Hash() != quote.SourceHash || await store.AnyTrialAsync(attempt.AccountId, ids, ct))
        {
            await store.AppendEventAsync(attempt, PaymentEventKinds.PaidNeedsReview, null, now, ct);
            return FulfilmentOutcome.NeedsReview;
        }

        var period = quote.Period == "Month" ? BillingPeriod.Month : BillingPeriod.Year;
        if (RenewalPolicy.DetermineTerm(new(period, now)) is not { Anchor: { } anchor, Ordinal: { } ordinal,
                StartsAt: { } starts, EndsAt: { } ends })
            return FulfilmentOutcome.Rejected;
        var term = new PaidTerm(Guid.NewGuid(), quote.Capacity == 1 ? "Independent" : "Grouped", quote.Capacity,
            starts, ends, quote.Period, anchor.LocalStart, ordinal);
        await store.FulfilAsync(new(attempt, payment, term, quote.PlanVersionId, quote.MembersPerShop,
            quote.DevelopmentFixture, ids, now), ct);
        return FulfilmentOutcome.Fulfilled;
    }
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
