namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaymentConfirmations
{
    // One versioned snapshot expression serves issuance and migration. INVOKER preserves forced RLS.
    // It copies accepted order names and terms, never live merchant details or inferred legal identities.
    private const string SnapshotFunction = """
        CREATE FUNCTION public.payment_confirmation_snapshot(cid uuid, account uuid, ordering uuid, attempt uuid,
            receipt uuid, issued timestamptz, basis text) RETURNS jsonb LANGUAGE sql STABLE
            SET search_path = pg_catalog AS $$
        SELECT jsonb_build_object(
            'policy', 'payment-confirmation/1',
            'receipt', jsonb_build_object('confirmationId', cid, 'accountId', account, 'orderId', ordering,
                'source', p.source, 'amountPaise', p.amount_paise, 'currency', p.currency,
                'verifiedAt', to_char(p.verified_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'issuedAt', to_char(issued AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'issuanceBasis', basis),
            'party', jsonb_build_object('accountNameAtIssuance', b.name, 'acceptedByMerchantUserId', a.actor_id),
            'terms', jsonb_build_object(
                'planVersionId', q.snapshot->'planVersionId', 'planVersion', q.snapshot->'planVersion',
                'planName', q.snapshot->'planName', 'period', q.snapshot->'period', 'capacity', q.snapshot->'capacity',
                'membersPerShop', q.snapshot->'membersPerShop', 'shopCount', q.shop_count,
                'customerTermsVersion', q.snapshot->'customerTermsVersion', 'refundPolicyVersion', q.snapshot->'refundPolicyVersion',
                'billingTimeZone', q.snapshot->'billingTimeZone', 'calendarVersion', q.snapshot->'calendarVersion',
                'renewalPolicyVersion', q.snapshot->'renewalPolicyVersion', 'startRule', q.snapshot->'startRule',
                'acceptedAt', to_char(a.accepted_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'quoteRevision', q.quote_revision, 'snapshotHash', q.snapshot_hash,
                'shops', (SELECT jsonb_agg(jsonb_build_object('shopId', s.shop_id, 'name', s.shop_name, 'termId', q.term_id)
                    ORDER BY s.shop_id) FROM public.checkout_order_shops s WHERE s.account_id = account AND s.order_id = ordering)),
            'receiptReference', coalesce(attempt, receipt), 'providerId', p.provider_id,
            'providerAccountId', p.provider_account_id, 'environment', p.environment, 'configurationVersion', p.configuration_version,
            'providerOrderReference', p.order_reference, 'providerPaymentReference', p.payment_reference,
            'effectiveAt', to_char(p.effective_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'effectiveTimeBasis', p.time_basis,
            'fulfilmentAtIssuance', CASE WHEN EXISTS (SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = account AND f.order_id = ordering AND
                    (f.attempt_id = attempt OR f.manual_receipt_id = receipt)) THEN 'Fulfilled' ELSE 'NeedsReview' END)
        FROM public.checkout_orders q JOIN public.billing_accounts b ON b.id = q.account_id
        JOIN public.checkout_order_acceptances a USING (account_id, order_id)
        JOIN LATERAL (
            SELECT 'Online'::text AS source, v.amount_paise, v.currency, v.verified_at, v.provider_id,
                v.provider_account_id, v.environment, v.configuration_version,
                s.provider_order_reference AS order_reference, v.provider_payment_reference AS payment_reference,
                v.captured_at AS effective_at, v.capture_time_basis AS time_basis
            FROM public.verified_payments v JOIN public.payment_attempt_sessions s USING (attempt_id, account_id)
            WHERE v.account_id = account AND v.order_id = ordering AND v.attempt_id = attempt AND receipt IS NULL
            UNION ALL
            SELECT 'Manual', m.amount_paise, m.currency, m.verified_at, 'manual', m.receiving_account_id,
                m.environment, m.configuration_version, NULL, NULL, m.received_at, 'VerifiedManualEvidence'
            FROM public.manual_payment_receipts m WHERE m.account_id = account AND m.order_id = ordering
                AND m.receipt_id = receipt AND attempt IS NULL
        ) p ON true
        WHERE q.account_id = account AND q.order_id = ordering AND (basis = 'Retained' OR issued >= p.verified_at);
        $$;
        """;
}
