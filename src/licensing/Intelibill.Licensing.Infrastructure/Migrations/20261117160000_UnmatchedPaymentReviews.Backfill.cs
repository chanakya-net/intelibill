namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class UnmatchedPaymentReviews
{
    private const string Backfill = """
        ALTER TABLE public.verified_payments NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_events NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_attempt_sessions NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.manual_payment_receipts NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_fulfilments NO FORCE ROW LEVEL SECURITY;

        INSERT INTO public.unmatched_payments (case_id, account_id, order_id, attempt_id, classification,
            expected_amount_paise, expected_currency, observed_amount_paise, observed_currency, provider_id,
            receiving_account_id, environment, configuration_version, provider_order_reference, provider_payment_reference,
            observed_at, captured_at)
        SELECT gen_random_uuid(), v.account_id, v.order_id, v.attempt_id, 'LegacyNeedsReview',
            v.amount_paise, v.currency, v.amount_paise, v.currency, v.provider_id, v.provider_account_id,
            v.environment, v.configuration_version, s.provider_order_reference, v.provider_payment_reference,
            v.verified_at, CASE WHEN v.capture_time_basis = 'ProviderCapture' THEN v.captured_at END
        FROM public.verified_payments v JOIN public.payment_attempt_sessions s USING (account_id, attempt_id)
        WHERE EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = v.attempt_id AND e.kind = 'PaidNeedsReview')
            AND NOT EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.attempt_id = v.attempt_id);

        INSERT INTO public.unmatched_payments (case_id, account_id, order_id, manual_receipt_id, classification,
            expected_amount_paise, expected_currency, observed_amount_paise, observed_currency, provider_id,
            receiving_account_id, environment, configuration_version, observed_at, captured_at)
        SELECT gen_random_uuid(), r.account_id, r.order_id, r.receipt_id, r.review_reason,
            r.amount_paise, r.currency, r.amount_paise, r.currency, 'manual', r.receiving_account_id,
            r.environment, r.configuration_version, r.verified_at, r.received_at
        FROM public.manual_payment_receipts r WHERE r.review_reason IS NOT NULL
            AND NOT EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.manual_receipt_id = r.receipt_id);

        ALTER TABLE public.verified_payments FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_events FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_attempt_sessions FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.manual_payment_receipts FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_fulfilments FORCE ROW LEVEL SECURITY;
        """;
}
