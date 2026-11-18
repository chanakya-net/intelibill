namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>Restores the #869 guards exactly; only reachable while no renewal evidence exists.</summary>
public sealed partial class PaidRenewals
{
    private static readonly string RollbackSql = $$"""
        DROP TRIGGER guard_payment_fulfilment_renewal ON public.payment_fulfilments;
        DROP FUNCTION public.guard_payment_fulfilment_renewal();
        CREATE OR REPLACE FUNCTION public.guard_checkout_order() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash
            FROM public.checkout_order_shops s JOIN public.shop_affiliations a
                ON a.request_id = s.affiliation_request_id
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id AND s.replay_id = NEW.replay_id
                AND a.term_id IS NOT DISTINCT FROM NEW.term_id AND a.applied_at <= NEW.created_at;
            IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid
                OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
                OR (SELECT count(*) FROM public.checkout_order_shops s
                    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id) <> named
                OR (NEW.term_id IS NOT NULL AND (SELECT count(*) FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) <> named)
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
        CREATE OR REPLACE FUNCTION public.guard_payment_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND q.term_id IS NULL
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
                    AND q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment'
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND a.accepted_at < q.expires_at) THEN
                RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE OR REPLACE FUNCTION public.guard_payment_fulfilment_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.payment_fulfilments f
                    JOIN public.checkout_order_shops s ON s.account_id = f.account_id AND s.order_id = f.order_id
                    WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.term_id = NEW.term_id
                        AND s.shop_id = NEW.shop_id AND s.affiliation_request_id = NEW.affiliation_request_id)
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id = NEW.account_id
                    AND a.shop_id = NEW.shop_id AND a.request_id = NEW.affiliation_request_id AND a.term_id IS NULL)
                OR EXISTS (SELECT FROM public.trial_history h WHERE h.shop_id = NEW.shop_id) THEN
                RAISE EXCEPTION 'Paid shop coverage is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE OR REPLACE FUNCTION public.require_complete_payment_fulfilment() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                    JOIN public.subscription_term_facts t ON t.account_id = q.account_id AND t.term_id = NEW.term_id
                    JOIN public.subscription_coverage_terms c ON c.account_id = q.account_id AND c.term_id = NEW.term_id
                    WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND t.basis = 'Paid'
                        AND t.price_paise = q.amount_paise AND c.capacity = (q.snapshot->>'capacity')::int
                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = q.shop_count)
                OR ({{MissingAudit}}) THEN
                RAISE EXCEPTION 'Payment fulfilment evidence is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        DROP INDEX public.payment_fulfilment_shops_account_shop;
        ALTER TABLE public.payment_fulfilment_shops DROP CONSTRAINT payment_fulfilment_shops_term_shop,
            ADD CONSTRAINT payment_fulfilment_shops_shop_id_key UNIQUE (shop_id);
        ALTER TABLE public.payment_fulfilments DROP CONSTRAINT payment_fulfilment_predecessor,
            DROP CONSTRAINT payment_fulfilment_one_successor, DROP CONSTRAINT payment_fulfilment_renewal_evidence,
            DROP COLUMN renewal_policy_version, DROP COLUMN grace_days_included, DROP COLUMN payment_effective_at,
            DROP COLUMN predecessor_term_id;
        """;
}
