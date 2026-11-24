namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundApprovals
{
    private const string Scope = """
        CREATE FUNCTION public.refund_target_shops(acct uuid, term uuid, at_time timestamptz)
        RETURNS TABLE(shop_id uuid, request_id uuid, shop_name text, restricted boolean,
            scheduled_restriction_at timestamptz, closed boolean, courtesy_ends_at timestamptz)
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT a.shop_id, a.request_id, a.shop_name, restrictions.active, restrictions.scheduled,
                EXISTS (SELECT FROM public.active_operational_closure_targets c WHERE c.account_id = acct
                    AND c.shop_id = a.shop_id AND c.affiliation_request_id = a.request_id),
                (SELECT max(g.ends_at) FROM public.complimentary_grant_shops g WHERE g.account_id = acct
                    AND g.shop_id = a.shop_id AND g.starts_at <= at_time AND at_time < g.ends_at)
            FROM public.shop_affiliations a
            CROSS JOIN LATERAL (SELECT coalesce(bool_or(x.effective_at <= at_time), false) AS active,
                min(x.effective_at) FILTER (WHERE x.effective_at > at_time) AS scheduled
                FROM public.active_operational_restrictions x WHERE x.account_id = acct
                    AND (x.shop_id IS NULL OR x.shop_id = a.shop_id)) restrictions
            WHERE a.account_id = acct AND (a.term_id = term OR EXISTS (
                SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = acct AND p.term_id = term
                    AND p.shop_id = a.shop_id AND p.affiliation_request_id = a.request_id))
            ORDER BY a.shop_id
        $$;

        CREATE FUNCTION public.refund_term_committed(acct uuid, term uuid) RETURNS boolean
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS (SELECT FROM public.payment_fulfilments s WHERE s.account_id = acct AND s.predecessor_term_id = term)
                OR EXISTS (SELECT FROM public.capacity_increases i WHERE i.account_id = acct AND i.term_id = term AND i.state = 'Pending')
                OR EXISTS (SELECT FROM public.scheduled_capacity_reductions r WHERE r.account_id = acct AND r.term_id = term
                    AND r.state IN ('Scheduled', 'Funded', 'Applying', 'NeedsResolution', 'NeedsReview'))
        $$;
        """;

    private const string AffiliationCoverageGuard = """
        CREATE OR REPLACE FUNCTION public.guard_shop_affiliation_coverage() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.shop_affiliation_requests r
                    LEFT JOIN public.subscription_coverage_terms t
                        ON t.account_id = r.account_id AND t.term_id = r.term_id
                    WHERE r.account_id = NEW.account_id AND r.shop_id = NEW.shop_id AND r.request_id = NEW.request_id
                    AND r.term_id IS NOT DISTINCT FROM NEW.term_id
                    AND r.created_at <= NEW.accepted_at AND NEW.accepted_at < r.expires_at
                    AND ((NEW.term_id IS NULL AND r.reviewed_refund_paid_through IS NULL)
                        OR (NEW.term_id IS NOT NULL AND t.term_id IS NOT NULL
                            AND r.reviewed_refund_paid_through IS NOT DISTINCT FROM (
                                SELECT new_paid_through FROM public.refund_boundary(t.account_id, t.term_id))
                            AND NEW.coverage_ends_at = coalesce(r.reviewed_refund_paid_through, t.ends_at)
                            AND t.starts_at <= NEW.applied_at AND NEW.applied_at < NEW.coverage_ends_at
                            AND public.capacity_slot_available(t.account_id,t.term_id,t.capacity)))) THEN
                RAISE EXCEPTION 'Shop affiliation selection is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    private const string RestoreAffiliationCoverageGuard = """
        CREATE OR REPLACE FUNCTION public.guard_shop_affiliation_coverage() RETURNS trigger
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
        """;
}
