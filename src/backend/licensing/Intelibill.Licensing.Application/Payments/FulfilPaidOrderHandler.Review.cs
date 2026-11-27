using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed partial class FulfilPaidOrderHandler
{
    internal async Task<PaidOrderDecision> DecideReviewAsync(CheckoutQuoteV1 quote, UnmatchedPayment review,
        CheckoutSource source, ReviewFundingTime timing, StoredPaymentAttempt? attempt, CancellationToken ct)
    {
        var now = BillingAccountRules.StorageInstant(lifecycle.GetUtcNow());
        if (await reviews.HasCompetingFundsAsync(quote.AccountId, quote.OrderId, attempt?.AttemptId, review.ManualReceiptId,
                review.CaseId, ct))
            return new(null, [], now, "DuplicateFunds");
        if (timing.LatestPossibleCaptureAt > now)
            return new(null, [], now, "CaptureTimeUnproven");
        if ((await store.ReadRestorationEvidenceAsync(quote.AccountId, now, ct)).State != PaymentRestorationState.Ready)
            return new(null, [], now, "RestorationPending");
        var hold = await store.FindRestorationHoldAsync(quote.AccountId, quote.OrderId, ct);
        if (hold is not null && (hold.AttemptId != attempt?.AttemptId || hold.ManualReceiptId != review.ManualReceiptId))
            return new(null, [], now, "DuplicateFunds");
        // Direct provider times are exact. A provider creation time may have second precision.
        var notBefore = attempt is null ? quote.CreatedAt : review.CapturedAt is null
            ? attempt.CreatedAt.AddTicks(-(attempt.CreatedAt.Ticks % TimeSpan.TicksPerSecond)) : attempt.CreatedAt;
        return await DecideAsync(quote, timing.EarliestCaptureAt, ct, notBefore, retry: hold, attempt: attempt?.AttemptId,
            recovery: new(review.CaseId, review.ManualReceiptId, source, timing.LatestPossibleCaptureAt));
    }
}
