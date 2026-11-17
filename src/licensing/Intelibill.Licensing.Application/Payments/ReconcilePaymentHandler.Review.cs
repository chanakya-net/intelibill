using Intelibill.Licensing.Application.BillingAccounts;

namespace Intelibill.Licensing.Application.Payments;

public sealed partial class ReconcilePaymentHandler
{
    private async Task<bool> RecordReviewCapturesAsync(StoredPaymentAttempt attempt, RecoveryObservation observation,
        string? existing, Guid? inbox, CancellationToken ct)
    {
        var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
        var all = observation.Payment is { } direct ? observation.Captures.Append(direct) : observation.Captures;
        var captures = all.Where(payment => UnmatchedPaymentClassification.HasCaptureEvidence(payment) &&
                !(payment.CapturedAt > now || payment.CreatedAt > now))
            .DistinctBy(payment => (payment.Pin, payment.ProviderPaymentReference)).ToArray();
        var ambiguous = captures.Length > 1;
        var retained = false;
        foreach (var payment in captures.Where(payment => payment.ProviderPaymentReference != existing))
        {
            var classification = UnmatchedPaymentClassification.Discrepancy(attempt, payment, attempt.ProviderOrderReference);
            if (classification is null && (ambiguous || existing is not null))
                classification = "DuplicateFunds";
            if (classification is null)
                continue;
            await reviews.RecordCaptureAsync(attempt, payment, classification, now, inbox, ct);
            retained = true;
        }
        return retained;
    }
}
