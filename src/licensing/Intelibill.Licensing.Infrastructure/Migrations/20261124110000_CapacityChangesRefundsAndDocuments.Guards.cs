namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CapacityChangesRefundsAndDocuments
{
    // Named shops are inserted first, from live affiliations, then sealed by their header in the same transaction.
    private static readonly string Guards = $$"""
        CREATE FUNCTION public.guard_capacity_quote_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.capacity_change_quotes q WHERE q.account_id = NEW.account_id AND q.quote_id = NEW.quote_id)
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                        AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
                RAISE EXCEPTION 'Capacity quote shop must be affiliated and inserted before its quote' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER capacity_quote_shop_guard BEFORE INSERT ON public.capacity_change_quote_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_capacity_quote_shop();

        CREATE FUNCTION public.require_capacity_quote_seal() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.capacity_change_quotes q WHERE q.account_id = NEW.account_id
                  AND q.quote_id = NEW.quote_id AND q.xmin = pg_current_xact_id()::xid AND q.replay_id = NEW.replay_id) THEN
                RAISE EXCEPTION 'Capacity quote shop and its sealed quote must be inserted together' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER capacity_quote_shop_sealed AFTER INSERT ON public.capacity_change_quote_shops
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_capacity_quote_seal();

        CREATE FUNCTION public.guard_capacity_quote() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash FROM public.capacity_change_quote_shops s
            WHERE s.account_id = NEW.account_id AND s.quote_id = NEW.quote_id AND s.replay_id = NEW.replay_id;
            IF NEW.actor_id IS DISTINCT FROM {{Actor}} OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
                OR (SELECT count(*) FROM public.capacity_change_quote_shops s
                    WHERE s.account_id = NEW.account_id AND s.quote_id = NEW.quote_id) <> named
                OR NOT EXISTS (SELECT FROM public.subscription_coverage_terms t
                    LEFT JOIN public.trial_history h ON h.account_id = t.account_id AND h.term_id = t.term_id
                    LEFT JOIN LATERAL (SELECT e.ends_at FROM public.trial_extensions e
                        WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY e.revision DESC LIMIT 1) e ON true
                    WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND t.capacity = NEW.prior_capacity
                        AND t.starts_at = NEW.interval_starts_at AND coalesce(e.ends_at, t.ends_at) = NEW.paid_through)
                OR EXISTS (SELECT FROM public.payment_fulfilments f
                    WHERE f.account_id = NEW.account_id AND f.predecessor_term_id = NEW.term_id)
                OR EXISTS (SELECT FROM public.payment_trial_conversions c
                    WHERE c.account_id = NEW.account_id AND c.trial_term_id = NEW.term_id)
                OR EXISTS (SELECT FROM public.capacity_change_quote_shops s
                    JOIN public.shop_affiliations a ON a.account_id = s.account_id AND a.shop_id = s.shop_id
                        AND a.request_id = s.affiliation_request_id
                    WHERE s.account_id = NEW.account_id AND s.quote_id = NEW.quote_id AND NOT (
                        a.term_id IS NOT DISTINCT FROM NEW.term_id
                        OR EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = s.account_id
                            AND p.term_id = NEW.term_id AND p.shop_id = s.shop_id
                            AND p.affiliation_request_id = s.affiliation_request_id)
                        OR (a.term_id IS NULL AND EXISTS (SELECT FROM public.trial_history h
                            WHERE h.account_id = s.account_id AND h.term_id = NEW.term_id AND h.shop_id = s.shop_id))))
                OR (NEW.snapshot->>'accountId')::uuid IS DISTINCT FROM NEW.account_id
                OR (NEW.snapshot->>'quoteId')::uuid IS DISTINCT FROM NEW.quote_id
                OR (NEW.snapshot->>'termId')::uuid IS DISTINCT FROM NEW.term_id
                OR NEW.snapshot->>'outcome' IS DISTINCT FROM NEW.outcome
                OR (NEW.snapshot->>'amountPaise')::bigint IS DISTINCT FROM NEW.amount_paise
                OR (NEW.snapshot->>'purchasedGrossPaise')::bigint IS DISTINCT FROM NEW.purchased_gross_paise
                OR (NEW.snapshot->>'targetGrossPaise')::bigint IS DISTINCT FROM NEW.target_gross_paise
                OR (NEW.snapshot->>'priorCapacity')::int IS DISTINCT FROM NEW.prior_capacity
                OR (NEW.snapshot->>'targetCapacity')::int IS DISTINCT FROM NEW.target_capacity
                OR (NEW.snapshot->>'shopCount')::int IS DISTINCT FROM NEW.shop_count
                OR NEW.snapshot->>'shopHash' IS DISTINCT FROM NEW.shop_hash
                OR NEW.snapshot->>'snapshotHash' IS DISTINCT FROM NEW.snapshot_hash
                OR NEW.snapshot->>'currency' IS DISTINCT FROM 'INR'
                OR (NEW.snapshot->>'createdAt')::timestamptz IS DISTINCT FROM NEW.created_at
                OR (NEW.snapshot->>'expiresAt')::timestamptz IS DISTINCT FROM NEW.expires_at THEN
                RAISE EXCEPTION 'Capacity quote must contain complete coherent evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER capacity_quote_guard BEFORE INSERT ON public.capacity_change_quotes
            FOR EACH ROW EXECUTE FUNCTION public.guard_capacity_quote();
        """;
}
