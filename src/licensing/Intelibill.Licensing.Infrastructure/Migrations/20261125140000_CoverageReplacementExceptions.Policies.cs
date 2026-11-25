namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageReplacementExceptions
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";
    private const string Admin = "nullif(current_setting('app.exception_admin_id', true), '')::uuid";
    private const string AdminSession = "nullif(current_setting('app.exception_session_id', true), '')::uuid";
    private const string ShopScope = "nullif(current_setting('app.status_shop_id', true), '')::uuid";

    private const string Policies = $"""
        ALTER TABLE public.coverage_replacement_exceptions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_exceptions FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_exception_approvals ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_exception_approvals FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.paid_slot_handoffs ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.paid_slot_handoffs FORCE ROW LEVEL SECURITY;

        CREATE POLICY account_read ON public.coverage_replacement_exceptions FOR SELECT USING (account_id = {Account});
        CREATE POLICY owner_read ON public.coverage_replacement_exceptions FOR SELECT USING (
            exception_id = nullif(current_setting('app.coverage_exception_id', true), '')::uuid
            AND nullif(current_setting('app.affiliation_shop_id', true), '')::uuid IN (outgoing_shop_id, incoming_shop_id));
        CREATE POLICY owner_request ON public.coverage_replacement_exceptions FOR INSERT WITH CHECK (
            account_id = {Account} AND requester_id = {Actor} AND state = 'AwaitingConsents'
            AND outgoing_consent_event IS NULL AND incoming_consent_event IS NULL
            AND outgoing_revoke_event IS NULL AND incoming_revoke_event IS NULL AND reason IS NULL
            AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.account_id = coverage_replacement_exceptions.account_id
                    AND u.actor_id = coverage_replacement_exceptions.requester_id
                    AND u.action = 'billing.coverage-replacement-exception.request'
                    AND u.xmin = pg_current_xact_id()::xid));
        CREATE POLICY retire_obsolete ON public.coverage_replacement_exceptions FOR UPDATE
            USING (account_id = {Account} AND state IN ('AwaitingConsents', 'ReadyForReview'))
            WITH CHECK (state = 'Blocked' AND reason = 'SourceChanged' AND EXISTS (
                SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.account_id = coverage_replacement_exceptions.account_id
                    AND u.actor_id = {Actor}
                    AND u.action = 'billing.coverage-replacement-exception.request'
                    AND u.xmin = pg_current_xact_id()::xid)
                AND public.coverage_exception_stale(exception_id, nullif(current_setting('app.exception_evaluated_at', true), '')::timestamptz));
        CREATE POLICY consent_update ON public.coverage_replacement_exceptions FOR UPDATE
            USING (account_id = {Account})
            WITH CHECK (EXISTS (SELECT FROM public.entitlement_merchant_inbox i
                WHERE i.kind = 'CoverageReplacementExceptionConsent'
                    AND i.account_id = coverage_replacement_exceptions.account_id
                    AND i.event_id IN (outgoing_consent_event, outgoing_revoke_event, incoming_consent_event, incoming_revoke_event)
                    AND i.xmin = pg_current_xact_id()::xid
                    AND convert_from(i.payload, 'UTF8')::jsonb @> jsonb_build_object(
                        'exceptionId', exception_id, 'termId', term_id, 'proposalRevision', proposal_revision,
                        'termsDigest', terms_digest)));
        CREATE POLICY admin_prepare_update ON public.coverage_replacement_exceptions FOR UPDATE
            USING (account_id = {Account} AND {Admin} IS NOT NULL AND {AdminSession} IS NOT NULL)
            WITH CHECK (state IN ('Preparing', 'Refused') AND EXISTS (
                SELECT FROM public.coverage_replacement_exception_approvals a
                WHERE a.exception_id = coverage_replacement_exceptions.exception_id
                    AND a.account_id = coverage_replacement_exceptions.account_id
                    AND a.admin_id = {Admin} AND a.session_id = {AdminSession}
                    AND a.xmin = pg_current_xact_id()::xid));
        CREATE POLICY receipt_update ON public.coverage_replacement_exceptions FOR UPDATE
            USING (account_id = {Account})
            WITH CHECK (EXISTS (SELECT FROM public.entitlement_merchant_inbox i
                WHERE i.kind = 'CoverageReplacementExceptionReceipt'
                    AND i.account_id = coverage_replacement_exceptions.account_id
                    AND i.xmin = pg_current_xact_id()::xid
                    AND (convert_from(i.payload, 'UTF8')::jsonb->>'exceptionId')::uuid = coverage_replacement_exceptions.exception_id));

        CREATE POLICY admin_read ON public.coverage_replacement_exception_approvals FOR SELECT USING (
            account_id = {Account} AND ({Admin} IS NOT NULL OR EXISTS (
                SELECT FROM public.entitlement_merchant_inbox i
                WHERE i.account_id = coverage_replacement_exception_approvals.account_id
                    AND i.kind IN ('CoverageReplacementExceptionReceipt', 'CoverageReplacementExceptionConsent')
                    AND i.xmin = pg_current_xact_id()::xid)));
        CREATE POLICY admin_insert ON public.coverage_replacement_exception_approvals FOR INSERT WITH CHECK (
            account_id = {Account} AND admin_id = {Admin} AND session_id = {AdminSession} AND state = 'Preparing'
            AND EXISTS (SELECT FROM public.admin_sessions s JOIN public.admin_identities a ON a.id = s.admin_id
                WHERE s.id = coverage_replacement_exception_approvals.session_id
                    AND a.id = coverage_replacement_exception_approvals.admin_id
                    AND s.revoked_at IS NULL AND a.deactivated_at IS NULL AND a.mfa_enrolled_at IS NOT NULL
                    AND (a.capabilities & 4) = 4
                    AND s.mfa_verified_at = coverage_replacement_exception_approvals.mfa_verified_at));
        CREATE POLICY receipt_update ON public.coverage_replacement_exception_approvals FOR UPDATE
            USING (account_id = {Account})
            WITH CHECK (EXISTS (SELECT FROM public.entitlement_merchant_inbox i
                WHERE i.account_id = coverage_replacement_exception_approvals.account_id
                    AND i.kind IN ('CoverageReplacementExceptionReceipt', 'CoverageReplacementExceptionConsent')
                    AND i.xmin = pg_current_xact_id()::xid));

        CREATE POLICY account_read ON public.paid_slot_handoffs FOR SELECT USING (account_id = {Account});
        CREATE POLICY shop_read ON public.paid_slot_handoffs FOR SELECT USING ({ShopScope} IN (outgoing_shop_id, incoming_shop_id));
        CREATE POLICY owner_read ON public.paid_slot_handoffs FOR SELECT USING (
            exception_id = nullif(current_setting('app.coverage_exception_id', true), '')::uuid
            AND nullif(current_setting('app.affiliation_shop_id', true), '')::uuid IN (outgoing_shop_id, incoming_shop_id));
        CREATE POLICY receipt_insert ON public.paid_slot_handoffs FOR INSERT WITH CHECK (
            account_id = {Account} AND EXISTS (
                SELECT FROM public.coverage_replacement_exception_approvals a
                JOIN public.entitlement_merchant_inbox i
                    ON i.account_id = a.account_id AND i.event_id = a.consume_receipt_event
                    AND i.kind = 'CoverageReplacementExceptionReceipt'
                WHERE a.approval_id = paid_slot_handoffs.approval_id
                    AND a.exception_id = paid_slot_handoffs.exception_id
                    AND a.account_id = paid_slot_handoffs.account_id
                    AND a.state = 'Applied' AND a.xmin = pg_current_xact_id()::xid
                    AND i.xmin = pg_current_xact_id()::xid));

        CREATE TRIGGER retain_coverage_exception BEFORE DELETE OR TRUNCATE ON public.coverage_replacement_exceptions
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_coverage_exception_approval BEFORE DELETE OR TRUNCATE ON public.coverage_replacement_exception_approvals
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_paid_slot_handoff BEFORE UPDATE OR DELETE OR TRUNCATE ON public.paid_slot_handoffs
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private const string TransitionGuards = """
        CREATE FUNCTION public.guard_coverage_exception() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF (to_jsonb(NEW) - ARRAY['state','reason','outgoing_consent_event','outgoing_revoke_event','outgoing_actor_id',
                    'outgoing_authority_version','outgoing_active_members','outgoing_seat_hash','outgoing_consented_at',
                    'incoming_consent_event','incoming_revoke_event','incoming_actor_id','incoming_authority_version',
                    'incoming_active_members','incoming_seat_hash','incoming_consented_at','approved_at','applied_at'])
                IS DISTINCT FROM
               (to_jsonb(OLD) - ARRAY['state','reason','outgoing_consent_event','outgoing_revoke_event','outgoing_actor_id',
                    'outgoing_authority_version','outgoing_active_members','outgoing_seat_hash','outgoing_consented_at',
                    'incoming_consent_event','incoming_revoke_event','incoming_actor_id','incoming_authority_version',
                    'incoming_active_members','incoming_seat_hash','incoming_consented_at','approved_at','applied_at'])
                OR OLD.outgoing_consent_event IS NOT NULL AND NEW.outgoing_consent_event IS DISTINCT FROM OLD.outgoing_consent_event
                OR OLD.incoming_consent_event IS NOT NULL AND NEW.incoming_consent_event IS DISTINCT FROM OLD.incoming_consent_event
                OR OLD.outgoing_revoke_event IS NOT NULL AND NEW.outgoing_revoke_event IS DISTINCT FROM OLD.outgoing_revoke_event
                OR OLD.incoming_revoke_event IS NOT NULL AND NEW.incoming_revoke_event IS DISTINCT FROM OLD.incoming_revoke_event
                OR OLD.state IN ('Applied','Refused','Blocked')
                OR OLD.state = 'AwaitingConsents' AND NEW.state NOT IN ('AwaitingConsents','ReadyForReview','Blocked')
                OR OLD.state = 'ReadyForReview' AND NEW.state NOT IN ('Preparing','Refused','Blocked')
                OR OLD.state = 'Preparing' AND NEW.state NOT IN ('Consuming','Aborting','Refused','Blocked')
                OR OLD.state = 'Consuming' AND NEW.state NOT IN ('Applied','Refused','Blocked')
                OR OLD.state = 'Aborting' AND NEW.state NOT IN ('Refused','Blocked')
            THEN
                RAISE EXCEPTION 'Invalid coverage exception transition' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_coverage_exception BEFORE UPDATE ON public.coverage_replacement_exceptions
            FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_exception();

        CREATE FUNCTION public.guard_coverage_exception_approval() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF (to_jsonb(NEW) - ARRAY['consume_event','abort_event','state','outcome_reason','merchant_permit_count',
                    'merchant_max_permit_end','merchant_permit_digest','consume_receipt_event','courtesy_grant_id',
                    'courtesy_term_id','applied_at'])
                IS DISTINCT FROM
               (to_jsonb(OLD) - ARRAY['consume_event','abort_event','state','outcome_reason','merchant_permit_count',
                    'merchant_max_permit_end','merchant_permit_digest','consume_receipt_event','courtesy_grant_id',
                    'courtesy_term_id','applied_at'])
                OR OLD.consume_event IS NOT NULL AND NEW.consume_event IS DISTINCT FROM OLD.consume_event
                OR OLD.abort_event IS NOT NULL AND NEW.abort_event IS DISTINCT FROM OLD.abort_event
                OR OLD.state IN ('Applied','Refused','Aborted')
                OR OLD.state = 'Preparing' AND NEW.state NOT IN ('Consuming','Aborting','Refused')
                OR OLD.state = 'Consuming' AND NEW.state NOT IN ('Applied','Refused')
                OR OLD.state = 'Aborting' AND NEW.state NOT IN ('Aborted','Refused')
                OR NEW.state = 'Applied' AND (NEW.consume_event IS NULL OR NEW.consume_receipt_event IS NULL)
            THEN
                RAISE EXCEPTION 'Invalid coverage exception approval transition' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_coverage_exception_approval BEFORE UPDATE ON public.coverage_replacement_exception_approvals
            FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_exception_approval();

        CREATE FUNCTION public.guard_paid_slot_handoff() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            win_end timestamptz;
            expected_gen int;
            prev_holder uuid;
            prev_start timestamptz;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            PERFORM pg_advisory_xact_lock(hashtextextended('g' || least(NEW.outgoing_shop_id, NEW.incoming_shop_id)::text, 0));
            PERFORM pg_advisory_xact_lock(hashtextextended('g' || greatest(NEW.outgoing_shop_id, NEW.incoming_shop_id)::text, 0));

            SELECT count(*)::int + 1 INTO expected_gen
            FROM public.paid_slot_handoffs h
            WHERE h.account_id = NEW.account_id AND h.term_id = NEW.term_id
                AND h.slot_origin_shop_id = NEW.slot_origin_shop_id;
            IF NEW.slot_generation <> expected_gen THEN
                RAISE EXCEPTION 'Paid slot handoff generation mismatch' USING ERRCODE = '23514';
            END IF;

            SELECT r.shop_id, r.interval_starts_at INTO prev_holder, prev_start
            FROM public.term_paid_slot_intervals(NEW.account_id, NEW.term_id) r
            WHERE r.slot_origin_shop_id = NEW.slot_origin_shop_id AND r.is_final_holder;
            IF prev_holder IS DISTINCT FROM NEW.outgoing_shop_id OR prev_start IS DISTINCT FROM NEW.previous_starts_at THEN
                RAISE EXCEPTION 'Outgoing shop does not hold the target paid slot' USING ERRCODE = '23514';
            END IF;

            IF NEW.original_ends_at IS DISTINCT FROM public.term_effective_paid_through(NEW.account_id, NEW.term_id) THEN
                RAISE EXCEPTION 'Paid slot handoff end disagrees with effective paid-through' USING ERRCODE = '23514';
            END IF;

            win_end := public.term_effective_access_end(NEW.account_id, NEW.term_id);
            IF public.shop_has_conflicting_paid_coverage(NEW.account_id, NEW.incoming_shop_id, NULL, NEW.cutover_at, win_end) THEN
                RAISE EXCEPTION 'Incoming shop has conflicting paid coverage' USING ERRCODE = '23514';
            END IF;

            IF NEW.courtesy_grant_id IS NOT NULL AND NOT EXISTS (
                SELECT FROM public.complimentary_grants g
                JOIN public.complimentary_grant_shops s USING (grant_id, account_id, term_id)
                WHERE g.grant_id = NEW.courtesy_grant_id AND g.account_id = NEW.account_id
                    AND g.term_id = NEW.courtesy_term_id AND s.shop_id = NEW.incoming_shop_id
                    AND s.affiliation_request_id = NEW.incoming_affiliation_id
                    AND g.starts_at = NEW.courtesy_starts_at AND g.ends_at = NEW.cutover_at
            ) THEN
                RAISE EXCEPTION 'Linked courtesy grant does not match handoff' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_paid_slot_handoff BEFORE INSERT ON public.paid_slot_handoffs
            FOR EACH ROW EXECUTE FUNCTION public.guard_paid_slot_handoff();
        """;
}
