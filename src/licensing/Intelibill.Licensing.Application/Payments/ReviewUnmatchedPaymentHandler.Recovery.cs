using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed partial class ReviewUnmatchedPaymentHandler
{
    private async Task<RecoveryPreview> PreviewRecoveryAsync(Guid account, UnmatchedPayment review, CheckoutQuoteV1 quote,
        StoredPaymentAttempt? attempt, CancellationToken ct)
    {
        if (review.Classification is not ("CoverageChanged" or "LegacyNeedsReview" or "RestorationPending"))
            return new(null, review.Classification, null);
        if (!CheckoutRenewalPolicy.Payable(quote) || quote.Acceptance!.AcceptedAt >= quote.ExpiresAt)
            return new(null, "AcceptedTermsUnavailable", null);
        var source = await CheckoutRenewalPolicy.SourceAsync(orders, quote, ct);
        var frozen = await orders.ReviewShopsAsync(quote.AccountId, quote.OrderId, ct);
        var revisions = quote.TermId is { } term ? await orders.WithdrawnRevisionsAsync(account, term, ct) : [];
        var equivalent = PaymentReviewRecoveryPolicy.Equivalent(quote, source, frozen, revisions);
        if (equivalent is null)
            return new(null, "CoverageChanged", null);
        if (await reviews.FindFundingAsync(account, review, ct) is not { } timing)
            return new(null, "CaptureTimeUnproven", null);
        var decision = await fulfilment.DecideReviewAsync(quote, review, equivalent, timing, attempt, ct);
        return decision.Term is null ? new(null, decision.ReviewReason, null) : new(equivalent.Hash(), null, decision);
    }

    private async Task<ErrorOr<PaymentReviewOutcome>> RecoverLockedAsync(Guid account, UnmatchedPayment review,
        ReviewPaymentCommand command, PaymentReviewDecision decision, AdminSessionCaller caller, CancellationToken ct)
    {
        await reviews.UseReviewOrderAsync(review.OrderId, ct);
        if (await orders.FindAsync(account, review.OrderId, false, ct) is not { Quote: var quote })
            return PaymentReviewErrors.Missing;
        var attempt = review.AttemptId is { } id ? await payments.FindAttemptAsync(account, id, false, ct) : null;
        var preview = await PreviewRecoveryAsync(account, review, quote, attempt, ct);
        if (preview.Decision?.Term is not { } term)
            return PaymentReviewErrors.RecoveryUnavailable;
        if (preview.SourceHash != command.SourceHash)
            return PaymentReviewErrors.Stale;
        var recovered = decision with { TermId = term.TermId, Disposition = "AcceptedTermsRecovered" };
        await reviews.SaveDecisionAsync(account, caller, recovered, command, quote.SourceHash, PaymentReviewRecoveryPolicy.Version, ct);
        await reviews.UseReviewFundingAsync(review, ct);
        await fulfilment.FulfilAsync(quote, preview.Decision, attempt, review.ManualReceiptId, ct);
        return new PaymentReviewOutcome(recovered, true);
    }

    private sealed record RecoveryPreview(string? SourceHash, string? Blocker, FulfilPaidOrderHandler.PaidOrderDecision? Decision);
}
