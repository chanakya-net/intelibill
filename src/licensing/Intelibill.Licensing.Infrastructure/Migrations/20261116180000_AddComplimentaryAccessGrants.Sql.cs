namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class AddComplimentaryAccessGrants
{
    // Writes need the provider grant context and the bound account. Reads: the provider grant context sees its own
    // account's receipts; account-scoped status reads see operational rows; a named shop sees only its own grants.
    private const string Policies = $"""
        CREATE POLICY grant_scope ON public.complimentary_grant_scopes FOR ALL
            USING ({GrantContext} AND account_id = {AccountScope})
            WITH CHECK ({GrantContext} AND account_id = {AccountScope});
        CREATE POLICY account_read ON public.complimentary_grants FOR SELECT USING (account_id = {AccountScope});
        CREATE POLICY shop_read ON public.complimentary_grants FOR SELECT USING (EXISTS (
            SELECT FROM public.complimentary_grant_shops l
            WHERE l.grant_id = complimentary_grants.grant_id AND l.shop_id = {ShopScope}));
        CREATE POLICY grant_insert ON public.complimentary_grants FOR INSERT WITH CHECK (
            {GrantContext} AND account_id = {AccountScope} AND EXISTS (
                SELECT FROM public.complimentary_grant_scopes s
                WHERE s.account_id = complimentary_grants.account_id AND s.revision = complimentary_grants.granted_version));
        CREATE POLICY provider_read ON public.complimentary_grant_receipts FOR SELECT USING (
            {GrantContext} AND account_id = {AccountScope});
        CREATE POLICY receipt_insert ON public.complimentary_grant_receipts FOR INSERT WITH CHECK (
            {GrantContext} AND account_id = {AccountScope}
            AND admin_id = nullif(current_setting('app.grant_admin_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.complimentary_grants g WHERE g.grant_id = complimentary_grant_receipts.grant_id
                AND g.account_id = complimentary_grant_receipts.account_id
                AND g.granted_version = complimentary_grant_receipts.expected_version + 1));
        CREATE POLICY account_read ON public.complimentary_grant_shops FOR SELECT USING (account_id = {AccountScope});
        CREATE POLICY shop_read ON public.complimentary_grant_shops FOR SELECT USING (shop_id = {ShopScope});
        CREATE POLICY link_insert ON public.complimentary_grant_shops FOR INSERT WITH CHECK (
            {GrantContext} AND account_id = {AccountScope});
        CREATE POLICY grant_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (
            {GrantContext} AND account_id = {AccountScope} AND EXISTS (
                SELECT FROM public.complimentary_grants g
                WHERE g.account_id = subscription_coverage_terms.account_id AND g.term_id = subscription_coverage_terms.term_id
                    AND g.starts_at = subscription_coverage_terms.starts_at AND g.ends_at = subscription_coverage_terms.ends_at
                    AND g.coverage_mode = subscription_coverage_terms.mode AND g.shop_capacity = subscription_coverage_terms.capacity));
        CREATE POLICY grant_shop_read ON public.subscription_coverage_terms FOR SELECT USING (EXISTS (
            SELECT FROM public.complimentary_grant_shops l
            JOIN public.shop_affiliations a ON a.shop_id = l.shop_id AND a.account_id = l.account_id
            WHERE l.term_id = subscription_coverage_terms.term_id AND l.account_id = subscription_coverage_terms.account_id
                AND l.shop_id = {ShopScope}));
        """;

    // Each named shop must belong to the grant's account through its own affiliation (checked by trigger, not a
    // foreign key, so the retained affiliation table keeps refusing truncation with its own 55000); windows for one shop never
    // overlap (adjacent windows are legal); a grant's coverage term can never be assigned to any other shop.
    private const string Guards = """
        CREATE FUNCTION public.guard_complimentary_grant_shop() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('g' || NEW.shop_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.request_id = NEW.affiliation_request_id
                    AND a.shop_id = NEW.shop_id AND a.account_id = NEW.account_id)
                OR EXISTS (SELECT FROM public.complimentary_grant_shops l WHERE l.shop_id = NEW.shop_id
                    AND l.starts_at < NEW.ends_at AND NEW.starts_at < l.ends_at) THEN
                RAISE EXCEPTION 'Complimentary grant shop is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_complimentary_grant_shop BEFORE INSERT ON public.complimentary_grant_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_complimentary_grant_shop();

        CREATE FUNCTION public.guard_complimentary_grant_scope() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF TG_OP <> 'UPDATE' THEN
                RAISE EXCEPTION 'Complimentary grant revisions are retained' USING ERRCODE = '55000';
            END IF;
            IF NEW.account_id <> OLD.account_id OR NEW.revision <> OLD.revision + 1 THEN
                RAISE EXCEPTION 'Complimentary grant revisions only advance' USING ERRCODE = '55000';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_complimentary_grant_scope BEFORE UPDATE OR DELETE ON public.complimentary_grant_scopes
            FOR EACH ROW EXECUTE FUNCTION public.guard_complimentary_grant_scope();

        CREATE FUNCTION public.reserve_complimentary_coverage() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.complimentary_grants g
                WHERE g.account_id = NEW.account_id AND g.term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Complimentary coverage belongs to its named shops' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER reserve_complimentary_coverage BEFORE INSERT ON public.shop_affiliation_requests
            FOR EACH ROW EXECUTE FUNCTION public.reserve_complimentary_coverage();
        CREATE TRIGGER reserve_complimentary_coverage BEFORE INSERT ON public.shop_affiliations
            FOR EACH ROW EXECUTE FUNCTION public.reserve_complimentary_coverage();
        """;

    // At commit a grant has its receipt, exactly its named shops and the revision it advanced to.
    private const string Completeness = """
        CREATE FUNCTION public.require_complete_complimentary_grant() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.complimentary_grant_receipts r WHERE r.grant_id = NEW.grant_id)
                OR (SELECT count(*) FROM public.complimentary_grant_shops l WHERE l.grant_id = NEW.grant_id) <> NEW.shop_capacity
                OR NOT EXISTS (SELECT FROM public.complimentary_grant_scopes s
                    WHERE s.account_id = NEW.account_id AND s.revision >= NEW.granted_version) THEN
                RAISE EXCEPTION 'Complimentary grant evidence is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER complimentary_grant_complete AFTER INSERT ON public.complimentary_grants
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_complete_complimentary_grant();
        """;

    private const string Rollback = """
        LOCK TABLE public.complimentary_grant_shops, public.complimentary_grant_receipts, public.complimentary_grants,
            public.complimentary_grant_scopes IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.complimentary_grant_scopes NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.complimentary_grants NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.complimentary_grant_scopes) OR EXISTS (SELECT FROM public.complimentary_grants) THEN
                RAISE EXCEPTION 'Complimentary grant evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER reserve_complimentary_coverage ON public.shop_affiliation_requests;
        DROP TRIGGER reserve_complimentary_coverage ON public.shop_affiliations;
        DROP FUNCTION public.reserve_complimentary_coverage();
        DROP POLICY grant_shop_read ON public.subscription_coverage_terms;
        DROP POLICY grant_term_insert ON public.subscription_coverage_terms;
        DROP TABLE public.complimentary_grant_shops, public.complimentary_grant_receipts, public.complimentary_grants,
            public.complimentary_grant_scopes;
        DROP FUNCTION public.guard_complimentary_grant_shop();
        DROP FUNCTION public.guard_complimentary_grant_scope();
        DROP FUNCTION public.require_complete_complimentary_grant();
        """;
}
