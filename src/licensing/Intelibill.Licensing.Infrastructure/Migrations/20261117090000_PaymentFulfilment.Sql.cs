namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaymentFulfilment
{
    private static string AttemptWrite(string table) => $"""
        CREATE POLICY attempt_insert ON public.{table} FOR INSERT WITH CHECK (
            account_id = {Account} AND attempt_id = {Attempt});
        """;

    private static string FulfilmentWrite(string table) => $"""
        CREATE POLICY fulfilment_insert ON public.{table} FOR INSERT WITH CHECK (
            account_id = {Account} AND EXISTS (SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = {table}.account_id AND f.order_id = {table}.order_id AND f.attempt_id = {Attempt}));
        """;

    private static string AttemptRead(string table) => $"""
        CREATE POLICY payment_attempt_read ON public.{table} FOR SELECT USING (
            account_id = {Account} AND EXISTS (SELECT FROM public.payment_attempts a
                WHERE a.account_id = {table}.account_id AND a.order_id = {table}.order_id AND a.attempt_id = {Attempt}));
        """;

    // A paid term is written only with the attempt's own fulfilment; a named shop sees only its own paid coverage.
    private static readonly string Policies = $"""
        CREATE POLICY attempt_start ON public.payment_attempts FOR INSERT WITH CHECK (
            account_id = {Account} AND actor_id = {Actor} AND EXISTS (
                SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.replay_id = payment_attempts.replay_id AND u.account_id = payment_attempts.account_id
                    AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid
                    AND u.action = 'billing.checkout.payment.start'));
        {AttemptWrite("payment_attempt_sessions")}
        {AttemptWrite("payment_events")}
        {AttemptWrite("verified_payments")}
        {AttemptWrite("payment_fulfilments")}
        {FulfilmentWrite("payment_fulfilment_shops")}
        {FulfilmentWrite("payment_fulfilment_deliveries")}
        {AttemptRead("checkout_orders")}
        {AttemptRead("checkout_order_shops")}
        {AttemptRead("checkout_order_acceptances")}
        CREATE POLICY shop_read ON public.payment_fulfilment_shops FOR SELECT USING (shop_id = {Shop});
        CREATE POLICY paid_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (
            account_id = {Account} AND EXISTS (SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = subscription_coverage_terms.account_id AND f.term_id = subscription_coverage_terms.term_id
                    AND f.attempt_id = {Attempt}));
        CREATE POLICY paid_facts_insert ON public.subscription_term_facts FOR INSERT WITH CHECK (
            account_id = {Account} AND basis = 'Paid' AND source_reference = 'payment-fulfilment/1' AND EXISTS (
                SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = subscription_term_facts.account_id AND f.term_id = subscription_term_facts.term_id
                    AND f.attempt_id = {Attempt}));
        CREATE POLICY paid_shop_read ON public.subscription_coverage_terms FOR SELECT USING (EXISTS (
            SELECT FROM public.payment_fulfilment_shops l
            WHERE l.term_id = subscription_coverage_terms.term_id AND l.account_id = subscription_coverage_terms.account_id
                AND l.shop_id = {Shop}));
        CREATE POLICY paid_shop_read ON public.subscription_term_facts FOR SELECT USING (EXISTS (
            SELECT FROM public.payment_fulfilment_shops l
            WHERE l.term_id = subscription_term_facts.term_id AND l.account_id = subscription_term_facts.account_id
                AND l.shop_id = {Shop}));
        """;

    // Money evidence must match the pinned attempt exactly; a link names only an order shop that has no other coverage.
    private const string Guards = """
        CREATE FUNCTION public.guard_payment_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            -- Checked here rather than by foreign key so the retained checkout tables keep refusing truncation (55000).
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND q.term_id IS NULL
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.snapshot_hash = NEW.snapshot_hash AND NEW.created_at < q.expires_at) THEN
                RAISE EXCEPTION 'Payment attempt must match its accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_payment_attempt BEFORE INSERT ON public.payment_attempts
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_attempt();

        CREATE FUNCTION public.guard_verified_payment() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.payment_attempts a JOIN public.payment_attempt_sessions s USING (account_id, attempt_id)
                WHERE a.account_id = NEW.account_id AND a.attempt_id = NEW.attempt_id AND a.order_id = NEW.order_id
                    AND a.provider_id = NEW.provider_id AND a.provider_account_id = NEW.provider_account_id
                    AND a.environment = NEW.environment AND a.configuration_version = NEW.configuration_version
                    AND a.amount_paise = NEW.amount_paise AND a.currency = NEW.currency) THEN
                RAISE EXCEPTION 'Verified payment must equal its pinned attempt' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_verified_payment BEFORE INSERT ON public.verified_payments
            FOR EACH ROW EXECUTE FUNCTION public.guard_verified_payment();

        CREATE FUNCTION public.guard_payment_fulfilment_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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
        CREATE TRIGGER guard_payment_fulfilment_shop BEFORE INSERT ON public.payment_fulfilment_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_fulfilment_shop();

        CREATE FUNCTION public.reserve_paid_fulfilment_coverage() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = NEW.account_id AND f.term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Paid coverage belongs to its named shops' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER reserve_paid_fulfilment_coverage BEFORE INSERT ON public.shop_affiliation_requests
            FOR EACH ROW EXECUTE FUNCTION public.reserve_paid_fulfilment_coverage();
        CREATE TRIGGER reserve_paid_fulfilment_coverage BEFORE INSERT ON public.shop_affiliations
            FOR EACH ROW EXECUTE FUNCTION public.reserve_paid_fulfilment_coverage();
        """;

    // At commit a fulfilment has its term facts at the order price, every named shop and its audit event.
    private const string Completeness = """
        CREATE FUNCTION public.require_complete_payment_fulfilment() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                    JOIN public.subscription_term_facts t ON t.account_id = q.account_id AND t.term_id = NEW.term_id
                    JOIN public.subscription_coverage_terms c ON c.account_id = q.account_id AND c.term_id = NEW.term_id
                    WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND t.basis = 'Paid'
                        AND t.price_paise = q.amount_paise AND c.capacity = (q.snapshot->>'capacity')::int
                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = q.shop_count)
                OR NOT EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = NEW.attempt_id
                    AND e.kind = 'OrderFulfilled') THEN
                RAISE EXCEPTION 'Payment fulfilment evidence is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER payment_fulfilment_complete AFTER INSERT ON public.payment_fulfilments
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_complete_payment_fulfilment();
        """;

    private const string Rollback = """
        LOCK TABLE public.payment_attempts IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.payment_attempts NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.payment_attempts) THEN
                RAISE EXCEPTION 'Payment evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER reserve_paid_fulfilment_coverage ON public.shop_affiliation_requests;
        DROP TRIGGER reserve_paid_fulfilment_coverage ON public.shop_affiliations;
        DROP POLICY paid_term_insert ON public.subscription_coverage_terms;
        DROP POLICY paid_shop_read ON public.subscription_coverage_terms;
        DROP POLICY paid_facts_insert ON public.subscription_term_facts;
        DROP POLICY paid_shop_read ON public.subscription_term_facts;
        DROP POLICY payment_attempt_read ON public.checkout_orders;
        DROP POLICY payment_attempt_read ON public.checkout_order_shops;
        DROP POLICY payment_attempt_read ON public.checkout_order_acceptances;
        DROP TABLE public.payment_fulfilment_deliveries, public.payment_fulfilment_shops, public.payment_fulfilments,
            public.verified_payments, public.payment_events, public.payment_attempt_sessions, public.payment_attempts,
            public.development_gateway_payments, public.development_gateway_sessions;
        DROP FUNCTION public.guard_payment_attempt(), public.guard_verified_payment(), public.guard_payment_fulfilment_shop(),
            public.reserve_paid_fulfilment_coverage(), public.require_complete_payment_fulfilment();
        """;
}
