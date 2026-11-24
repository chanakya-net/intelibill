namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundApprovals
{
    // Invoker functions under forced RLS. The receipt lock is transaction-scoped and re-entrant: the handler takes it
    // first, the insert guard takes it again, so a direct SQL contender is serialized on the same original payment.
    private const string ApprovalGuards = """
        CREATE FUNCTION public.lock_refund_receipt(confirmation uuid) RETURNS void LANGUAGE sql
        SET search_path = pg_catalog AS $$
            SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('refund-receipt:' || confirmation::text, 0));
        $$;

        -- Held and (later) confirmed amounts count once per stable reservation identity.
        CREATE FUNCTION public.refund_receipt_occupied_paise(scope_account uuid, scope_confirmation uuid) RETURNS bigint
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT coalesce(sum(held.amount_paise), 0)::bigint FROM (
                SELECT DISTINCT ON (a.reservation_id) a.amount_paise FROM public.refund_approvals a
                WHERE a.account_id = scope_account AND a.confirmation_id = scope_confirmation
                ORDER BY a.reservation_id) held;
        $$;

        CREATE FUNCTION public.guard_refund_approval() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            prior bigint;
            occupied bigint;
        BEGIN
            PERFORM public.lock_refund_receipt(NEW.confirmation_id);
            IF NOT EXISTS (
                SELECT FROM public.refund_requests r
                JOIN public.payment_confirmations c ON c.confirmation_id = r.confirmation_id
                    AND c.account_id = r.account_id AND c.order_id = r.order_id
                WHERE r.account_id = NEW.account_id AND r.refund_request_id = NEW.refund_request_id
                    AND r.order_id = NEW.order_id AND r.confirmation_id = NEW.confirmation_id
                    AND r.attempt_id IS NOT DISTINCT FROM NEW.attempt_id
                    AND r.manual_receipt_id IS NOT DISTINCT FROM NEW.manual_receipt_id
                    AND r.payment_amount_paise = NEW.payment_amount_paise AND r.amount_paise = NEW.requested_amount_paise
                    AND r.currency = NEW.currency AND r.provider_id = NEW.provider_id
                    AND r.provider_account_id = NEW.provider_account_id AND r.environment = NEW.environment
                    AND r.configuration_version = NEW.configuration_version
                    AND (c.facts->'receipt'->>'amountPaise')::bigint = NEW.payment_amount_paise
                    AND coalesce(c.facts->'terms'->>'refundPolicyVersion', 'unrecorded') = NEW.refund_policy_version) THEN
                RAISE EXCEPTION 'Refund approval must match its request and the original verified payment'
                    USING ERRCODE = '23514';
            END IF;
            -- Read after the lock in a subsequent statement: an earlier READ COMMITTED snapshot can be stale.
            SELECT coalesce(max(a.receipt_revision), 0) INTO prior FROM public.refund_approvals a
                WHERE a.account_id = NEW.account_id AND a.confirmation_id = NEW.confirmation_id;
            SELECT public.refund_receipt_occupied_paise(NEW.account_id, NEW.confirmation_id) INTO occupied;
            IF NEW.receipt_revision <> prior + 1 OR NEW.remaining_before_paise <> NEW.payment_amount_paise - occupied
                OR NEW.amount_paise > NEW.remaining_before_paise THEN
                RAISE EXCEPTION 'Refund approval exceeds or misstates the verified receipt' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER refund_approval_guard BEFORE INSERT ON public.refund_approvals
            FOR EACH ROW EXECUTE FUNCTION public.guard_refund_approval();

        CREATE FUNCTION public.require_refund_access_decision() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        BEGIN
            IF (SELECT count(*) FROM public.refund_access_decisions d
                WHERE d.account_id = NEW.account_id AND d.approval_id = NEW.approval_id
                    AND d.refund_request_id = NEW.refund_request_id) <> 1 THEN
                RAISE EXCEPTION 'Refund approval requires its explicit access decision' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER refund_approval_complete AFTER INSERT ON public.refund_approvals
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_refund_access_decision();
        """;

    private const string DecisionGuards = """
        CREATE FUNCTION public.guard_refund_access_decision() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        DECLARE
            approval record;
            prior bigint;
        BEGIN
            SELECT a.order_id, a.attempt_id, a.manual_receipt_id INTO approval FROM public.refund_approvals a
                WHERE a.account_id = NEW.account_id AND a.approval_id = NEW.approval_id
                    AND a.refund_request_id = NEW.refund_request_id AND a.decided_at = NEW.decided_at
                    AND a.xmin = pg_current_xact_id()::xid;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'Access decision must be recorded with its refund approval' USING ERRCODE = '23514';
            END IF;
            IF NEW.term_id IS NOT NULL AND NOT EXISTS (
                SELECT FROM public.payment_fulfilments f
                WHERE f.account_id = NEW.account_id AND f.order_id = approval.order_id AND f.term_id = NEW.term_id
                    AND (f.attempt_id = approval.attempt_id OR f.manual_receipt_id = approval.manual_receipt_id)
            ) AND NOT EXISTS (
                SELECT FROM public.capacity_increases i
                WHERE i.account_id = NEW.account_id AND i.order_id = approval.order_id AND i.term_id = NEW.term_id
                    AND i.state = 'Applied'
                    AND (i.attempt_id = approval.attempt_id OR i.receipt_id = approval.manual_receipt_id)
            ) THEN
                RAISE EXCEPTION 'Access decision must name the term this payment funded' USING ERRCODE = '23514';
            END IF;
            IF NEW.treatment = 'Preserve' THEN
                RETURN NEW;
            END IF;
            SELECT coalesce(max(d.term_revision), 0) INTO prior FROM public.refund_access_decisions d
                WHERE d.account_id = NEW.account_id AND d.term_id = NEW.term_id;
            IF NEW.term_revision <> prior + 1 OR NOT EXISTS (
                SELECT FROM public.subscription_coverage_terms t
                JOIN public.subscription_term_facts f USING (account_id, term_id)
                WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND f.basis = 'Paid'
                    AND NEW.effective_at > t.starts_at AND NEW.previous_paid_through <= t.ends_at
            ) OR EXISTS (
                SELECT FROM public.payment_fulfilments s
                WHERE s.account_id = NEW.account_id AND s.predecessor_term_id = NEW.term_id
            ) THEN
                RAISE EXCEPTION 'Access decision must apply to the current unrenewed paid term' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER refund_access_decision_guard BEFORE INSERT ON public.refund_access_decisions
            FOR EACH ROW EXECUTE FUNCTION public.guard_refund_access_decision();
        """;
}
