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
public sealed partial class FulfilPaidOrderHandler(IPaymentStore store, ICheckoutOrderStore orders, IPaymentReviewStore reviews, TimeProvider lifecycle)
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
        if (!PaymentFulfilmentPolicy.Funds(attempt, payment))
        {
            var at = BillingAccountRules.StorageInstant(lifecycle.GetUtcNow());
            if (UnmatchedPaymentClassification.HasCaptureEvidence(payment) &&
                UnmatchedPaymentClassification.Discrepancy(attempt, payment, attempt.ProviderOrderReference) is { } mismatch &&
                !(payment.CapturedAt > at || payment.CreatedAt > at))
                await reviews.RecordCaptureAsync(attempt, payment, mismatch, at, null, ct);
            return FulfilmentOutcome.Rejected;
        }
        if (await orders.FindAsync(attempt.AccountId, attempt.OrderId, false, ct) is not { Quote: var quote })
            return FulfilmentOutcome.Rejected;

        // A proven capture time is the payment's effective time. A creation-only time proves just a lower bound, so the
        // verification instant bounds it from above and a renewal needs both bounds to agree.
        var proven = payment.CapturedAt is not null;
        var effective = BillingAccountRules.StorageInstant(payment.CapturedAt ?? payment.CreatedAt!.Value);
        var notBefore = proven ? attempt.CreatedAt : new DateTimeOffset(attempt.CreatedAt.Ticks / TimeSpan.TicksPerSecond *
            TimeSpan.TicksPerSecond, attempt.CreatedAt.Offset);
        var decision = await DecideAsync(quote, effective, ct, notBefore, upperBound: !proven, attempt: attempt.AttemptId);
        await store.RecordVerifiedAsync(attempt, payment, decision.At, ct);
        if (decision.Term is null)
        {
            if (decision.Hold is { } hold)
                await store.RecordRestorationHoldAsync(quote.AccountId, quote.OrderId, attempt.AttemptId, null, proven ? effective : decision.At, hold, ct);
            await reviews.RecordCaptureAsync(attempt, payment, decision.ReviewReason!, decision.At, null, ct);
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
        DateTimeOffset? notBefore = null, bool upperBound = false, PaymentRestorationHold? retry = null,
        Guid? attempt = null, ReviewRecoverySource? recovery = null)
    {
        var now = BillingAccountRules.StorageInstant(lifecycle.GetUtcNow());
        if (await store.HasFulfilmentAsync(quote.AccountId, quote.OrderId, ct) ||
            retry is null && (await store.FindRestorationHoldAsync(quote.AccountId, quote.OrderId, ct) is not null ||
                await reviews.HasCompetingFundsAsync(quote.AccountId, quote.OrderId, attempt, recovery?.ReceiptId, recovery?.CaseId, ct)))
            return new(null, [], now, "DuplicateFunds");
        var ids = await orders.ShopIdsAsync(quote.AccountId, quote.OrderId, ct);
        var source = recovery?.Source ?? await CheckoutRenewalPolicy.SourceAsync(orders, quote, ct);
        if (source is null || recovery is null && !source.Matches(quote))
            return new(null, [], now, "CoverageChanged");
        if (effectiveAt < notBefore)
            return new(null, [], now, "TermUnavailable");
        var latestPossible = recovery?.LatestPossibleCaptureAt ?? (upperBound ? now : effectiveAt);
        if (CheckoutRenewalPolicy.IsRenewal(quote))
            return RenewalDecision(quote, source, effectiveAt, latestPossible, now, ids);

        // Qualify the accepted quote using the earliest possible capture. Persist the same effective
        // instant as the verified payment so restoration retries retain the original evidence.
        return await DecideFreshAsync(quote, source, ids, effectiveAt, latestPossible, now, retry, ct);
    }

    private async Task<PaidOrderDecision> DecideFreshAsync(CheckoutQuoteV1 quote, CheckoutSource source, Guid[] ids,
        DateTimeOffset earliestCaptureAt, DateTimeOffset effectiveAt, DateTimeOffset now, PaymentRestorationHold? retry, CancellationToken ct)
    {
        if (CheckoutRenewalPolicy.FreshTerm(quote, source, earliestCaptureAt, now) is not { } fresh)
            return new(null, [], now, "TermUnavailable");
        var evidence = await store.ReadRestorationEvidenceAsync(quote.AccountId, now, ct);
        if (evidence.State != PaymentRestorationState.Ready || retry is not null && !evidence.Resolves(retry))
            return new(null, [], now, "RestorationPending", Hold: evidence);
        var renewal = quote.TermId is { } prior ? new RenewalProvenance(prior, effectiveAt, 0) : null;
        return new(fresh.Term, ids, now, null, renewal, new(evidence, fresh.Kind, effectiveAt));
    }

    public Task<ErrorOr<FulfilmentOutcome>> RetryAsync(StoredPaymentAttempt attempt, CancellationToken ct) =>
        store.InAttemptScopeAsync<FulfilmentOutcome>(attempt.AccountId, attempt.AttemptId, true, async token =>
            await RetryLockedAsync(attempt.AccountId, attempt.OrderId, attempt, null, token)
                ? FulfilmentOutcome.Fulfilled : FulfilmentOutcome.AlreadyRecorded, ct);

    internal async Task<bool> RetryLockedAsync(Guid account, Guid order, StoredPaymentAttempt? attempt, Guid? receipt,
        CancellationToken ct)
    {
        if (await store.FindRestorationHoldAsync(account, order, ct) is not { } hold ||
            hold.AttemptId != attempt?.AttemptId || hold.ManualReceiptId != receipt ||
            await orders.FindAsync(account, order, false, ct) is not { Quote: var quote })
            return false;
        if (await reviews.HasCompetingFundsAsync(account, order, attempt?.AttemptId, receipt, null, ct))
            return false;
        var decision = await DecideAsync(quote, hold.EffectiveAt, ct, notBefore: attempt?.CreatedAt, retry: hold);
        if (decision.Term is null)
            return false;
        await FulfilAsync(quote, decision, attempt, receipt, ct);
        return true;
    }

    private static PaidOrderDecision RenewalDecision(CheckoutQuoteV1 quote, CheckoutSource source, DateTimeOffset effectiveAt,
        DateTimeOffset latestPossible, DateTimeOffset now, Guid[] ids)
    {
        if (source.Prior is not { } prior ||
            CheckoutRenewalPolicy.Successor(quote, prior, effectiveAt, now, out var grace) is not { } successor)
            return new(null, [], now, "TermUnavailable");
        if (latestPossible != effectiveAt &&
            (CheckoutRenewalPolicy.Successor(quote, prior, latestPossible, now, out var late) is null || late != grace))
            return new(null, [], now, "CaptureTimeUnproven");
        return new(successor, ids, now, null, new(quote.TermId!.Value, latestPossible, grace));
    }

    internal Task FulfilAsync(CheckoutQuoteV1 quote, PaidOrderDecision decision, StoredPaymentAttempt? attempt,
        Guid? manualReceipt, CancellationToken ct) =>
        store.FulfilAsync(new(quote.AccountId, quote.OrderId, attempt, manualReceipt, quote.AmountPaise, quote.Currency,
            decision.Term!, quote.PlanVersionId, quote.MembersPerShop, quote.DevelopmentFixture, decision.Shops, decision.At,
            decision.Renewal, decision.Fresh), ct);

    internal sealed record PaidOrderDecision(PaidTerm? Term, Guid[] Shops, DateTimeOffset At, string? ReviewReason,
        RenewalProvenance? Renewal = null, FreshPaymentTerm? Fresh = null, PaymentRestorationEvidence? Hold = null);
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
