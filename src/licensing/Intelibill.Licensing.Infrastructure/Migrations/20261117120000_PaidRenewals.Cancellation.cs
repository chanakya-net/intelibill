namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidRenewals
{
    // One live scope for the preview and both write guards. A paid link must still name the current affiliation.
    private const string CancellationScope = """
        CREATE FUNCTION public.subscription_cancellation_scope(scope_account uuid, scope_term uuid)
        RETURNS TABLE (shop_id uuid, request_id uuid, shop_name text, applied_at timestamptz, coverage_ends_at timestamptz)
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT a.shop_id, a.request_id, a.shop_name, a.applied_at,
                CASE WHEN a.term_id = t.term_id THEN a.coverage_ends_at ELSE t.ends_at END
            FROM public.subscription_coverage_terms t
            JOIN public.shop_affiliations a ON a.account_id = t.account_id
            WHERE t.account_id = scope_account AND t.term_id = scope_term
                AND NOT EXISTS (SELECT FROM public.payment_fulfilments s
                    WHERE s.account_id = t.account_id AND s.predecessor_term_id = t.term_id)
                AND (a.term_id = t.term_id OR EXISTS (
                    SELECT FROM public.payment_fulfilment_shops p
                    JOIN public.payment_fulfilments f USING (account_id, order_id, term_id)
                    WHERE p.account_id = t.account_id AND p.term_id = t.term_id AND f.predecessor_term_id IS NOT NULL
                        AND p.shop_id = a.shop_id AND p.affiliation_request_id = a.request_id))
                AND NOT EXISTS (SELECT FROM public.payment_fulfilment_shops p
                    WHERE p.account_id = t.account_id AND p.term_id = t.term_id
                        AND NOT EXISTS (SELECT FROM public.shop_affiliations live
                            WHERE live.account_id = p.account_id AND live.shop_id = p.shop_id
                                AND live.request_id = p.affiliation_request_id));
        $$;
        """;

    private const string RenewalCancellationGuards = """
        CREATE OR REPLACE FUNCTION public.guard_subscription_cancellation() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.subscription_coverage_terms t JOIN public.subscription_term_facts f USING (account_id, term_id)
                WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND f.basis = 'Paid'
                    AND f.price_paise IS NOT NULL AND t.starts_at <= NEW.recorded_at
                    AND t.ends_at = NEW.effective_at AND t.capacity >= NEW.shop_count
            ) OR NOT EXISTS (
                SELECT FROM public.subscription_cancellation_receipts r
                JOIN public.billing_account_members m ON m.account_id = r.account_id AND m.merchant_user_id = r.actor_id
                WHERE r.change_id = NEW.change_id AND m.status = 1 AND m.role IN (1, 2)
                    AND m.revision = r.membership_revision
            ) OR EXISTS (SELECT FROM public.payment_fulfilments r
                WHERE r.account_id = NEW.account_id AND r.predecessor_term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Cancellation requires the paid boundary and an acceptance receipt' USING ERRCODE = '23514';
            END IF;
            IF NEW.kind = 'WithdrawCancellation' THEN
                IF NOT EXISTS (
                    SELECT FROM public.scheduled_subscription_changes s
                    WHERE s.account_id = NEW.account_id AND s.change_id = NEW.withdraws_change_id
                        AND s.term_id = NEW.term_id AND s.kind = 'ScheduleCancellation' AND s.revision = NEW.revision - 1
                        AND s.effective_at = NEW.effective_at AND s.policy_version = NEW.policy_version
                        AND s.shop_count = NEW.shop_count AND s.shop_hash = NEW.shop_hash AND s.recorded_at <= NEW.recorded_at
                ) OR EXISTS (SELECT FROM public.scheduled_subscription_change_shops x WHERE x.change_id = NEW.change_id) THEN
                    RAISE EXCEPTION 'Withdrawal must follow the scheduled cancellation it withdraws' USING ERRCODE = '23514';
                END IF;
            ELSIF (NEW.revision > 1 AND NOT EXISTS (
                    SELECT FROM public.scheduled_subscription_changes w
                    WHERE w.account_id = NEW.account_id AND w.term_id = NEW.term_id
                        AND w.revision = NEW.revision - 1 AND w.kind = 'WithdrawCancellation'))
                OR NEW.shop_count <> (SELECT count(*) FROM public.scheduled_subscription_change_shops s WHERE s.change_id = NEW.change_id)
                OR NEW.shop_count <> (SELECT count(*)
                    FROM public.subscription_cancellation_scope(NEW.account_id, NEW.term_id)) THEN
                RAISE EXCEPTION 'Cancellation requires complete shop scope after any earlier withdrawal' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        CREATE OR REPLACE FUNCTION public.guard_subscription_cancellation_shop() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.scheduled_subscription_changes c
                CROSS JOIN LATERAL public.subscription_cancellation_scope(c.account_id, c.term_id) a
                WHERE c.change_id = NEW.change_id AND c.kind = 'ScheduleCancellation' AND a.shop_id = NEW.shop_id
                    AND a.request_id = NEW.request_id AND a.shop_name = NEW.shop_name
                    AND a.applied_at <= c.recorded_at AND a.coverage_ends_at = c.effective_at
            ) OR EXISTS (SELECT FROM public.subscription_cancellation_receipts r WHERE r.change_id = NEW.change_id) THEN
                RAISE EXCEPTION 'Cancellation shop must belong to the active scope before acceptance' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;
}
