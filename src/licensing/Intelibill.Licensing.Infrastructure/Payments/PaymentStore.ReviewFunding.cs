using Intelibill.Licensing.Application.Payments;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    public Task<PaymentReviewReferences> FindReferencesAsync(Guid account, Guid id, CancellationToken ct) =>
        db.Database.SqlQuery<PaymentReviewReferences>($"""
            SELECT s.provider_order_reference AS expected_order, c.provider_order_reference AS observed_order,
                c.provider_payment_reference AS observed_payment,
                v.amount_paise AS verified_amount_paise, v.currency AS verified_currency, v.verified_at
            FROM public.unmatched_payments c LEFT JOIN public.payment_attempt_sessions s
                ON s.account_id = c.account_id AND s.attempt_id = c.attempt_id
            LEFT JOIN public.verified_payments v
                ON v.account_id = c.account_id AND v.order_id = c.order_id AND v.attempt_id = c.attempt_id
                AND v.provider_id = c.provider_id AND v.provider_account_id = c.receiving_account_id
                AND v.environment = c.environment AND v.provider_payment_reference = c.provider_payment_reference
            WHERE c.account_id = {account} AND c.case_id = {id}
            """).SingleAsync(ct);

    public Task<ReviewFundingTime?> FindFundingAsync(Guid account, UnmatchedPayment review, CancellationToken ct) =>
        db.Database.SqlQuery<ReviewFundingTime>($"""
            SELECT CASE WHEN v.capture_time_basis = 'ProviderCapture' THEN v.captured_at ELSE c.created_at END AS earliest_capture_at,
                v.captured_at AS latest_possible_capture_at
            FROM public.unmatched_payments c JOIN public.verified_payments v
                ON v.account_id = c.account_id AND v.order_id = c.order_id AND v.attempt_id = c.attempt_id
                AND v.provider_payment_reference = c.provider_payment_reference AND v.provider_id = c.provider_id
                AND v.provider_account_id = c.receiving_account_id AND v.environment = c.environment
                AND v.configuration_version = c.configuration_version
                AND v.amount_paise = c.observed_amount_paise AND v.currency = c.observed_currency
                AND c.observed_at = GREATEST(v.verified_at, c.captured_at, c.created_at)
            WHERE c.account_id = {account} AND c.case_id = {review.CaseId}
                AND (v.capture_time_basis = 'ProviderCapture' AND v.captured_at = c.captured_at OR
                    v.capture_time_basis = 'ObservedAtVerification' AND c.captured_at IS NULL AND c.created_at <= v.captured_at)
            UNION ALL
            SELECT r.received_at, r.received_at FROM public.unmatched_payments c JOIN public.manual_payment_receipts r
                ON r.account_id = c.account_id AND r.order_id = c.order_id AND r.receipt_id = c.manual_receipt_id
                AND r.amount_paise = c.observed_amount_paise AND r.currency = c.observed_currency
                AND r.received_at = c.captured_at AND r.verified_at = c.observed_at
            WHERE c.account_id = {account} AND c.case_id = {review.CaseId}
            """).SingleOrDefaultAsync(ct);

    public Task UseReviewOrderAsync(Guid order, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT set_config('app.manual_order_id', {order.ToString("D")}, true)", ct);

    public async Task UseReviewFundingAsync(UnmatchedPayment review, CancellationToken ct)
    {
        if (review.AttemptId is { } attempt)
            await UseAttemptAsync(attempt, ct);
        if (review.ManualReceiptId is { } receipt)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.manual_order_id', {review.OrderId.ToString("D")}, true),
                    set_config('app.manual_receipt_id', {receipt.ToString("D")}, true)
                """, ct);
    }
}
