namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidFreshStarts
{
    private const string SourceGuards = """
        CREATE FUNCTION public.guard_checkout_trial_source() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id)
                OR NOT EXISTS (SELECT FROM public.trial_history h
                    LEFT JOIN LATERAL (SELECT revision, ends_at FROM public.trial_extensions e
                        WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY revision DESC LIMIT 1) e ON true
                    WHERE h.account_id = NEW.account_id AND h.shop_id = NEW.shop_id AND h.term_id = NEW.trial_term_id
                        AND NEW.revision = coalesce(e.revision, 0) AND NEW.ends_at = coalesce(e.ends_at, h.ends_at)) THEN
                RAISE EXCEPTION 'Checkout trial source must be current and unsealed' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_checkout_trial_source BEFORE INSERT ON public.checkout_trial_sources
            FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_trial_source();

        CREATE FUNCTION public.require_checkout_trial_sources() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE summary jsonb := NEW.snapshot->'freshStart';
        DECLARE frozen bigint;
        DECLARE trial_count bigint;
        DECLARE digest text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to('["paid-fresh-start/1","trials",[' ||
                coalesce(string_agg('["' || s.shop_id || '","' || s.trial_term_id || '",' || s.revision ||
                    ',"' || to_char(s.ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') || '"]',
                    ',' ORDER BY s.shop_id), '') || ']]', 'UTF8')), 'hex')
            INTO frozen, digest FROM public.checkout_trial_sources s
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id;
            SELECT count(*) INTO trial_count FROM public.checkout_order_shops s
            JOIN public.trial_history h USING (account_id, shop_id)
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id;
            IF (summary IS NULL AND frozen <> 0)
                OR (summary IS NOT NULL AND (summary->>'policy' IS DISTINCT FROM 'paid-fresh-start/1'
                    OR summary->>'trialSourceHash' IS DISTINCT FROM digest
                    OR (summary->>'trialShopCount')::int IS DISTINCT FROM frozen
                    OR (NEW.term_id IS NULL AND (NEW.snapshot->>'startRule' IS DISTINCT FROM 'Fulfilment' OR frozen <> trial_count))
                    OR (NEW.term_id IS NOT NULL AND (NEW.snapshot->>'startRule' IS DISTINCT FROM 'RestoredAccess' OR frozen <> 0)))) THEN
                RAISE EXCEPTION 'Checkout fresh-start source set is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER require_checkout_trial_sources BEFORE INSERT ON public.checkout_orders
            FOR EACH ROW EXECUTE FUNCTION public.require_checkout_trial_sources();

        CREATE FUNCTION public.refuse_converted_trial_extension() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF EXISTS (SELECT FROM public.payment_trial_conversions x WHERE x.account_id = NEW.account_id
                AND x.shop_id = NEW.shop_id) THEN
                RAISE EXCEPTION 'A converted trial cannot be extended' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER refuse_converted_trial_extension BEFORE INSERT ON public.trial_extensions
            FOR EACH ROW EXECUTE FUNCTION public.refuse_converted_trial_extension();
        """;

    private static readonly string RestorationGuards = $$"""
        CREATE FUNCTION public.guard_payment_restoration_hold() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id)
                OR NOT EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND q.snapshot->>'startRule' IN ('Fulfilment', 'RestoredAccess'))
                OR (NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.verified_payments v
                    WHERE v.attempt_id = NEW.attempt_id AND v.account_id = NEW.account_id AND v.order_id = NEW.order_id
                        AND v.captured_at = NEW.effective_at AND v.verified_at = NEW.held_at))
                OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
                    WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id AND r.order_id = NEW.order_id
                        AND r.received_at = NEW.effective_at AND r.review_reason = 'RestorationPending'))
                OR (NEW.account_revision IS NULL AND EXISTS (SELECT FROM public.entitlement_account_heads h
                    WHERE h.account_id = NEW.account_id))
                OR (NEW.account_revision IS NOT NULL AND NOT EXISTS (SELECT FROM public.entitlement_account_heads h
                    WHERE h.account_id = NEW.account_id AND h.revision = NEW.account_revision AND h.restriction = NEW.restriction
                        AND h.upstream_verified_at = NEW.upstream_verified_at)) THEN
                RAISE EXCEPTION 'Payment restoration hold needs verified funds and current restriction evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_payment_restoration_hold BEFORE INSERT ON public.payment_restoration_holds
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_restoration_hold();

        CREATE FUNCTION public.guard_paid_fresh_start() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id)
                OR NOT EXISTS (SELECT FROM public.entitlement_account_heads h
                    WHERE h.account_id = NEW.account_id AND h.restriction = 'Unrestricted'
                        AND h.revision = NEW.account_revision AND h.upstream_verified_at = NEW.upstream_verified_at
                        AND h.released_revision IS NOT DISTINCT FROM NEW.released_revision FOR SHARE) THEN
                RAISE EXCEPTION 'Paid fresh start needs current unrestricted restoration evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_paid_fresh_start BEFORE INSERT ON public.paid_fresh_starts
            FOR EACH ROW EXECUTE FUNCTION public.guard_paid_fresh_start();

        CREATE FUNCTION public.guard_payment_trial_conversion() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.payment_fulfilment_shops p
                    WHERE p.account_id = NEW.account_id AND p.order_id = NEW.order_id AND p.shop_id = NEW.shop_id)
                OR NOT EXISTS (SELECT FROM public.paid_fresh_starts f
                    JOIN public.checkout_trial_sources s USING (account_id, order_id)
                    JOIN public.trial_history h ON h.account_id = s.account_id AND h.shop_id = s.shop_id
                    LEFT JOIN LATERAL (SELECT revision, ends_at FROM public.trial_extensions e
                        WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY revision DESC LIMIT 1) e ON true
                    WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.kind = 'TrialConversion'
                        AND s.shop_id = NEW.shop_id AND s.trial_term_id = NEW.trial_term_id
                        AND s.revision = coalesce(e.revision, 0) AND s.ends_at = coalesce(e.ends_at, h.ends_at)
                        AND NEW.paid_starts_at >= h.starts_at) THEN
                RAISE EXCEPTION 'Trial conversion must use the accepted trial evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_payment_trial_conversion BEFORE INSERT ON public.payment_trial_conversions
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_trial_conversion();
        """;
}
