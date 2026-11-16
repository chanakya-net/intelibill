namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CheckoutPayments
{
    private static readonly string Guards = $$"""
        CREATE FUNCTION public.guard_checkout_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id)
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                        AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
                RAISE EXCEPTION 'Checkout shop must be affiliated and inserted before the quote' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER checkout_shop_guard BEFORE INSERT ON public.checkout_order_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_shop();

        CREATE FUNCTION public.require_checkout_shop_seal() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                  AND q.xmin = pg_current_xact_id()::xid AND q.replay_id = NEW.replay_id) THEN
                RAISE EXCEPTION 'Checkout shop and its sealed quote must be inserted in the same transaction' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER checkout_shop_sealed AFTER INSERT ON public.checkout_order_shops
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_checkout_shop_seal();

        CREATE FUNCTION public.guard_checkout_order() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash
            FROM public.checkout_order_shops s JOIN public.shop_affiliations a
                ON a.request_id = s.affiliation_request_id
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id AND s.replay_id = NEW.replay_id
                AND a.term_id IS NOT DISTINCT FROM NEW.term_id AND a.applied_at <= NEW.created_at;
            IF NEW.actor_id IS DISTINCT FROM {{Actor}} OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
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
        CREATE TRIGGER checkout_order_guard BEFORE INSERT ON public.checkout_orders
            FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_order();

        CREATE FUNCTION public.guard_checkout_acceptance() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.actor_id IS DISTINCT FROM {{Actor}} OR NOT EXISTS (
                SELECT FROM public.checkout_orders q
                JOIN public.billing_account_members m ON m.account_id = q.account_id AND m.merchant_user_id = NEW.actor_id
                JOIN public.merchant_billing_delegation_uses u ON u.replay_id = NEW.replay_id AND u.account_id = q.account_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND m.role IN (1, 2) AND m.status = 1 AND m.revision = NEW.membership_revision
                    AND u.action = 'billing.checkout.accept' AND u.actor_id = NEW.actor_id AND u.request_id = NEW.request_id
                    AND q.created_at <= NEW.accepted_at AND NEW.accepted_at < q.expires_at
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND q.snapshot->>'customerTermsVersion' = NEW.customer_terms_version
                    AND q.snapshot->>'refundPolicyVersion' = NEW.refund_policy_version) THEN
                RAISE EXCEPTION 'Checkout acceptance must match retained quote and live billing authority' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER checkout_acceptance_guard BEFORE INSERT ON public.checkout_order_acceptances
            FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_acceptance();
        """;
}
