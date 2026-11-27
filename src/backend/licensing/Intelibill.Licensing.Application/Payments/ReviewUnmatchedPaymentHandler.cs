using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.BillingAccounts;

namespace Intelibill.Licensing.Application.Payments;

public sealed partial class ReviewUnmatchedPaymentHandler(IPaymentReviewDecisionStore reviews, ICheckoutOrderStore orders,
    IPaymentStore payments, FulfilPaidOrderHandler fulfilment, TimeProvider clock)
{
    public Task<ErrorOr<PaymentReviewDetail>> ReadAsync(AdminSessionCaller caller, Guid account, Guid id, Guid? after, CancellationToken ct) =>
        reviews.InReviewScopeAsync<PaymentReviewDetail>(caller, account, true, async (_, token) =>
        {
            if (await reviews.FindCaseAsync(account, id, token) is not { } review)
                return PaymentReviewErrors.Missing;
            await reviews.UseReviewOrderAsync(review.OrderId, token);
            if (await orders.FindAsync(account, review.OrderId, false, token) is not { Quote: var quote })
                return PaymentReviewErrors.Missing;
            var decision = await reviews.FindDecisionAsync(account, id, false, token);
            var attempt = review.AttemptId is { } attemptId ? await payments.FindAttemptAsync(account, attemptId, false, token) : null;
            var preview = decision is null ? await PreviewRecoveryAsync(account, review, quote, attempt, token) : null;
            return new PaymentReviewDetail(review, decision?.Version ?? 0, decision, quote,
                await orders.PageAsync(account, quote.OrderId, after, token),
                attempt?.Pin.ProviderAccountId ?? review.ReceivingAccountId, attempt?.Pin.Environment ?? review.Environment,
                attempt?.Pin.ConfigurationVersion ?? review.ConfigurationVersion, preview?.SourceHash, preview?.Blocker,
                preview?.Decision?.Term?.StartsAt, preview?.Decision?.Term?.EndsAt,
                await reviews.FindReferencesAsync(account, id, token));
        }, ct);

    public Task<ErrorOr<PaymentReviewDecision>> RecoverAsync(AdminSessionCaller caller, Guid account, Guid id, Guid key, CancellationToken ct) =>
        reviews.InReviewScopeAsync<PaymentReviewDecision>(caller, account, false, async (_, token) =>
        {
            var decision = await reviews.FindDecisionAsync(account, key, true, token);
            return decision is { } prior && prior.CaseId == id && prior.AdminId == caller.AdminId
                ? prior : PaymentReviewErrors.OperationMissing;
        }, ct);

    public Task<ErrorOr<PaymentReviewOutcome>> DecideAsync(AdminSessionCaller caller, Guid account, Guid id,
        ReviewPaymentCommand? command, CancellationToken ct) =>
        reviews.InReviewScopeAsync<PaymentReviewOutcome>(caller, account, true, (mfa, token) =>
            DecideLockedAsync(caller, account, id, command, mfa, token), ct);

    private async Task<ErrorOr<PaymentReviewOutcome>> DecideLockedAsync(AdminSessionCaller caller, Guid account, Guid id,
        ReviewPaymentCommand? command, DateTimeOffset mfa, CancellationToken ct)
    {
        if (command is null || !Valid(command))
            return PaymentReviewErrors.Invalid;
        if (await reviews.FindDecisionAsync(account, command.OperationKey, true, ct) is { } prior)
            return prior.CaseId == id && prior.AdminId == caller.AdminId && await reviews.SameDecisionAsync(account, prior, command, ct)
                ? new PaymentReviewOutcome(prior, false) : PaymentReviewErrors.PayloadMismatch;
        if (await reviews.FindCaseAsync(account, id, ct) is not { } review)
            return PaymentReviewErrors.Missing;
        if (command.ExpectedVersion != 0 || await reviews.FindDecisionAsync(account, id, false, ct) is not null)
            return PaymentReviewErrors.Stale;

        var disposition = command.Action == "ReferForRefundReview" ? "RefundReviewPending" : "NoAccessDecisionRecorded";
        var decision = new PaymentReviewDecision(review.CaseId, command.OperationKey, 1, command.Action, disposition,
            caller.AdminId, BillingAccountRules.StorageInstant(clock.GetUtcNow()), mfa, command.Reason.Trim(), command.Evidence.Trim(), null);
        if (command.Action == "RecoverAcceptedTerms")
            return await RecoverLockedAsync(account, review, command, decision, caller, ct);
        await reviews.SaveDecisionAsync(account, caller, decision, command, null, null, ct);
        return new PaymentReviewOutcome(decision, true);
    }

    private static bool Valid(ReviewPaymentCommand command) =>
        command.OperationKey != Guid.Empty && command.ExpectedVersion >= 0 &&
        (command.Action is "RecordNoAccessDecision" or "ReferForRefundReview" && command.SourceHash is null ||
            command.Action == "RecoverAcceptedTerms" && command.SourceHash is { Length: 64 } hash && hash.All(char.IsAsciiHexDigitLower)) &&
        ValidText(command.Reason, 400) && ValidText(command.Evidence, 400);

    private static bool ValidText(string? text, int limit) =>
        text is not null && text.Length <= limit && text.Trim().Length > 0 && !text.Any(char.IsControl);
}

public sealed record PaymentReviewOutcome(PaymentReviewDecision Decision, bool Created);
