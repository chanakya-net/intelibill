using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record ReviewPaymentCommand(Guid OperationKey, long ExpectedVersion, string Action, string Reason, string Evidence,
    string? SourceHash = null);

public sealed record PaymentReviewDecision(Guid CaseId, Guid OperationKey, long Version, string Action, string Disposition,
    Guid AdminId, DateTimeOffset DecidedAt, DateTimeOffset MfaVerifiedAt, string Reason, string Evidence, Guid? TermId);

public sealed record PaymentReviewDetail(UnmatchedPayment Case, long Version, PaymentReviewDecision? Decision,
    CheckoutQuoteV1 Order, CheckoutShopPageV1 Shops, string? ExpectedReceivingAccount, string? ExpectedEnvironment,
    string? ExpectedConfigurationVersion, string? RecoverySourceHash, string? RecoveryBlocker,
    DateTimeOffset? RecoveryStartsAt, DateTimeOffset? RecoveryEndsAt, PaymentReviewReferences References);

public sealed record PaymentReviewReferences(string? ExpectedOrder, string? ObservedOrder, string? ObservedPayment);

public static class PaymentReviewErrors
{
    public static Error Missing => Error.NotFound("PaymentReview.CaseNotFound", "The payment case is unavailable.");
    public static Error OperationMissing => Error.NotFound("PaymentReview.OperationNotFound", "The payment decision is unavailable.");
    public static Error Invalid => Error.Validation("PaymentReview.InvalidDecision", "Choose a decision and provide a reason and evidence.");
    public static Error Stale => Error.Conflict("PaymentReview.CaseVersionConflict", "The payment review changed. Refresh and review the evidence again.");
    public static Error PayloadMismatch => Error.Conflict("PaymentReview.OperationPayloadMismatch", "This operation key already has different evidence.");
    public static Error RecoveryUnavailable => Error.Conflict("PaymentReview.RecoveryUnavailable", "The original accepted terms cannot currently be recovered.");
}

public interface IPaymentReviewDecisionStore
{
    Task<ErrorOr<T>> InReviewScopeAsync<T>(AdminSessionCaller caller, Guid account, bool write,
        Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);
    Task<PaymentReviewReferences> FindReferencesAsync(Guid account, Guid id, CancellationToken ct);
    Task<ReviewFundingTime?> FindFundingAsync(Guid account, UnmatchedPayment review, CancellationToken ct);
    Task UseReviewOrderAsync(Guid order, CancellationToken ct);
    Task UseReviewFundingAsync(UnmatchedPayment review, CancellationToken ct);
    Task<UnmatchedPayment?> FindCaseAsync(Guid account, Guid id, CancellationToken ct);
    Task<PaymentReviewDecision?> FindDecisionAsync(Guid account, Guid key, bool byOperation, CancellationToken ct);
    Task<bool> SameDecisionAsync(Guid account, PaymentReviewDecision decision, ReviewPaymentCommand command, CancellationToken ct);
    Task SaveDecisionAsync(Guid account, AdminSessionCaller caller, PaymentReviewDecision decision, ReviewPaymentCommand command,
        string? acceptedSourceHash, string? recoveryPolicy, CancellationToken ct);
}
