using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;

namespace Intelibill.Licensing.Application.Payments;

public sealed record UnmatchedPayment(Guid CaseId, Guid OrderId, Guid? AttemptId, Guid? ManualReceiptId,
    string Classification, long ExpectedAmountPaise, string ExpectedCurrency, long ObservedAmountPaise,
    string ObservedCurrency, string ProviderId, string ReceivingAccountId, string Environment,
    string ConfigurationVersion, DateTimeOffset ObservedAt, DateTimeOffset? CapturedAt, DateTimeOffset? CreatedAt,
    Guid? InboxId, string? Disposition);

public interface IPaymentReviewStore
{
    Task RecordCaptureAsync(StoredPaymentAttempt attempt, PaymentObservation payment, string classification,
        DateTimeOffset observedAt, Guid? inboxId, CancellationToken ct);
    Task RecordManualAsync(Guid account, Guid order, ManualPaymentReceipt receipt, CancellationToken ct);
    Task<bool> HasCompetingFundsAsync(Guid account, Guid order, Guid? attempt, Guid? receipt, Guid? review, CancellationToken ct);
    Task<ErrorOr<IReadOnlyList<UnmatchedPayment>>> ListAsync(AdminSessionCaller caller, Guid account, Guid? order, Guid? after, CancellationToken ct);
}

internal static class UnmatchedPaymentClassification
{
    internal static string? Discrepancy(StoredPaymentAttempt attempt, PaymentObservation payment, string? reference) =>
        payment.Pin != attempt.Pin || payment.ProviderOrderReference != reference ? "ReceivingPinMismatch" :
        payment.Currency != attempt.Currency ? "CurrencyMismatch" :
        payment.AmountPaise < attempt.AmountPaise ? "Underpayment" :
        payment.AmountPaise > attempt.AmountPaise ? "Overpayment" : null;

    internal static bool HasCaptureEvidence(PaymentObservation payment) =>
        payment.State == ObservedPaymentState.Captured && payment.AmountPaise > 0 &&
        payment.ProviderPaymentReference is { Length: > 0 and <= 120 } && payment.Currency.Length == 3 &&
        (payment.CapturedAt ?? payment.CreatedAt) is not null;
}
