namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidCapacityIncreases
{
    private const string CoverageGuard = """
        ALTER FUNCTION public.guard_shop_affiliation_coverage() RENAME TO guard_shop_affiliation_before_increases;
        DROP TRIGGER shop_affiliation_coverage_guard ON public.shop_affiliations;
        CREATE FUNCTION public.capacity_slot_available(account uuid, term uuid, original_capacity int)
        RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT NOT EXISTS (SELECT FROM public.capacity_increases WHERE account_id=account AND term_id=term AND state='Pending')
                AND NOT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id=account AND predecessor_term_id=term)
                AND coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id=account AND i.term_id=term
                    AND i.state='Applied' ORDER BY target_revision DESC LIMIT 1), original_capacity) > (
                    SELECT count(*) FROM (
                        SELECT shop_id FROM public.shop_affiliations WHERE account_id=account AND term_id=term
                        UNION SELECT a.shop_id FROM public.payment_fulfilment_shops p JOIN public.shop_affiliations a
                            ON a.account_id=p.account_id AND a.shop_id=p.shop_id AND a.request_id=p.affiliation_request_id
                            WHERE p.account_id=account AND p.term_id=term
                    ) occupied)
        $$;
        CREATE FUNCTION public.guard_shop_affiliation_coverage() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.shop_affiliation_requests r
                    WHERE r.account_id = NEW.account_id AND r.shop_id = NEW.shop_id AND r.request_id = NEW.request_id
                    AND r.term_id IS NOT DISTINCT FROM NEW.term_id
                    AND r.created_at <= NEW.accepted_at AND NEW.accepted_at < r.expires_at)
                OR (NEW.term_id IS NOT NULL AND NOT EXISTS (
                    SELECT FROM public.subscription_coverage_terms t
                    WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id
                    AND t.starts_at <= NEW.applied_at AND NEW.coverage_ends_at = t.ends_at
                    AND public.capacity_slot_available(t.account_id,t.term_id,t.capacity)
                )) THEN
                RAISE EXCEPTION 'Shop affiliation selection is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER shop_affiliation_coverage_guard BEFORE INSERT ON public.shop_affiliations
            FOR EACH ROW EXECUTE FUNCTION public.guard_shop_affiliation_coverage();
        """;
}
