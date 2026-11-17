namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidFreshStarts
{
    private const string FreshCompleteness = """
        CREATE FUNCTION public.complete_fresh_start(p public.payment_fulfilments) RETURNS boolean
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT CASE WHEN f.order_id IS NULL THEN h.order_id IS NULL AND q.snapshot->>'startRule' = 'PriorPaidThrough'
                ELSE f.term_id = p.term_id AND f.ready_at = p.fulfilled_at
                    AND f.effective_at = coalesce(v.captured_at, r.received_at)
                    AND (h.order_id IS NULL OR (h.attempt_id IS NOT DISTINCT FROM p.attempt_id
                        AND h.manual_receipt_id IS NOT DISTINCT FROM p.manual_receipt_id
                        AND f.ready_at >= h.held_at AND (h.restriction IS NULL OR h.restriction = 'Unrestricted'
                            OR h.restriction IN ('Trading', 'Security')
                                AND coalesce(f.released_revision >= h.account_revision, false))))
                    AND t.period_ordinal = 0 AND t.billing_period = q.snapshot->>'period'
                    AND c.starts_at = (CASE WHEN q.snapshot->'freshStart' IS NULL
                        THEN date_trunc('second', f.ready_at)
                        ELSE date_trunc('second', f.ready_at) + CASE WHEN date_trunc('second', f.ready_at) = f.ready_at
                            THEN interval '0 seconds' ELSE interval '1 second' END END)
                    AND t.anchor_local = c.starts_at AT TIME ZONE 'Asia/Kolkata'
                    AND c.ends_at = (t.anchor_local + CASE WHEN t.billing_period = 'Month'
                        THEN interval '1 month' ELSE interval '1 year' END) AT TIME ZONE 'Asia/Kolkata'
                    AND (CASE f.kind WHEN 'RestoredAccess' THEN q.snapshot->>'startRule' = 'RestoredAccess'
                        AND p.predecessor_term_id IS NOT NULL AND p.payment_effective_at = f.effective_at
                        WHEN 'TrialConversion' THEN q.snapshot->>'startRule' = 'Fulfilment' AND p.predecessor_term_id IS NULL
                            AND (q.snapshot->'freshStart'->>'trialShopCount')::int > 0
                        ELSE q.snapshot->>'startRule' = 'Fulfilment' AND p.predecessor_term_id IS NULL
                            AND coalesce((q.snapshot->'freshStart'->>'trialShopCount')::int, 0) = 0 END)
                    AND coalesce((q.snapshot->'freshStart'->>'trialShopCount')::int, 0) =
                        (SELECT count(*) FROM public.payment_trial_conversions x
                        WHERE x.account_id = p.account_id AND x.order_id = p.order_id AND x.paid_starts_at = c.starts_at)
                END
            FROM public.checkout_orders q
            JOIN public.subscription_term_facts t ON t.account_id = p.account_id AND t.term_id = p.term_id
            JOIN public.subscription_coverage_terms c ON c.account_id = p.account_id AND c.term_id = p.term_id
            LEFT JOIN public.paid_fresh_starts f ON f.account_id = q.account_id AND f.order_id = q.order_id
            LEFT JOIN public.payment_restoration_holds h ON h.account_id = q.account_id AND h.order_id = q.order_id
            LEFT JOIN public.verified_payments v ON v.attempt_id = p.attempt_id
            LEFT JOIN public.manual_payment_receipts r ON r.receipt_id = p.manual_receipt_id
            WHERE q.account_id = p.account_id AND q.order_id = p.order_id
        $$;
        """;
}
