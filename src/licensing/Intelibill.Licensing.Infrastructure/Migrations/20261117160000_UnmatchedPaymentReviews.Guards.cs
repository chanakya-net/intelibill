namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class UnmatchedPaymentReviews
{
    private const string LiveBillingSession = """
        EXISTS (SELECT FROM public.admin_sessions s JOIN public.admin_identities a ON a.id = s.admin_id
            WHERE s.id = payment_review_decisions.session_id AND a.id = payment_review_decisions.admin_id
                AND s.revoked_at IS NULL AND s.mfa_verified_at = payment_review_decisions.mfa_verified_at
                AND a.mfa_enrolled_at IS NOT NULL AND a.deactivated_at IS NULL AND (a.capabilities & 2) = 2)
        """;

    internal static string SourceGuard(string capacityReason = "") => $$"""
        CREATE OR REPLACE FUNCTION public.guard_unmatched_payment_source() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_attempts a
                WHERE a.account_id = NEW.account_id AND a.order_id = NEW.order_id AND a.attempt_id = NEW.attempt_id
                    AND a.amount_paise = NEW.expected_amount_paise AND a.currency = NEW.expected_currency) THEN
                RAISE EXCEPTION 'Review evidence must match the original attempt' USING ERRCODE = '23514';
            END IF;
            IF NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
                WHERE r.account_id = NEW.account_id AND r.order_id = NEW.order_id AND r.receipt_id = NEW.manual_receipt_id
                    AND (r.review_reason = NEW.classification {{capacityReason}}) AND r.amount_paise = NEW.expected_amount_paise
                    AND r.amount_paise = NEW.observed_amount_paise AND r.currency = NEW.expected_currency
                    AND r.currency = NEW.observed_currency AND r.received_at = NEW.captured_at
                    AND r.verified_at = NEW.observed_at AND r.receiving_account_id = NEW.receiving_account_id
                    AND r.environment = NEW.environment AND r.configuration_version = NEW.configuration_version
                    AND NEW.provider_id = 'manual') THEN
                RAISE EXCEPTION 'Review evidence must match the original manual receipt' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    private static string DecisionGuards => SourceGuard() + """
        CREATE TRIGGER guard_unmatched_payment_source BEFORE INSERT ON public.unmatched_payments
            FOR EACH ROW EXECUTE FUNCTION public.guard_unmatched_payment_source();

        CREATE FUNCTION public.require_payment_review_recovery() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.action = 'RecoverAcceptedTerms' AND NOT EXISTS (SELECT FROM public.unmatched_payments c
                JOIN public.checkout_orders q USING (account_id, order_id)
                JOIN public.payment_fulfilments f USING (account_id, order_id)
                WHERE c.account_id = NEW.account_id AND c.case_id = NEW.case_id AND f.term_id = NEW.term_id
                    AND q.snapshot->>'sourceHash' = NEW.accepted_source_hash
                    AND c.classification IN ('CoverageChanged', 'LegacyNeedsReview', 'RestorationPending')
                    AND c.attempt_id IS NOT DISTINCT FROM f.attempt_id
                    AND c.manual_receipt_id IS NOT DISTINCT FROM f.manual_receipt_id) THEN
                RAISE EXCEPTION 'Reviewed recovery must fulfil the selected original receipt' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER require_payment_review_recovery AFTER INSERT ON public.payment_review_decisions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_payment_review_recovery();
        """;

    private const string ReviewedManualFunds = """
        OR r.review_reason = 'CoverageChanged' AND EXISTS (SELECT FROM public.unmatched_payments c
            JOIN public.payment_review_decisions d USING (account_id, case_id)
            WHERE c.account_id = r.account_id AND c.order_id = r.order_id AND c.manual_receipt_id = r.receipt_id
                AND c.classification = 'CoverageChanged' AND c.observed_amount_paise = r.amount_paise
                AND c.observed_currency = r.currency AND c.captured_at = r.received_at
                AND d.action = 'RecoverAcceptedTerms' AND d.term_id = NEW.term_id)
        """;

    private const string MissingOnlineReview = """
        OR EXISTS (SELECT FROM public.unmatched_payments c
            JOIN public.verified_payments v ON v.attempt_id = c.attempt_id
                AND v.provider_payment_reference = c.provider_payment_reference AND v.account_id = c.account_id
            WHERE c.account_id = NEW.account_id AND c.order_id = NEW.order_id AND c.attempt_id = NEW.attempt_id
                AND c.classification IN ('CoverageChanged', 'LegacyNeedsReview')
                AND NOT EXISTS (SELECT FROM public.payment_restoration_holds h
                    WHERE h.account_id = NEW.account_id AND h.order_id = NEW.order_id AND h.attempt_id = NEW.attempt_id)
                AND NOT EXISTS (SELECT FROM public.payment_review_decisions d WHERE d.account_id = c.account_id
                    AND d.case_id = c.case_id AND d.action = 'RecoverAcceptedTerms' AND d.term_id = NEW.term_id))
        """;

    private const string OverriddenRestoration = """
        OR EXISTS (SELECT FROM public.unmatched_payments c JOIN public.payment_review_decisions d USING (account_id, case_id)
            WHERE c.account_id = NEW.account_id AND c.order_id = NEW.order_id
                AND (c.attempt_id = NEW.attempt_id OR c.manual_receipt_id = NEW.manual_receipt_id)
                AND c.classification IN ('RestorationPending', 'LegacyNeedsReview')
                AND d.action IN ('RecordNoAccessDecision', 'ReferForRefundReview'))
        """;

    private static string MissingAudit(bool reviewedRecovery) => $$"""
        (NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_events e
            WHERE e.attempt_id = NEW.attempt_id AND e.account_id = NEW.account_id
                AND e.order_id = NEW.order_id AND e.kind = 'OrderFulfilled'))
        OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
            WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id
                AND r.order_id = NEW.order_id AND (r.term_id = NEW.term_id AND r.review_reason IS NULL
                    OR r.review_reason = 'RestorationPending' AND EXISTS (SELECT FROM public.payment_restoration_holds h
                        WHERE h.account_id = r.account_id AND h.order_id = r.order_id AND h.manual_receipt_id = r.receipt_id)
                    {{(reviewedRecovery ? ReviewedManualFunds : "")}})))
        """;

    // A renewal successor is the predecessor's next period on the same anchor, same capacity, whole package.
    internal static string FulfilmentCompleteness(bool reviewedRecovery, string priorCapacity = "pc.capacity", string priorMode = "pc.mode") => $"""
        CREATE OR REPLACE FUNCTION public.require_complete_payment_fulfilment() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        DECLARE fresh_kind text;
        BEGIN
            SELECT kind INTO fresh_kind FROM public.paid_fresh_starts f
            WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id;
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                    JOIN public.subscription_term_facts t ON t.account_id = q.account_id AND t.term_id = NEW.term_id
                    JOIN public.subscription_coverage_terms c ON c.account_id = q.account_id AND c.term_id = NEW.term_id
                    WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND t.basis = 'Paid'
                        AND t.price_paise = q.amount_paise AND c.capacity = (q.snapshot->>'capacity')::int
                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = q.shop_count)
                OR ({MissingAudit(reviewedRecovery)})
                {(reviewedRecovery ? MissingOnlineReview : "")}
                {(reviewedRecovery ? OverriddenRestoration : "")}
                OR NOT public.complete_fresh_start(NEW)
                OR (NEW.predecessor_term_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.subscription_term_facts pf
                    JOIN public.subscription_coverage_terms pc ON pc.account_id = pf.account_id AND pc.term_id = pf.term_id
                    JOIN public.subscription_term_facts sf ON sf.account_id = pf.account_id AND sf.term_id = NEW.term_id
                    JOIN public.subscription_coverage_terms sc ON sc.account_id = pf.account_id AND sc.term_id = NEW.term_id
                    WHERE pf.account_id = NEW.account_id AND pf.term_id = NEW.predecessor_term_id AND pf.basis = 'Paid'
                        AND sf.time_zone = pf.time_zone AND sf.billing_period = pf.billing_period
                        AND sf.calendar_version = pf.calendar_version
                        AND (CASE WHEN fresh_kind = 'RestoredAccess' THEN sf.period_ordinal = 0
                            AND sc.starts_at >= pc.ends_at + interval '168 hours'
                            AND NEW.payment_effective_at >= pc.ends_at + interval '168 hours'
                            AND NEW.grace_days_included = 0
                            ELSE sf.anchor_local = pf.anchor_local AND sf.period_ordinal = pf.period_ordinal + 1
                                AND sc.starts_at = pc.ends_at END)
                        AND sc.capacity = {priorCapacity} AND sc.mode = {priorMode}
                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id)
                            = (SELECT count(*) FROM public.shop_affiliations a
                                WHERE a.account_id = NEW.account_id AND a.term_id = NEW.predecessor_term_id)
                            + (SELECT count(*) FROM public.payment_fulfilment_shops p
                                WHERE p.account_id = NEW.account_id AND p.term_id = NEW.predecessor_term_id))) THEN
                RAISE EXCEPTION 'Payment fulfilment evidence is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        """;
}
