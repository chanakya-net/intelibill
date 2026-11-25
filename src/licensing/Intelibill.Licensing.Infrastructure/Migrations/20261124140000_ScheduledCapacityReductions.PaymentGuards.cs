namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class ScheduledCapacityReductions
{
    private const string ManualReductionReason = """
        OR r.review_reason = 'ReductionPending' AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
            WHERE s.account_id = r.account_id AND s.order_id = r.order_id AND s.receipt_id = r.receipt_id
                AND s.state = 'NeedsReview' AND s.reason = NEW.classification)
        """;

    private const string Confirmation = """
        ALTER FUNCTION public.payment_confirmation_snapshot(uuid,uuid,uuid,uuid,uuid,timestamptz,text)
            RENAME TO payment_confirmation_before_reductions;
        CREATE FUNCTION public.payment_confirmation_snapshot(cid uuid,account uuid,ordering uuid,attempt uuid,receipt uuid,
            issued timestamptz,basis text) RETURNS jsonb LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT public.payment_confirmation_before_reductions(cid,account,ordering,attempt,receipt,issued,basis) ||
                CASE WHEN EXISTS (SELECT FROM public.scheduled_capacity_reductions s
                    WHERE s.account_id = account AND s.order_id = ordering
                        AND (s.attempt_id = attempt OR s.receipt_id = receipt)
                        AND NOT EXISTS (SELECT FROM public.payment_fulfilments f
                            WHERE f.account_id = account AND f.order_id = ordering))
                THEN jsonb_build_object('fulfilmentAtIssuance', 'Pending') ELSE '{}'::jsonb END
        $$;
        """;

    // Allow reduction renewal orders to name exactly the frozen retained subset of their scheduled reduction.
    internal const string CheckoutOrderGuard = """
        CREATE OR REPLACE FUNCTION public.guard_checkout_order() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        DECLARE reduction_id uuid;
        BEGIN
            reduction_id := (NEW.snapshot->'reduction'->>'scheduleId')::uuid;
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash
            FROM public.checkout_order_shops s JOIN public.shop_affiliations a
                ON a.request_id = s.affiliation_request_id
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id AND s.replay_id = NEW.replay_id
                AND a.applied_at <= NEW.created_at
                AND (a.term_id IS NOT DISTINCT FROM NEW.term_id OR (NEW.term_id IS NOT NULL AND EXISTS (
                    SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = s.account_id
                        AND p.term_id = NEW.term_id AND p.shop_id = s.shop_id
                        AND p.affiliation_request_id = s.affiliation_request_id)));
            IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid
                OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
                OR (SELECT count(*) FROM public.checkout_order_shops s
                    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id) <> named
                OR (NEW.term_id IS NOT NULL AND reduction_id IS NULL AND (SELECT count(*) FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) + (SELECT count(*)
                    FROM public.payment_fulfilment_shops p WHERE p.account_id = NEW.account_id AND p.term_id = NEW.term_id) <> named)
                OR (reduction_id IS NOT NULL AND (NEW.term_id IS NULL OR NOT EXISTS (
                    SELECT FROM public.scheduled_capacity_reductions r
                    WHERE r.account_id = NEW.account_id AND r.schedule_id = reduction_id AND r.term_id = NEW.term_id
                        AND r.state = 'Scheduled' AND r.amount_paise = NEW.amount_paise
                        AND r.retained_shop_count = NEW.shop_count AND r.retained_shop_hash = NEW.shop_hash
                        AND r.snapshot_hash = NEW.snapshot->'reduction'->>'scheduleHash'
                        AND (SELECT count(*) FROM public.checkout_order_shops os
                            JOIN public.scheduled_capacity_reduction_shops rs
                                ON rs.account_id = os.account_id AND rs.schedule_id = r.schedule_id
                                AND rs.shop_id = os.shop_id AND rs.affiliation_request_id = os.affiliation_request_id
                                AND rs.retained
                            WHERE os.account_id = NEW.account_id AND os.order_id = NEW.order_id) = NEW.shop_count)))
                OR (NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.payment_fulfilments f
                    WHERE f.account_id = NEW.account_id AND f.predecessor_term_id = NEW.term_id))
                OR (NEW.snapshot->>'accountId')::uuid IS DISTINCT FROM NEW.account_id
                OR (NEW.snapshot->>'orderId')::uuid IS DISTINCT FROM NEW.order_id
                OR (NEW.snapshot->>'termId')::uuid IS DISTINCT FROM NEW.term_id
                OR (NEW.snapshot->>'amountPaise')::bigint IS DISTINCT FROM NEW.amount_paise
                OR (NEW.snapshot->>'revision')::int IS DISTINCT FROM NEW.quote_revision
                OR coalesce((NEW.snapshot->>'capacity')::int, 0) NOT BETWEEN NEW.shop_count AND 100000
                OR coalesce((NEW.snapshot->>'membersPerShop')::int, 0) NOT BETWEEN 1 AND 100000
                OR (NEW.snapshot->>'shopCount')::int IS DISTINCT FROM NEW.shop_count
                OR NEW.snapshot->>'shopHash' IS DISTINCT FROM NEW.shop_hash
                OR NEW.snapshot->>'snapshotHash' IS DISTINCT FROM NEW.snapshot_hash
                OR NEW.snapshot->>'currency' IS DISTINCT FROM 'INR'
                OR NEW.snapshot->>'priceBasis' IS DISTINCT FROM 'TaxInclusiveFinal'
                OR NEW.snapshot->>'acceptance' IS NOT NULL
                OR (NEW.snapshot->>'createdAt')::timestamptz IS DISTINCT FROM NEW.created_at
                OR (NEW.snapshot->>'expiresAt')::timestamptz IS DISTINCT FROM NEW.expires_at THEN
                RAISE EXCEPTION 'Checkout quote must contain complete coherent evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    private const string RestoreCheckoutOrderGuard = """
        CREATE OR REPLACE FUNCTION public.guard_checkout_order() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash
            FROM public.checkout_order_shops s JOIN public.shop_affiliations a
                ON a.request_id = s.affiliation_request_id
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id AND s.replay_id = NEW.replay_id
                AND a.applied_at <= NEW.created_at
                AND (a.term_id IS NOT DISTINCT FROM NEW.term_id OR (NEW.term_id IS NOT NULL AND EXISTS (
                    SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = s.account_id
                        AND p.term_id = NEW.term_id AND p.shop_id = s.shop_id
                        AND p.affiliation_request_id = s.affiliation_request_id)));
            IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid
                OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
                OR (SELECT count(*) FROM public.checkout_order_shops s
                    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id) <> named
                OR (NEW.term_id IS NOT NULL AND (SELECT count(*) FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) + (SELECT count(*)
                    FROM public.payment_fulfilment_shops p WHERE p.account_id = NEW.account_id AND p.term_id = NEW.term_id) <> named)
                OR (NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.payment_fulfilments f
                    WHERE f.account_id = NEW.account_id AND f.predecessor_term_id = NEW.term_id))
                OR (NEW.snapshot->>'accountId')::uuid IS DISTINCT FROM NEW.account_id
                OR (NEW.snapshot->>'orderId')::uuid IS DISTINCT FROM NEW.order_id
                OR (NEW.snapshot->>'termId')::uuid IS DISTINCT FROM NEW.term_id
                OR (NEW.snapshot->>'amountPaise')::bigint IS DISTINCT FROM NEW.amount_paise
                OR (NEW.snapshot->>'revision')::int IS DISTINCT FROM NEW.quote_revision
                OR coalesce((NEW.snapshot->>'capacity')::int, 0) NOT BETWEEN NEW.shop_count AND 100000
                OR coalesce((NEW.snapshot->>'membersPerShop')::int, 0) NOT BETWEEN 1 AND 100000
                OR (NEW.snapshot->>'shopCount')::int IS DISTINCT FROM NEW.shop_count
                OR NEW.snapshot->>'shopHash' IS DISTINCT FROM NEW.shop_hash
                OR NEW.snapshot->>'snapshotHash' IS DISTINCT FROM NEW.snapshot_hash
                OR NEW.snapshot->>'currency' IS DISTINCT FROM 'INR'
                OR NEW.snapshot->>'priceBasis' IS DISTINCT FROM 'TaxInclusiveFinal'
                OR NEW.snapshot->>'acceptance' IS NOT NULL
                OR (NEW.snapshot->>'createdAt')::timestamptz IS DISTINCT FROM NEW.created_at
                OR (NEW.snapshot->>'expiresAt')::timestamptz IS DISTINCT FROM NEW.expires_at THEN
                RAISE EXCEPTION 'Checkout quote must contain complete coherent evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    private const string PaymentGuards = """
        CREATE OR REPLACE FUNCTION public.guard_payment_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                        OR (q.term_id IS NOT NULL AND (
                            (q.snapshot->>'startRule' = 'PriorPaidThrough' AND (q.snapshot->'reduction' IS NULL
                                OR EXISTS (SELECT FROM public.scheduled_capacity_reductions r
                                    WHERE r.account_id = q.account_id AND r.term_id = q.term_id
                                        AND r.schedule_id = (q.snapshot->'reduction'->>'scheduleId')::uuid
                                        AND r.snapshot_hash = q.snapshot->'reduction'->>'scheduleHash'
                                        AND r.amount_paise = q.amount_paise AND r.state = 'Scheduled')))
                            OR q.snapshot->>'startRule' = 'CapacityIncrease' AND EXISTS (SELECT FROM public.capacity_change_quotes z
                                WHERE z.account_id=q.account_id AND z.quote_id=q.order_id AND z.term_id=q.term_id
                                    AND z.snapshot_hash=q.snapshot->'increase'->>'quoteHash' AND z.amount_paise=q.amount_paise
                                    AND z.outcome='Payable' AND NEW.created_at < z.paid_through
                                    AND (NEW.provider_id, NEW.provider_account_id, NEW.environment, NEW.configuration_version) =
                                    (z.provider_id, z.provider_account_id, z.provider_environment, z.provider_configuration_version)
                                    AND coalesce(NEW.selection_revision,0)=z.routing_revision)
                            OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.snapshot_hash = NEW.snapshot_hash AND NEW.created_at < q.expires_at) THEN
                RAISE EXCEPTION 'Payment attempt must match its accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;

        CREATE OR REPLACE FUNCTION public.guard_manual_payment_receipt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q JOIN public.checkout_order_acceptances a USING (account_id, order_id)
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                        OR (q.term_id IS NOT NULL AND (
                            (q.snapshot->>'startRule' = 'PriorPaidThrough' AND (q.snapshot->'reduction' IS NULL
                                OR EXISTS (SELECT FROM public.scheduled_capacity_reductions r
                                    WHERE r.account_id = q.account_id AND r.term_id = q.term_id
                                        AND r.schedule_id = (q.snapshot->'reduction'->>'scheduleId')::uuid
                                        AND r.snapshot_hash = q.snapshot->'reduction'->>'scheduleHash'
                                        AND r.amount_paise = q.amount_paise)))
                            OR q.snapshot->>'startRule' = 'CapacityIncrease' AND EXISTS (SELECT FROM public.capacity_change_quotes z
                                WHERE z.account_id=q.account_id AND z.quote_id=q.order_id AND z.term_id=q.term_id
                                    AND z.snapshot_hash=q.snapshot->'increase'->>'quoteHash' AND z.amount_paise=q.amount_paise
                                    AND z.outcome='Payable')
                            OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND a.accepted_at < q.expires_at) THEN
                RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    internal static string FulfilmentCompleteness(bool refundBoundaries = false, bool appliedOnly = false)
    {
        const string originalEnd = "pc.ends_at + interval '168 hours'";
        var decisions = appliedOnly ? "public.applied_refund_decisions" : "public.refund_access_decisions";
        var restorationEnd = refundBoundaries
            ? $"coalesce((SELECT d.new_access_ends_at FROM {decisions} d WHERE d.account_id = pc.account_id AND d.term_id = pc.term_id AND d.term_revision IS NOT NULL ORDER BY d.term_revision DESC LIMIT 1), {originalEnd})"
            : originalEnd;
        const string appliedPrior = """
            (SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = pc.account_id
                AND i.term_id = pc.term_id AND i.state = 'Applied' ORDER BY i.target_revision DESC LIMIT 1)
            """;
        return $$"""
            CREATE OR REPLACE FUNCTION public.require_complete_payment_fulfilment() RETURNS trigger LANGUAGE plpgsql
            SET search_path = pg_catalog AS $$
            DECLARE fresh_kind text;
            DECLARE applied_reduction uuid;
            BEGIN
                SELECT kind INTO fresh_kind FROM public.paid_fresh_starts f
                WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id;
                SELECT r.schedule_id INTO applied_reduction FROM public.scheduled_capacity_reductions r
                WHERE r.account_id = NEW.account_id AND r.order_id = NEW.order_id AND r.term_id = NEW.predecessor_term_id
                    AND r.successor_term_id = NEW.term_id AND r.state = 'Applied' AND r.consume_event IS NOT NULL
                    AND r.attempt_id IS NOT DISTINCT FROM NEW.attempt_id
                    AND r.receipt_id IS NOT DISTINCT FROM NEW.manual_receipt_id
                    AND EXISTS (SELECT FROM public.entitlement_merchant_inbox e
                        WHERE e.account_id = r.account_id AND e.event_id = r.consume_event AND e.kind = 'ReductionReceipt');
                IF NOT EXISTS (SELECT FROM public.checkout_orders q
                        JOIN public.subscription_term_facts t ON t.account_id = q.account_id AND t.term_id = NEW.term_id
                        JOIN public.subscription_coverage_terms c ON c.account_id = q.account_id AND c.term_id = NEW.term_id
                        WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND t.basis = 'Paid'
                            AND t.price_paise = q.amount_paise AND c.capacity = (q.snapshot->>'capacity')::int
                            AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                                WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = q.shop_count)
                    OR ((NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_events e
                        WHERE e.attempt_id = NEW.attempt_id AND e.account_id = NEW.account_id
                            AND e.order_id = NEW.order_id AND e.kind = 'OrderFulfilled'))
                        OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
                            WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id
                                AND r.order_id = NEW.order_id AND (r.term_id = NEW.term_id AND r.review_reason IS NULL
                                    OR r.review_reason = 'ReductionPending' AND applied_reduction IS NOT NULL
                                    OR r.review_reason = 'RestorationPending' AND EXISTS (SELECT FROM public.payment_restoration_holds h
                                        WHERE h.account_id = r.account_id AND h.order_id = r.order_id AND h.manual_receipt_id = r.receipt_id)
                                    OR r.review_reason = 'CoverageChanged' AND EXISTS (SELECT FROM public.unmatched_payments c
                                        JOIN public.payment_review_decisions d USING (account_id, case_id)
                                        WHERE c.account_id = r.account_id AND c.order_id = r.order_id AND c.manual_receipt_id = r.receipt_id
                                            AND c.classification = 'CoverageChanged' AND c.observed_amount_paise = r.amount_paise
                                            AND c.observed_currency = r.currency AND c.captured_at = r.received_at
                                            AND d.action = 'RecoverAcceptedTerms' AND d.term_id = NEW.term_id)))))
                    OR EXISTS (SELECT FROM public.unmatched_payments c
                        JOIN public.verified_payments v ON v.attempt_id = c.attempt_id
                            AND v.provider_payment_reference = c.provider_payment_reference AND v.account_id = c.account_id
                        WHERE c.account_id = NEW.account_id AND c.order_id = NEW.order_id AND c.attempt_id = NEW.attempt_id
                            AND c.classification IN ('CoverageChanged', 'LegacyNeedsReview')
                            AND NOT EXISTS (SELECT FROM public.payment_restoration_holds h
                                WHERE h.account_id = NEW.account_id AND h.order_id = NEW.order_id AND h.attempt_id = NEW.attempt_id)
                            AND NOT EXISTS (SELECT FROM public.payment_review_decisions d WHERE d.account_id = c.account_id
                                AND d.case_id = c.case_id AND d.action = 'RecoverAcceptedTerms' AND d.term_id = NEW.term_id))
                    OR EXISTS (SELECT FROM public.unmatched_payments c JOIN public.payment_review_decisions d USING (account_id, case_id)
                        WHERE c.account_id = NEW.account_id AND c.order_id = NEW.order_id
                            AND (c.attempt_id = NEW.attempt_id OR c.manual_receipt_id = NEW.manual_receipt_id)
                            AND c.classification IN ('RestorationPending', 'LegacyNeedsReview')
                            AND d.action IN ('RecordNoAccessDecision', 'ReferForRefundReview'))
                    OR NOT public.complete_fresh_start(NEW)
                    OR (NEW.predecessor_term_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.subscription_term_facts pf
                        JOIN public.subscription_coverage_terms pc ON pc.account_id = pf.account_id AND pc.term_id = pf.term_id
                        JOIN public.subscription_term_facts sf ON sf.account_id = pf.account_id AND sf.term_id = NEW.term_id
                        JOIN public.subscription_coverage_terms sc ON sc.account_id = pf.account_id AND sc.term_id = NEW.term_id
                        WHERE pf.account_id = NEW.account_id AND pf.term_id = NEW.predecessor_term_id AND pf.basis = 'Paid'
                            AND sf.time_zone = pf.time_zone AND sf.billing_period = pf.billing_period
                            AND sf.calendar_version = pf.calendar_version
                            AND (CASE WHEN fresh_kind = 'RestoredAccess' THEN sf.period_ordinal = 0
                                AND sc.starts_at >= {{restorationEnd}}
                                AND NEW.payment_effective_at >= {{restorationEnd}}
                                AND NEW.grace_days_included = 0
                                ELSE sf.anchor_local = pf.anchor_local AND sf.period_ordinal = pf.period_ordinal + 1
                                    AND sc.starts_at = pc.ends_at END)
                            AND (CASE WHEN applied_reduction IS NOT NULL THEN EXISTS (
                                    SELECT FROM public.scheduled_capacity_reductions r
                                    WHERE r.account_id = NEW.account_id AND r.schedule_id = applied_reduction
                                        AND sc.capacity = r.target_capacity AND sc.mode = r.target_mode
                                        AND sf.member_allowance = r.target_member_allowance
                                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = r.retained_shop_count
                                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                                            JOIN public.scheduled_capacity_reduction_shops rs
                                                ON rs.account_id = l.account_id AND rs.schedule_id = r.schedule_id
                                                AND rs.shop_id = l.shop_id AND rs.affiliation_request_id = l.affiliation_request_id
                                                AND rs.retained
                                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = r.retained_shop_count)
                                ELSE sc.capacity = coalesce({{appliedPrior}}, pc.capacity)
                                    AND sc.mode = CASE WHEN {{appliedPrior}} > 1 THEN 'Grouped' ELSE pc.mode END
                                    AND NOT EXISTS (SELECT FROM public.scheduled_capacity_reductions r
                                        WHERE r.account_id = NEW.account_id AND r.term_id = NEW.predecessor_term_id
                                            AND (r.state IN ('Funded', 'Applying', 'NeedsResolution')
                                                OR (r.state = 'NeedsReview' AND (fresh_kind IS DISTINCT FROM 'RestoredAccess'
                                                    OR NOT public.reduction_review_settled(r.account_id, r.schedule_id)))))
                                    AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                                        WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id)
                                        = (SELECT count(*) FROM public.shop_affiliations a
                                            WHERE a.account_id = NEW.account_id AND a.term_id = NEW.predecessor_term_id)
                                        + (SELECT count(*) FROM public.payment_fulfilment_shops p
                                            WHERE p.account_id = NEW.account_id AND p.term_id = NEW.predecessor_term_id)
                                END))) THEN
                    RAISE EXCEPTION 'Payment fulfilment evidence is incomplete' USING ERRCODE = '23514';
                END IF;
                RETURN NULL;
            END $$;
            """;
    }

    private static string RestoreFulfilmentCompleteness()
    {
        const string predecessor = """
            (SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = pc.account_id
                AND i.term_id = pc.term_id AND i.state = 'Applied' ORDER BY i.target_revision DESC LIMIT 1)
            """;
        return UnmatchedPaymentReviews.FulfilmentCompleteness(true, $"coalesce({predecessor},pc.capacity)",
            $"CASE WHEN {predecessor}>1 THEN 'Grouped' ELSE pc.mode END");
    }
}
