namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidFreshStarts
{
    internal const string PaymentGuards = """
        CREATE OR REPLACE FUNCTION public.guard_payment_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                        OR (q.term_id IS NOT NULL AND (q.snapshot->>'startRule' = 'PriorPaidThrough'
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
                        OR (q.term_id IS NOT NULL AND (q.snapshot->>'startRule' = 'PriorPaidThrough'
                            OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND a.accepted_at < q.expires_at) THEN
                RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;

        """;

    private static string FulfilmentGuards => FreshCompleteness + ShopLinkGuard + Completeness;

    private const string ShopLinkGuard = """
        CREATE OR REPLACE FUNCTION public.guard_payment_fulfilment_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE prior uuid;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            SELECT f.predecessor_term_id INTO prior FROM public.payment_fulfilments f
            WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.term_id = NEW.term_id;
            IF NOT EXISTS (SELECT FROM public.payment_fulfilments f
                    JOIN public.checkout_order_shops s ON s.account_id = f.account_id AND s.order_id = f.order_id
                    WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.term_id = NEW.term_id
                        AND s.shop_id = NEW.shop_id AND s.affiliation_request_id = NEW.affiliation_request_id)
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id = NEW.account_id
                    AND a.shop_id = NEW.shop_id AND a.request_id = NEW.affiliation_request_id
                    AND (prior IS NOT NULL OR a.term_id IS NULL))
                OR (prior IS NULL AND EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.shop_id = NEW.shop_id))
                OR (prior IS NOT NULL AND NOT EXISTS (SELECT FROM public.shop_affiliations a
                        WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id AND a.term_id = prior)
                    AND NOT EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = NEW.account_id
                        AND p.shop_id = NEW.shop_id AND p.term_id = prior AND p.affiliation_request_id = NEW.affiliation_request_id))
                OR (EXISTS (SELECT FROM public.trial_history h WHERE h.shop_id = NEW.shop_id)
                    AND NOT EXISTS (SELECT FROM public.payment_trial_conversions c
                        WHERE c.account_id = NEW.account_id AND c.shop_id = NEW.shop_id
                            AND (prior IS NOT NULL OR c.order_id = NEW.order_id))) THEN
                RAISE EXCEPTION 'Paid shop coverage is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    private const string MissingAudit = """
        (NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_events e
            WHERE e.attempt_id = NEW.attempt_id AND e.account_id = NEW.account_id
                AND e.order_id = NEW.order_id AND e.kind = 'OrderFulfilled'))
        OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
            WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id
                AND r.order_id = NEW.order_id AND (r.term_id = NEW.term_id AND r.review_reason IS NULL
                    OR r.review_reason = 'RestorationPending' AND EXISTS (SELECT FROM public.payment_restoration_holds h
                        WHERE h.account_id = r.account_id AND h.order_id = r.order_id AND h.manual_receipt_id = r.receipt_id))))
        """;

    // A renewal successor is the predecessor's next period on the same anchor, same capacity, whole package.
    private static readonly string Completeness = $"""
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
                OR ({MissingAudit})
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
                        AND sc.capacity = pc.capacity AND sc.mode = pc.mode
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
