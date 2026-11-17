using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    public Task RecordCaptureAsync(StoredPaymentAttempt attempt, PaymentObservation payment, string classification,
        DateTimeOffset observedAt, Guid? inboxId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.unmatched_payments (case_id, account_id, order_id, attempt_id, inbox_id, classification,
            expected_amount_paise, expected_currency, observed_amount_paise, observed_currency, provider_id,
            receiving_account_id, environment, configuration_version, provider_order_reference, provider_payment_reference,
            observed_at, captured_at, created_at)
        VALUES ({Guid.NewGuid()}, {attempt.AccountId}, {attempt.OrderId}, {attempt.AttemptId}, {inboxId}, {classification},
            {attempt.AmountPaise}, {attempt.Currency}, {payment.AmountPaise}, {payment.Currency}, {payment.Pin.ProviderId},
            {payment.Pin.ProviderAccountId}, {payment.Pin.Environment}, {payment.Pin.ConfigurationVersion},
            {payment.ProviderOrderReference}, {payment.ProviderPaymentReference}, {observedAt}, {payment.CapturedAt}, {payment.CreatedAt})
        ON CONFLICT DO NOTHING
        """, ct);

    public Task RecordManualAsync(Guid account, Guid order, ManualPaymentReceipt receipt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.unmatched_payments (case_id, account_id, order_id, manual_receipt_id, classification,
                expected_amount_paise, expected_currency, observed_amount_paise, observed_currency, provider_id,
                receiving_account_id, environment, configuration_version, observed_at, captured_at)
            VALUES ({Guid.NewGuid()}, {account}, {order}, {receipt.ReceiptId}, {receipt.ReviewReason}, {receipt.AmountPaise},
                {receipt.Currency}, {receipt.AmountPaise}, {receipt.Currency}, 'manual', {receipt.ReceivingAccountId},
                {receipt.Environment}, {receipt.ConfigurationVersion}, {receipt.VerifiedAt}, {receipt.ReceivedAt})
            ON CONFLICT DO NOTHING
            """, ct);

    public Task<bool> HasCompetingFundsAsync(Guid account, Guid order, Guid? attempt, Guid? receipt, Guid? review, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT (EXISTS (SELECT FROM public.verified_payments WHERE account_id = {account} AND order_id = {order}
                    AND attempt_id IS DISTINCT FROM {attempt})
                OR EXISTS (SELECT FROM public.manual_payment_receipts WHERE account_id = {account} AND order_id = {order}
                    AND receipt_id IS DISTINCT FROM {receipt})
                OR EXISTS (SELECT FROM public.unmatched_payments c WHERE c.account_id = {account} AND c.order_id = {order}
                    AND c.case_id IS DISTINCT FROM {review} AND c.classification <> 'ReceivingPinMismatch'
                    AND (c.manual_receipt_id IS NULL OR c.manual_receipt_id IS DISTINCT FROM {receipt})
                    AND NOT EXISTS (SELECT FROM public.verified_payments v WHERE v.attempt_id = {attempt}
                        AND v.account_id = c.account_id AND v.order_id = c.order_id
                        AND v.provider_id = c.provider_id AND v.provider_account_id = c.receiving_account_id
                        AND v.environment = c.environment AND v.provider_payment_reference = c.provider_payment_reference))
                OR EXISTS (SELECT FROM public.unmatched_payments c JOIN public.payment_review_decisions d USING (account_id, case_id)
                    WHERE c.account_id = {account} AND c.order_id = {order}
                        AND c.classification IN ('RestorationPending', 'LegacyNeedsReview')
                        AND (c.attempt_id = {attempt} OR c.manual_receipt_id = {receipt})
                        AND d.action IN ('RecordNoAccessDecision', 'ReferForRefundReview'))) AS "Value"
            """).SingleAsync(ct);

    public Task<ErrorOr<IReadOnlyList<UnmatchedPayment>>> ListAsync(AdminSessionCaller caller, Guid account, Guid? order,
        Guid? after, CancellationToken ct) =>
        InManualScopeAsync<IReadOnlyList<UnmatchedPayment>>(caller, account, Guid.Empty, true, false,
            async (_, token) => await ReviewCases(account, order, after).OrderBy(row => row.CaseId).Take(40).ToArrayAsync(token), ct);

    private IQueryable<UnmatchedPayment> ReviewCases(Guid account, Guid? order = null, Guid? after = null) =>
        db.Database.SqlQuery<UnmatchedPayment>($"""
            SELECT c.case_id, c.order_id, c.attempt_id, c.manual_receipt_id, c.classification, c.expected_amount_paise,
                c.expected_currency, c.observed_amount_paise, c.observed_currency, c.provider_id, c.receiving_account_id,
                c.environment, c.configuration_version, c.observed_at, c.captured_at, c.created_at, c.inbox_id, d.disposition
            FROM public.unmatched_payments c LEFT JOIN public.payment_review_decisions d USING (account_id, case_id)
            WHERE c.account_id = {account} AND ({order}::uuid IS NULL OR c.order_id = {order})
                AND ({after}::uuid IS NULL OR c.case_id > {after})
            """);
}
