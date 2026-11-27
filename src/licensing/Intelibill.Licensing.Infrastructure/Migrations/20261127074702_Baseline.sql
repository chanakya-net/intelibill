-- Licensing schema baseline
-- Generated from pg_dump of the database the former migration chain produced.
-- Policies that named the migrating role are rebound to current_user when applied.
SET LOCAL check_function_bodies = false;
SET LOCAL client_min_messages = warning;

--
-- PostgreSQL database dump
--


-- Dumped from database version 17.9
-- Dumped by pg_dump version 17.9


--
-- Name: accept_billing_ownership_transfer(uuid, uuid, uuid, uuid, bytea, text, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.accept_billing_ownership_transfer(p_transfer uuid, p_member uuid, p_key uuid, p_request uuid, p_hash bytea, p_policy text, p_at timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
    actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
    proposal public.billing_ownership_proposals%ROWTYPE;
    recipient public.billing_account_members%ROWTYPE;
BEGIN
    IF NOT has_table_privilege(session_user, 'public.billing_ownership_acceptances', 'INSERT')
        OR NOT public.billing_transfer_has_proof(p_request, actor, NULL, 'billing.ownership-transfer.accept', p_at)
        THEN RETURN false;
    END IF;

    SELECT * INTO proposal FROM public.billing_ownership_proposals
    WHERE id = p_transfer AND account_id = account AND recipient_actor_id = actor
      AND created_at <= p_at AND p_at < expires_at AND responsibility_policy_version = p_policy;
    IF NOT FOUND OR EXISTS (SELECT FROM public.billing_ownership_acceptances WHERE transfer_id = p_transfer)
        OR proposal.recipient_memberships IS DISTINCT FROM public.billing_transfer_membership_snapshot(account, actor)
        THEN RETURN false;
    END IF;

    UPDATE public.billing_account_members SET role = 2, revision = revision + 1
    WHERE account_id = account AND id = proposal.owner_member_id AND merchant_user_id = proposal.owner_actor_id
      AND role = 1 AND status = 1 AND revision = proposal.owner_revision;
    IF NOT FOUND THEN RETURN false; END IF;

    SELECT * INTO recipient FROM public.billing_account_members
    WHERE account_id = account AND merchant_user_id = actor AND status = 1;
    IF FOUND THEN
        UPDATE public.billing_account_members SET role = 1, revision = revision + 1
        WHERE account_id = account AND id = recipient.id AND role = 2 AND status = 1 AND revision = recipient.revision;
        IF NOT FOUND THEN RETURN false; END IF;
    ELSE
        INSERT INTO public.billing_account_members
            (id, account_id, merchant_user_id, role, status, revision, created_at)
        VALUES (p_member, account, actor, 1, 1, 1, p_at);
    END IF;

    INSERT INTO public.billing_ownership_acceptances
        (transfer_id, account_id, recipient_actor_id, recipient_member_id, recipient_before_revision,
         recipient_after_revision, owner_after_revision, operation_key, request_id, request_hash, effective_at)
    VALUES (p_transfer, account, actor, coalesce(recipient.id, p_member), coalesce(recipient.revision, 0),
        coalesce(recipient.revision, 0) + 1, proposal.owner_revision + 1, p_key, p_request, p_hash, p_at)
    ON CONFLICT DO NOTHING;
    -- A false result always rolls back the delegating transaction, including any tentative role changes.
    RETURN FOUND;
END $$;



--
-- Name: refund_attempts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_attempts (
    reservation_id uuid NOT NULL,
    account_id uuid NOT NULL,
    approval_id uuid NOT NULL,
    decision_id uuid NOT NULL,
    refund_request_id uuid NOT NULL,
    confirmation_id uuid NOT NULL,
    order_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    provider_id text NOT NULL,
    provider_account_id text NOT NULL,
    environment text NOT NULL,
    configuration_version text NOT NULL,
    order_reference text NOT NULL,
    payment_reference text NOT NULL,
    affiliations jsonb NOT NULL,
    predecessor uuid,
    capacity_revision bigint DEFAULT 0 NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    payment_amount_paise bigint NOT NULL,
    CONSTRAINT refund_attempts_amount_paise_check CHECK ((amount_paise > 0)),
    CONSTRAINT refund_attempts_capacity_revision_check CHECK ((capacity_revision >= 0)),
    CONSTRAINT refund_attempts_check CHECK ((payment_amount_paise >= amount_paise)),
    CONSTRAINT refund_attempts_currency_check CHECK ((currency = 'INR'::text))
);

ALTER TABLE ONLY public.refund_attempts FORCE ROW LEVEL SECURITY;


--
-- Name: apply_confirmed_refund(public.refund_attempts, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.apply_confirmed_refund(a public.refund_attempts, at_time timestamp with time zone) RETURNS SETOF uuid
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE d public.refund_access_decisions; applied boolean;
BEGIN
    SELECT * INTO STRICT d FROM public.refund_access_decisions
    WHERE account_id=a.account_id AND decision_id=a.decision_id;
    applied := d.treatment='Preserve' OR EXISTS (
        SELECT FROM public.subscription_coverage_terms t
        LEFT JOIN LATERAL (SELECT * FROM public.applied_refund_decisions p WHERE p.account_id=t.account_id
            AND p.term_id=t.term_id AND p.term_revision IS NOT NULL ORDER BY p.term_revision DESC LIMIT 1) p ON true
        LEFT JOIN LATERAL (SELECT kind FROM public.scheduled_subscription_changes c WHERE c.account_id=t.account_id
            AND c.term_id=t.term_id ORDER BY revision DESC LIMIT 1) c ON true
        WHERE t.account_id=a.account_id AND t.term_id=d.term_id AND p.decision_id IS NOT DISTINCT FROM a.predecessor
            AND d.previous_paid_through=coalesce(p.new_paid_through,t.ends_at)
            AND d.previous_access_ends_at=coalesce(p.new_access_ends_at,CASE WHEN c.kind='ScheduleCancellation'
                THEN t.ends_at ELSE t.ends_at+interval '168 hours' END)
            AND NOT public.refund_term_committed(t.account_id,t.term_id)
            AND a.capacity_revision = coalesce((SELECT max(i.target_revision) FROM public.capacity_increases i
                WHERE i.account_id=t.account_id AND i.term_id=t.term_id AND i.state='Applied'), 0)
            AND a.affiliations=(SELECT coalesce(jsonb_object_agg(shop_id,request_id),'{}'::jsonb)
                FROM public.refund_target_shops(t.account_id,t.term_id,at_time)));
    INSERT INTO public.refund_applications VALUES(a.account_id,a.decision_id,
        CASE WHEN applied THEN 'Applied' ELSE 'NeedsReview' END,at_time) ON CONFLICT DO NOTHING;
    IF FOUND AND applied AND d.treatment<>'Preserve' THEN
        RETURN QUERY SELECT key::uuid FROM jsonb_object_keys(a.affiliations) key ORDER BY key;
    END IF;
END $$;


--
-- Name: billing_inviter_is_current(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.billing_inviter_is_current(p_invitation uuid) RETURNS boolean
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (
        SELECT FROM public.billing_account_invitations i
        JOIN public.billing_account_members issuer ON issuer.account_id = i.account_id
          AND issuer.merchant_user_id = i.issuer_actor_id AND issuer.revision = i.issuer_revision
          AND issuer.role = 1 AND issuer.status = 1
        WHERE has_table_privilege(session_user, 'public.billing_account_members', 'SELECT')
          AND i.id = p_invitation AND i.revision = 1
          AND i.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
          AND i.invited_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
        FOR SHARE OF issuer
    )
$$;


--
-- Name: billing_removal_owner(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.billing_removal_owner(p_account uuid, p_actor uuid) RETURNS integer
    LANGUAGE sql
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT revision FROM public.billing_account_members
    WHERE has_table_privilege(session_user, 'public.billing_account_members', 'SELECT')
      AND has_table_privilege(session_user, 'public.billing_member_removals', 'INSERT')
      AND account_id = p_account AND merchant_user_id = p_actor AND role = 1 AND status = 1
    FOR SHARE
$$;


--
-- Name: billing_roster(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.billing_roster(p_after uuid, p_target uuid) RETURNS TABLE(member_id uuid, role integer, revision integer, is_current boolean)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH scope AS (
        SELECT nullif(current_setting('app.billing_account_id', true), '')::uuid AS account_id,
               nullif(current_setting('app.billing_actor_id', true), '')::uuid AS actor_id
    )
    SELECT m.id, m.role, m.revision, m.merchant_user_id = s.actor_id
    FROM public.billing_account_members m CROSS JOIN scope s
    WHERE has_table_privilege(session_user, 'public.billing_account_members', 'SELECT')
      AND m.account_id = s.account_id AND (p_target IS NOT NULL OR m.status = 1) AND m.id > p_after
      AND (p_target IS NULL OR m.merchant_user_id = p_target)
      AND EXISTS (SELECT FROM public.billing_account_members caller
          WHERE caller.account_id = s.account_id AND caller.merchant_user_id = s.actor_id
            AND caller.status = 1 AND (p_target IS NULL OR caller.role = 1))
    ORDER BY m.id LIMIT 101
$$;


--
-- Name: billing_transfer_has_proof(uuid, uuid, uuid, text, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.billing_transfer_has_proof(p_request uuid, p_actor uuid, p_account uuid, p_action text, p_at timestamp with time zone) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
        WHERE u.request_id = p_request AND u.actor_id = p_actor
          AND u.account_id IS NOT DISTINCT FROM p_account AND u.action = p_action
          AND u.issued_at <= p_at AND p_at < u.expires_at
          AND u.xmin = pg_current_xact_id()::xid)
$$;


--
-- Name: billing_transfer_membership_snapshot(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.billing_transfer_membership_snapshot(p_account uuid, p_actor uuid) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT coalesce(jsonb_agg(jsonb_build_array(id, role, status, revision) ORDER BY id), '[]'::jsonb)
    FROM public.billing_account_members WHERE account_id = p_account AND merchant_user_id = p_actor
$$;


--
-- Name: capacity_slot_available(uuid, uuid, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.capacity_slot_available(account uuid, term uuid, original_capacity integer) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT public.pre_transfer_slot_available(account,term,original_capacity) AND
        coalesce((SELECT capacity FROM public.capacity_increases WHERE account_id=account AND term_id=term AND state='Applied'
            ORDER BY target_revision DESC LIMIT 1), original_capacity) >
            (SELECT count(*) FROM public.pre_transfer_paid_slot_intervals(account,term) WHERE is_final_holder)
$$;


--
-- Name: catalogue_manager_id(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.catalogue_manager_id() RETURNS uuid
    LANGUAGE sql STABLE
    AS $$
    SELECT admin.id FROM public.admin_identities admin
    WHERE admin.id = nullif(current_setting('app.catalogue_admin_id', true), '')::uuid
      AND admin.deactivated_at IS NULL AND admin.mfa_enrolled_at IS NOT NULL
      AND admin.capabilities & 4 = 4
$$;


--
-- Name: check_billing_active_owner(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.check_billing_active_owner() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    account uuid;
BEGIN
    IF TG_TABLE_NAME = 'billing_accounts' THEN
        account := NEW.id;
    ELSIF TG_OP = 'UPDATE' THEN
        IF NEW.account_id <> OLD.account_id THEN
            RAISE EXCEPTION 'Billing member account is immutable' USING ERRCODE = '23514';
        END IF;
        account := NEW.account_id;
    ELSE
        account := NEW.account_id;
    END IF;
    IF NOT EXISTS (SELECT FROM public.billing_account_members
                   WHERE account_id = account AND role = 1 AND status = 1) THEN
        RAISE EXCEPTION 'Billing account requires an active owner' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: check_billing_invitation_grant(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.check_billing_invitation_grant() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM public.billing_account_members m WHERE m.id = NEW.accepted_member_id
            AND m.account_id = NEW.account_id AND m.merchant_user_id = NEW.invited_actor_id
            AND m.role = 2 AND m.status = 1 AND m.revision = 1 AND m.created_at = NEW.accepted_at
    ) THEN
        RAISE EXCEPTION 'Billing invitation grant is missing' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: claim_entitlement_delivery(timestamp with time zone, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.claim_entitlement_delivery(p_now timestamp with time zone, p_lease uuid) RETURNS TABLE(event_id uuid, kind text, payload bytea, attempts integer)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH due AS MATERIALIZED (
        SELECT o.event_id, (o.attempts < 12 OR o.status = 'DeadLetter') AS claimable FROM public.entitlement_outbox o
        WHERE (o.status IN ('Pending', 'Leased', 'AwaitingAcknowledgement') OR o.status = 'DeadLetter' AND EXISTS (SELECT FROM public.entitlement_messages m
    WHERE m.event_id = o.event_id AND m.kind IN ('CapacityReserve','CapacityConsume','ReductionManifest','ReductionReserve','ReductionConsume','CoverageExceptionPrepare','CoverageExceptionConsume','CoverageExceptionAbort')) OR o.status = 'DeadLetter' AND EXISTS (SELECT FROM public.entitlement_messages m
    WHERE m.event_id = o.event_id AND (m.kind IN ('TransferPrepare','TransferConsume','TransferAbort')
        OR m.kind='Snapshot' AND EXISTS (SELECT FROM public.coverage_transfer_bindings b
            WHERE b.transfer_id=m.event_id AND b.destination_account_id=m.account_id AND b.shop_id=m.shop_id))))
            AND o.next_attempt_at <= p_now AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        ORDER BY o.next_attempt_at, o.event_id LIMIT 1 FOR UPDATE SKIP LOCKED
    ), claimed AS (
        UPDATE public.entitlement_outbox o
        SET status = CASE WHEN due.claimable THEN 'Leased' ELSE 'DeadLetter' END,
            lease_token = CASE WHEN due.claimable THEN p_lease END,
            lease_expires_at = CASE WHEN due.claimable THEN p_now + interval '30 seconds' END,
            next_attempt_at = p_now + interval '30 seconds',
            last_failure = CASE WHEN o.status = 'Leased' THEN 'LeaseExpired' ELSE o.last_failure END,
            attempts = least(o.attempts + 1, 12)
        FROM due WHERE o.event_id = due.event_id
        RETURNING o.event_id, o.attempts, o.status
    )
    SELECT c.event_id, m.kind, m.payload, c.attempts FROM claimed c
    JOIN public.entitlement_messages m USING (event_id) WHERE c.status = 'Leased'
$$;


--
-- Name: claim_gateway_inbox_event(timestamp with time zone, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.claim_gateway_inbox_event(p_now timestamp with time zone, p_token uuid) RETURNS TABLE(inbox_id uuid, kind text, order_reference text, payment_reference text, account_id uuid, attempt_id uuid, candidates integer, attempts integer)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH due AS MATERIALIZED (
        SELECT w.inbox_id FROM public.gateway_inbox_work w
        WHERE w.status IN ('Pending', 'Leased') AND w.next_attempt_at <= p_now
            AND (w.lease_expires_at IS NULL OR w.lease_expires_at <= p_now)
            AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND nullif(current_setting('app.status_shop_id', true), '') IS NULL AND nullif(current_setting('app.payment_attempt_id', true), '')::uuid IS NULL AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        ORDER BY w.next_attempt_at, w.inbox_id LIMIT 1 FOR UPDATE SKIP LOCKED
    ), resolved AS MATERIALIZED (
        SELECT due.inbox_id, e.kind, e.provider_order_reference, e.provider_payment_reference,
            m.candidates, CASE WHEN m.candidates = 1 THEN m.accounts[1] END AS account_id,
            CASE WHEN m.candidates = 1 THEN m.attempts[1] END AS attempt_id
        FROM due JOIN public.gateway_inbox_events e USING (inbox_id)
        CROSS JOIN LATERAL (
            SELECT count(*)::integer AS candidates, array_agg(a.account_id) AS accounts,
                array_agg(a.attempt_id) AS attempts
            FROM (
                SELECT a.account_id, a.attempt_id
                FROM public.payment_attempt_sessions s
                JOIN public.payment_attempts a USING (account_id, attempt_id)
                WHERE e.kind = 'PaymentCaptured' AND s.provider_order_reference = e.provider_order_reference
                    AND a.provider_id = e.provider_id AND a.provider_account_id = e.provider_account_id
                    AND a.environment = e.environment
                LIMIT 2
            ) a
        ) m
    ), leased AS (
        UPDATE public.gateway_inbox_work w SET
            attempts = least(w.attempts + 1, 12),
            status = CASE WHEN w.attempts < 12 THEN 'Leased' ELSE 'DeadLetter' END,
            lease_token = CASE WHEN w.attempts < 12 THEN p_token END,
            lease_expires_at = CASE WHEN w.attempts < 12 THEN p_now + interval '30 seconds' END,
            next_attempt_at = p_now + interval '30 seconds',
            last_failure = CASE WHEN w.status = 'Leased' THEN 'LeaseExpired' ELSE w.last_failure END,
            finished_at = CASE WHEN w.attempts = 12 THEN p_now END,
            account_id = r.account_id, attempt_id = r.attempt_id
        FROM resolved r WHERE w.inbox_id = r.inbox_id
        RETURNING w.inbox_id, w.lease_token, w.attempts
    )
    SELECT r.inbox_id, r.kind, r.provider_order_reference::text, r.provider_payment_reference::text,
        r.account_id, r.attempt_id, r.candidates, l.attempts
    FROM resolved r JOIN leased l USING (inbox_id) WHERE l.lease_token IS NOT NULL
$$;


--
-- Name: claim_pending_payment(timestamp with time zone, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.claim_pending_payment(p_now timestamp with time zone, p_token uuid) RETURNS TABLE(account_id uuid, attempt_id uuid)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH due AS (
        SELECT p.attempt_id FROM public.pending_payment_attempts p WHERE NOT p.completed
            AND p.next_attempt_at <= p_now AND (p.lease_expires_at IS NULL OR p.lease_expires_at <= p_now)
            AND (p.attempts < 12 OR p.lease_token IS NOT NULL)
            AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND nullif(current_setting('app.status_shop_id', true), '') IS NULL AND nullif(current_setting('app.payment_attempt_id', true), '')::uuid IS NULL AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        ORDER BY p.next_attempt_at, p.attempt_id LIMIT 1 FOR UPDATE SKIP LOCKED
    ), claimed AS (
        UPDATE public.pending_payment_attempts p SET attempts = least(p.attempts + 1, 12),
            lease_token = CASE WHEN p.attempts < 12 THEN p_token END,
            lease_expires_at = CASE WHEN p.attempts < 12 THEN p_now + interval '30 seconds' END
        FROM due WHERE p.attempt_id = due.attempt_id RETURNING p.account_id, p.attempt_id, p.lease_token
    )
    SELECT c.account_id, c.attempt_id FROM claimed c WHERE c.lease_token IS NOT NULL
$$;


--
-- Name: claim_refund(timestamp with time zone, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.claim_refund(at_time timestamp with time zone, claim uuid) RETURNS TABLE(reservation_id uuid, account_id uuid, order_id uuid, attempt_id uuid, provider_id text, provider_account_id text, environment text, configuration_version text, payment_reference text, order_reference text, amount_paise bigint, payment_amount_paise bigint, currency text, sent boolean, refund_reference text, candidate_reference text)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH due AS (
        SELECT w.reservation_id FROM public.refund_work w WHERE w.due_at <= at_time
            AND (w.lease_until IS NULL OR w.lease_until <= at_time) AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL AND nullif(current_setting('app.billing_actor_id',true),'') IS NULL
 AND nullif(current_setting('app.manual_admin_id',true),'') IS NULL
 AND nullif(current_setting('app.status_shop_id',true),'') IS NULL
 AND nullif(current_setting('app.payment_attempt_id',true),'') IS NULL
        ORDER BY w.due_at,w.reservation_id LIMIT 1 FOR UPDATE SKIP LOCKED
    ), leased AS (
        UPDATE public.refund_work w SET token=claim, wake_pending=false, lease_until=at_time+interval '60 seconds'
            FROM due WHERE w.reservation_id=due.reservation_id RETURNING w.reservation_id,w.refund_reference,w.candidate_reference
    )
    SELECT a.reservation_id,a.account_id,a.order_id,a.attempt_id,a.provider_id,a.provider_account_id,
        a.environment,a.configuration_version,a.payment_reference,a.order_reference,a.amount_paise,
        a.payment_amount_paise,a.currency,s.reservation_id IS NOT NULL,l.refund_reference,l.candidate_reference
    FROM leased l JOIN public.refund_attempts a USING(reservation_id) LEFT JOIN public.refund_sends s USING(reservation_id)
$$;


--
-- Name: payment_fulfilments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_fulfilments (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    attempt_id uuid,
    term_id uuid NOT NULL,
    policy_version text NOT NULL,
    fulfilled_at timestamp with time zone NOT NULL,
    manual_receipt_id uuid,
    predecessor_term_id uuid,
    payment_effective_at timestamp with time zone,
    grace_days_included integer,
    renewal_policy_version text,
    CONSTRAINT payment_fulfilment_one_source CHECK (((attempt_id IS NULL) <> (manual_receipt_id IS NULL))),
    CONSTRAINT payment_fulfilment_renewal_evidence CHECK ((((predecessor_term_id IS NULL) AND (payment_effective_at IS NULL) AND (grace_days_included IS NULL) AND (renewal_policy_version IS NULL)) OR ((predecessor_term_id IS NOT NULL) AND (payment_effective_at IS NOT NULL) AND (grace_days_included IS NOT NULL) AND (renewal_policy_version IS NOT NULL) AND (predecessor_term_id <> term_id)))),
    CONSTRAINT payment_fulfilments_fulfilled_at_check CHECK (isfinite(fulfilled_at)),
    CONSTRAINT payment_fulfilments_grace_days_included_check CHECK (((grace_days_included >= 0) AND (grace_days_included <= 7))),
    CONSTRAINT payment_fulfilments_payment_effective_at_check CHECK (isfinite(payment_effective_at)),
    CONSTRAINT payment_fulfilments_policy_version_check CHECK ((policy_version = 'payment-fulfilment/1'::text)),
    CONSTRAINT payment_fulfilments_renewal_policy_version_check CHECK ((renewal_policy_version = 'renewal/1'::text))
);

ALTER TABLE ONLY public.payment_fulfilments FORCE ROW LEVEL SECURITY;


--
-- Name: complete_fresh_start(public.payment_fulfilments); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.complete_fresh_start(p public.payment_fulfilments) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT CASE WHEN f.order_id IS NULL THEN h.order_id IS NULL AND q.snapshot->>'startRule' = 'PriorPaidThrough'
        ELSE f.term_id = p.term_id AND f.ready_at = p.fulfilled_at
            AND f.effective_at = coalesce(v.captured_at, r.received_at)
            AND (h.order_id IS NULL OR (h.attempt_id IS NOT DISTINCT FROM p.attempt_id
                AND h.manual_receipt_id IS NOT DISTINCT FROM p.manual_receipt_id
                AND f.ready_at >= h.held_at AND (h.restriction IS NULL OR h.restriction = 'Unrestricted'
                    OR h.restriction IN ('Trading', 'Security')
                        AND coalesce(f.released_revision >= h.account_revision, false))))
            AND t.period_ordinal = 0 AND t.billing_period = q.snapshot->>'period'
            AND c.starts_at = (CASE WHEN q.snapshot->'freshStart' IS NULL
                THEN date_trunc('second', f.ready_at)
                ELSE date_trunc('second', f.ready_at) + CASE WHEN date_trunc('second', f.ready_at) = f.ready_at
                    THEN interval '0 seconds' ELSE interval '1 second' END END)
            AND t.anchor_local = c.starts_at AT TIME ZONE 'Asia/Kolkata'
            AND c.ends_at = (t.anchor_local + CASE WHEN t.billing_period = 'Month'
                THEN interval '1 month' ELSE interval '1 year' END) AT TIME ZONE 'Asia/Kolkata'
            AND (CASE f.kind WHEN 'RestoredAccess' THEN q.snapshot->>'startRule' = 'RestoredAccess'
                AND p.predecessor_term_id IS NOT NULL AND p.payment_effective_at = f.effective_at
                WHEN 'TrialConversion' THEN q.snapshot->>'startRule' = 'Fulfilment' AND p.predecessor_term_id IS NULL
                    AND (q.snapshot->'freshStart'->>'trialShopCount')::int > 0
                ELSE q.snapshot->>'startRule' = 'Fulfilment' AND p.predecessor_term_id IS NULL
                    AND coalesce((q.snapshot->'freshStart'->>'trialShopCount')::int, 0) = 0 END)
            AND coalesce((q.snapshot->'freshStart'->>'trialShopCount')::int, 0) =
                (SELECT count(*) FROM public.payment_trial_conversions x
                WHERE x.account_id = p.account_id AND x.order_id = p.order_id AND x.paid_starts_at = c.starts_at)
        END
    FROM public.checkout_orders q
    JOIN public.subscription_term_facts t ON t.account_id = p.account_id AND t.term_id = p.term_id
    JOIN public.subscription_coverage_terms c ON c.account_id = p.account_id AND c.term_id = p.term_id
    LEFT JOIN public.paid_fresh_starts f ON f.account_id = q.account_id AND f.order_id = q.order_id
    LEFT JOIN public.payment_restoration_holds h ON h.account_id = q.account_id AND h.order_id = q.order_id
    LEFT JOIN public.verified_payments v ON v.attempt_id = p.attempt_id
    LEFT JOIN public.manual_payment_receipts r ON r.receipt_id = p.manual_receipt_id
    WHERE q.account_id = p.account_id AND q.order_id = p.order_id
$$;


--
-- Name: complete_pending_payment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.complete_pending_payment() RETURNS void
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    UPDATE public.pending_payment_attempts p SET completed = true, lease_token = NULL, lease_expires_at = NULL
    WHERE p.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid AND p.attempt_id = nullif(current_setting('app.payment_attempt_id', true), '')::uuid AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND NOT p.completed
        AND (EXISTS (SELECT FROM public.verified_payments v WHERE v.attempt_id = p.attempt_id)
            OR EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = p.attempt_id AND e.kind = 'CheckoutFailed'))
$$;


--
-- Name: coverage_exception_leg_activation(uuid, text, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_exception_leg_activation(id uuid, leg text, at_time timestamp with time zone) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    r public.coverage_replacement_exceptions%ROWTYPE;
    h public.paid_slot_handoffs%ROWTYPE;
    old_account text := current_setting('app.billing_account_id', true);
    target_shop uuid;
    target_aff uuid;
    outcome text;
BEGIN
    SELECT * INTO r FROM public.coverage_replacement_exceptions WHERE exception_id = id;
    IF NOT FOUND THEN RETURN 'NotApplicable'; END IF;
    IF leg = 'IncomingCourtesy' AND r.overlap_disposition <> 'CourtesyBridge' THEN RETURN 'NotApplicable'; END IF;
    IF r.state <> 'Applied' THEN RETURN 'Pending'; END IF;
    PERFORM set_config('app.billing_account_id', r.account_id::text, true);
    SELECT * INTO h FROM public.paid_slot_handoffs WHERE exception_id = id AND account_id = r.account_id;
    IF NOT FOUND THEN
        PERFORM set_config('app.billing_account_id', coalesce(old_account, ''), true);
        RETURN 'Pending';
    END IF;
    IF leg = 'IncomingCourtesy' AND h.courtesy_grant_id IS NULL THEN
        PERFORM set_config('app.billing_account_id', coalesce(old_account, ''), true);
        RETURN 'NotApplicable';
    END IF;
    IF leg = 'IncomingCourtesy' AND at_time < h.courtesy_starts_at THEN
        PERFORM set_config('app.billing_account_id', coalesce(old_account, ''), true);
        RETURN 'Waiting';
    END IF;
    IF leg = 'IncomingPaid' AND at_time < h.cutover_at THEN
        PERFORM set_config('app.billing_account_id', coalesce(old_account, ''), true);
        RETURN 'Waiting';
    END IF;
    target_shop := CASE WHEN leg = 'Outgoing' THEN h.outgoing_shop_id ELSE h.incoming_shop_id END;
    target_aff := CASE WHEN leg = 'Outgoing' THEN h.outgoing_affiliation_id ELSE h.incoming_affiliation_id END;

    SELECT CASE
        WHEN o.status IN ('NeedsReview', 'DeadLetter') THEN 'NeedsReview'
        WHEN snap->'shop'->>'state' = 'Closed' THEN 'Closed'
        WHEN (snap->'shop'->'suspension'->>'effectiveAt')::timestamptz <= at_time
            OR (snap->'account'->'suspension'->>'effectiveAt')::timestamptz <= at_time THEN 'Suspended'
        WHEN EXISTS (
            SELECT FROM public.entitlement_snapshots es
            JOIN public.entitlement_messages em USING (event_id, account_id, shop_id)
            JOIN public.applied_entitlement_shop_receipts a
                ON a.account_id = es.account_id AND a.shop_id = es.shop_id
                AND a.affiliation_request_id = es.affiliation_request_id AND a.shop_revision = es.shop_revision
            JOIN public.entitlement_merchant_inbox i
                ON i.event_id = es.event_id AND i.account_id = es.account_id AND i.kind = 'Acknowledgement'
            CROSS JOIN LATERAL (SELECT convert_from(em.payload, 'UTF8')::jsonb AS msg,
                convert_from(i.payload, 'UTF8')::jsonb AS ack) x
            WHERE es.account_id = r.account_id AND es.shop_id = target_shop AND es.affiliation_request_id = target_aff
                AND x.msg->'shop'->>'replacementExceptionId' = id::text
                AND x.msg->'shop'->>'replacementExceptionLeg' = leg
                AND (x.msg->'shop'->>'replacementExceptionGeneration')::bigint = h.generation
                AND x.ack->>'outcome' = 'Applied' AND x.ack->>'disposition' = 'Applied'
                AND (x.ack->>'appliedShopRevision')::bigint = es.shop_revision
                AND x.ack->>'digest' = em.digest
                AND x.ack->>'activatedExceptionId' = id::text
                AND x.ack->>'activatedExceptionLeg' = leg
                AND (x.ack->>'activatedExceptionGeneration')::bigint = h.generation)
            THEN CASE
                WHEN leg = 'IncomingCourtesy' AND at_time >= h.cutover_at THEN 'TermEnded'
                WHEN leg = 'IncomingPaid' AND at_time >= public.term_effective_access_end(r.account_id, r.term_id) THEN 'TermEnded'
                ELSE 'Acknowledged' END
        ELSE 'Applying' END INTO outcome
    FROM public.entitlement_snapshots s
    JOIN public.entitlement_messages m USING (event_id)
    JOIN public.entitlement_outbox o USING (event_id)
    CROSS JOIN LATERAL (SELECT convert_from(m.payload, 'UTF8')::jsonb AS snap) j
    WHERE s.account_id = r.account_id AND s.shop_id = target_shop AND s.affiliation_request_id = target_aff
    ORDER BY s.shop_revision DESC LIMIT 1;

    PERFORM set_config('app.billing_account_id', coalesce(old_account, ''), true);
    RETURN coalesce(outcome, 'Applying');
END $$;


--
-- Name: coverage_exception_owner_current(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_exception_owner_current(ac uuid, id uuid) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    r public.coverage_replacement_exceptions%ROWTYPE;
    previous_actor text := current_setting('app.billing_actor_id', true);
    authorized bool;
BEGIN
    SELECT * INTO r FROM public.coverage_replacement_exceptions WHERE account_id = ac AND exception_id = id;
    IF NOT FOUND THEN RETURN false; END IF;
    PERFORM set_config('app.billing_actor_id', r.requester_id::text, true);
    SELECT EXISTS (SELECT FROM public.billing_account_members m WHERE m.account_id = ac AND m.status = 1
        AND m.role = 1 AND m.revision = r.requester_revision AND m.merchant_user_id = r.requester_id) INTO authorized;
    PERFORM set_config('app.billing_actor_id', coalesce(previous_actor, ''), true);
    RETURN authorized;
END $$;


--
-- Name: coverage_exception_stale(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_exception_stale(id uuid, at_time timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    r public.coverage_replacement_exceptions%ROWTYPE;
    previous_account text := current_setting('app.billing_account_id', true);
    stale bool;
BEGIN
    SELECT * INTO r FROM public.coverage_replacement_exceptions WHERE exception_id = id;
    IF NOT FOUND OR r.state NOT IN ('AwaitingConsents', 'ReadyForReview') THEN RETURN false; END IF;
    PERFORM set_config('app.billing_account_id', r.account_id::text, true);
    SELECT r.expires_at <= at_time OR r.paid_cutover_at <= at_time OR r.paid_cutover_at >= r.normal_renewal_at
        OR r.outgoing_revoke_event IS NOT NULL OR r.incoming_revoke_event IS NOT NULL
        OR NOT public.coverage_exception_owner_current(r.account_id, r.exception_id)
        OR NOT public.replacement_term_available(r.account_id, r.term_id)
        OR NOT EXISTS (SELECT FROM public.term_effective_roster(r.account_id, r.term_id, NULL) tr
            WHERE tr.shop_id = r.outgoing_shop_id AND tr.affiliation_request_id = r.outgoing_affiliation_id)
        OR NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id = r.account_id
            AND a.shop_id = r.incoming_shop_id AND a.request_id = r.incoming_affiliation_id)
        OR public.shop_has_conflicting_paid_coverage(r.account_id, r.incoming_shop_id, NULL, r.paid_cutover_at,
            public.term_effective_access_end(r.account_id, r.term_id))
        OR EXISTS (SELECT FROM public.active_operational_closure_targets c WHERE c.account_id = r.account_id
            AND c.shop_id IN (r.outgoing_shop_id, r.incoming_shop_id))
        OR r.normal_renewal_at IS DISTINCT FROM public.term_effective_paid_through(r.account_id, r.term_id)
        OR (r.overlap_disposition = 'CourtesyBridge' AND EXISTS (SELECT FROM public.complimentary_grant_shops g
            WHERE g.shop_id = r.incoming_shop_id AND g.starts_at < r.paid_cutover_at
                AND r.requested_early_start_at < g.ends_at))
        OR EXISTS (SELECT FROM (SELECT i.capacity, i.members FROM public.capacity_increases i
            WHERE i.account_id = r.account_id AND i.term_id = r.term_id AND i.state = 'Applied'
            ORDER BY i.target_revision DESC LIMIT 1) adj
            WHERE adj.capacity <> r.capacity OR adj.members <> r.member_allowance)
        OR (public.replacement_permit_end(r.account_id, r.outgoing_shop_id, r.outgoing_affiliation_id, at_time)
            IS NOT NULL AND (r.observed_permit_deadline IS NULL
                OR public.replacement_permit_end(r.account_id, r.outgoing_shop_id, r.outgoing_affiliation_id, at_time) > r.paid_cutover_at
                OR public.replacement_permit_end(r.account_id, r.outgoing_shop_id, r.outgoing_affiliation_id, at_time) > r.observed_permit_deadline))
        INTO stale;
    PERFORM set_config('app.billing_account_id', coalesce(previous_account, ''), true);
    RETURN stale;
END $$;


--
-- Name: coverage_transfer_account_exists(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_account_exists(ac uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT has_table_privilege(session_user, 'public.coverage_transfer_proposals', 'INSERT')
        AND EXISTS (SELECT FROM public.billing_accounts a WHERE a.id = ac)
$$;


--
-- Name: coverage_transfer_blocker(uuid, timestamp with time zone, timestamp with time zone, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_blocker(id uuid, at_time timestamp with time zone, merchant_at timestamp with time zone DEFAULT NULL::timestamp with time zone, merchant_digest text DEFAULT NULL::text) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE previous text:=current_setting('app.billing_account_id',true); p public.coverage_transfer_proposals%ROWTYPE;
    result text:=public.original_transfer_blocker(id,at_time,merchant_at,merchant_digest);
BEGIN
    IF result IS NOT NULL THEN RETURN result; END IF;
    SELECT * INTO STRICT p FROM public.coverage_transfer_proposals WHERE transfer_id=id;
    PERFORM set_config('app.billing_account_id',p.source_account_id::text,true);
    SELECT CASE
        WHEN p.funding_digest IS NOT NULL AND p.funding_digest IS DISTINCT FROM public.transfer_funding_digest(
            p.source_account_id,p.source_term_id,p.shop_id,p.source_affiliation_id) THEN 'FundingChanged'
        WHEN p.funding_digest IS NULL AND (EXISTS(SELECT FROM public.capacity_increases
            WHERE account_id=p.source_account_id AND term_id=p.source_term_id AND state='Applied')
            OR EXISTS(SELECT FROM public.paid_slot_handoffs WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                AND p.shop_id IN (outgoing_shop_id,incoming_shop_id))) THEN 'AllocationRequiresNewConsent'
        WHEN EXISTS(SELECT FROM public.capacity_increases WHERE account_id=p.source_account_id AND term_id=p.source_term_id
            AND state='Pending') OR EXISTS(SELECT FROM public.scheduled_capacity_reductions WHERE account_id=p.source_account_id
            AND term_id=p.source_term_id AND state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview'))
            OR EXISTS(SELECT FROM public.coverage_replacements WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                AND outgoing_shop_id=p.shop_id AND state IN ('AwaitingConsent','Scheduled'))
            OR EXISTS(SELECT FROM public.coverage_replacement_exceptions WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                AND outgoing_shop_id=p.shop_id AND state IN ('AwaitingConsents','ReadyForReview','Preparing','Consuming','Aborting'))
            THEN 'CompetingCoverageChange'
    END INTO result;
    PERFORM set_config('app.billing_account_id',coalesce(previous,''),true);
    RETURN result;
END $$;


--
-- Name: coverage_transfer_evidence_digest(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_evidence_digest(source uuid, destination uuid) RETURNS text
    LANGUAGE sql
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT encode(sha256(convert_to(
        coalesce((SELECT jsonb_agg(to_jsonb(s) ORDER BY s.term_id) FROM public.coverage_transfer_party_terms(source) s), '[]')::text
        || '|' ||
        coalesce((SELECT jsonb_agg(to_jsonb(d) ORDER BY d.term_id) FROM public.coverage_transfer_party_terms(destination) d), '[]')::text,
        'UTF8')), 'hex')
$$;


--
-- Name: coverage_transfer_freeze_evidence(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_freeze_evidence(id uuid) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    r public.coverage_transfer_proposals%ROWTYPE;
    previous text := current_setting('app.billing_account_id', true);
    party record;
BEGIN
    SELECT * INTO STRICT r FROM public.coverage_transfer_proposals WHERE transfer_id = id;
    FOR party IN SELECT * FROM (VALUES ('Source', r.source_account_id), ('Destination', r.destination_account_id)) v(side, ac) LOOP
        PERFORM set_config('app.billing_account_id', party.ac::text, true);
        INSERT INTO public.coverage_transfer_evidence (transfer_id, side, account_id, term_id, mode, capacity,
            starts_at, ends_at, paid_through_at, access_ends_at, recorded_at)
        SELECT id, party.side, party.ac, t.term_id, t.mode, t.capacity, t.starts_at, t.ends_at,
            t.paid_through_at, t.access_ends_at, r.created_at
        FROM public.coverage_transfer_party_terms(party.ac) t;
    END LOOP;
    PERFORM set_config('app.billing_account_id', coalesce(previous, ''), true);
END $$;


--
-- Name: coverage_transfer_owner_current(uuid, uuid, uuid, bigint); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_owner_current(ac uuid, actor uuid, member uuid, rev bigint) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    previous_account text := current_setting('app.billing_account_id', true);
    previous_actor text := current_setting('app.billing_actor_id', true);
    authorized bool;
BEGIN
    PERFORM set_config('app.billing_account_id', ac::text, true), set_config('app.billing_actor_id', actor::text, true);
    SELECT EXISTS (SELECT FROM public.billing_account_members m WHERE m.id = member AND m.account_id = ac
        AND m.merchant_user_id = actor AND m.role = 1 AND m.status = 1 AND m.revision = rev) INTO authorized;
    PERFORM set_config('app.billing_account_id', coalesce(previous_account, ''), true),
        set_config('app.billing_actor_id', coalesce(previous_actor, ''), true);
    RETURN authorized;
END $$;


--
-- Name: coverage_transfer_participant_state(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_participant_state(id uuid) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE previous text:=current_setting('app.billing_account_id',true); source uuid; result text;
BEGIN
    SELECT source_account_id INTO source FROM public.coverage_transfer_proposals WHERE transfer_id=id;
    IF NOT FOUND THEN RETURN NULL; END IF;
    PERFORM set_config('app.billing_account_id',source::text,true);
    SELECT CASE WHEN p.state='PendingActivation' THEN public.transfer_activation_state(id)
        ELSE coalesce(p.state,'Preparing') END INTO result
    FROM public.coverage_transfer_approvals a LEFT JOIN LATERAL (
        SELECT state FROM public.coverage_transfer_progress WHERE transfer_id=id ORDER BY sequence DESC LIMIT 1
    ) p ON true WHERE a.transfer_id=id;
    PERFORM set_config('app.billing_account_id',coalesce(previous,''),true);
    RETURN result;
END $$;


--
-- Name: coverage_transfer_party_terms(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_party_terms(ac uuid) RETURNS TABLE(term_id uuid, mode text, capacity integer, starts_at timestamp with time zone, ends_at timestamp with time zone, paid_through_at timestamp with time zone, access_ends_at timestamp with time zone)
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE previous text := current_setting('app.billing_account_id', true);
BEGIN
    PERFORM set_config('app.billing_account_id', ac::text, true);
    RETURN QUERY SELECT t.term_id, t.mode, t.capacity, t.starts_at, t.ends_at,
            public.term_effective_paid_through(ac, t.term_id), public.term_effective_access_end(ac, t.term_id)
        FROM public.subscription_coverage_terms t WHERE t.account_id = ac ORDER BY t.term_id;
    PERFORM set_config('app.billing_account_id', coalesce(previous, ''), true);
END $$;


--
-- Name: coverage_transfer_permit_digest(uuid, uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_permit_digest(ac uuid, shop uuid, affiliation uuid, at_time timestamp with time zone) RETURNS text
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT encode(sha256(convert_to(coalesce(string_agg(
        permit_id::text || '/' || extract(epoch FROM not_before)::text || '/' || extract(epoch FROM valid_until)::text,
        ',' ORDER BY permit_id), ''), 'UTF8')), 'hex')
    FROM public.offline_device_observations
    WHERE account_id = ac AND shop_id = shop AND affiliation_request_id = affiliation
        AND permit_id IS NOT NULL AND valid_until > at_time
$$;


--
-- Name: due_entitlement_verification(timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.due_entitlement_verification(p_now timestamp with time zone) RETURNS TABLE(account_id uuid, shop_id uuid)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT a.account_id,a.shop_id FROM public.current_shop_affiliations a LEFT JOIN public.entitlement_shop_heads h USING(account_id,shop_id)
    WHERE (h.shop_id IS NULL OR h.next_verification_at<=p_now) AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
    ORDER BY h.next_verification_at NULLS FIRST,a.shop_id LIMIT 20
$$;


--
-- Name: due_scheduled_capacity_reductions(timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.due_scheduled_capacity_reductions(p_now timestamp with time zone) RETURNS TABLE(account_id uuid, schedule_id uuid)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT r.account_id, r.schedule_id
    FROM public.scheduled_capacity_reductions r
    WHERE nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        AND r.state = 'Funded' AND r.effective_at <= p_now
    ORDER BY r.effective_at, r.schedule_id LIMIT 50
$$;


--
-- Name: due_transfer_applications(timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.due_transfer_applications(p_now timestamp with time zone) RETURNS TABLE(source_account_id uuid, destination_account_id uuid, transfer_id uuid)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT a.source_account_id,a.destination_account_id,a.transfer_id FROM public.coverage_transfer_approvals a
    WHERE a.cutover_at<=p_now AND (SELECT p.state FROM public.coverage_transfer_progress p WHERE p.transfer_id=a.transfer_id
        ORDER BY p.sequence DESC LIMIT 1)='Scheduled' AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
    ORDER BY a.cutover_at,a.transfer_id LIMIT 20
$$;


--
-- Name: finish_entitlement_delivery(uuid, uuid, timestamp with time zone, boolean, boolean, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamp with time zone, p_success boolean, p_wait_ack boolean, p_failure text) RETURNS void
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    UPDATE public.entitlement_outbox o
SET status='Pending', attempts=attempts-1, last_failure=NULL, lease_token=NULL, lease_expires_at=NULL,
    next_attempt_at=p_now + interval '1 minute'
WHERE p_failure='Coverage.AwaitingOutgoingRelease' AND NOT p_success AND p_wait_ack
    AND o.event_id=p_event AND o.status='Leased' AND o.lease_token=p_lease AND o.lease_expires_at>p_now
    AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
    AND EXISTS (SELECT FROM public.entitlement_messages m WHERE m.event_id=o.event_id AND m.kind='Snapshot'
        AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementId' IS NOT NULL);
UPDATE public.entitlement_outbox o
SET status='Pending', attempts=attempts-1, last_failure=NULL, lease_token=NULL, lease_expires_at=NULL,
    next_attempt_at=p_now + interval '1 minute'
WHERE p_failure='Coverage.ExceptionAwaitingOutgoingRelease' AND NOT p_success AND p_wait_ack
    AND o.event_id=p_event AND o.status='Leased' AND o.lease_token=p_lease AND o.lease_expires_at>p_now
    AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
    AND EXISTS (SELECT FROM public.entitlement_messages m WHERE m.event_id=o.event_id AND m.kind='Snapshot'
        AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementExceptionId' IS NOT NULL);
    UPDATE public.entitlement_outbox
    SET status = CASE WHEN p_success AND NOT p_wait_ack THEN 'Delivered'
            WHEN attempts >= 12 THEN 'DeadLetter'
            WHEN p_success THEN 'AwaitingAcknowledgement' ELSE 'Pending' END,
        lease_token = NULL, lease_expires_at = NULL,
        next_attempt_at = p_now + make_interval(secs => least(300, (2 << least(attempts, 8)))),
        last_failure = CASE WHEN p_success AND p_wait_ack THEN 'AcknowledgementMissing'
            WHEN p_success THEN NULL ELSE p_failure END
    WHERE event_id = p_event AND status = 'Leased' AND lease_token = p_lease AND lease_expires_at > p_now AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
$$;


--
-- Name: finish_gateway_inbox_event(uuid, uuid, timestamp with time zone, text, text, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.finish_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamp with time zone, p_outcome text, p_failure text, p_dead boolean) RETURNS boolean
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH finished AS (
        UPDATE public.gateway_inbox_work w SET
            status = CASE WHEN p_outcome IS NOT NULL THEN 'Processed'
                          WHEN p_dead OR w.attempts = 12 THEN 'DeadLetter' ELSE 'Pending' END,
            outcome = p_outcome, last_failure = p_failure, lease_token = NULL, lease_expires_at = NULL,
            next_attempt_at = p_now + make_interval(secs => least(300, (2 << least(w.attempts, 8)))),
            finished_at = CASE WHEN p_outcome IS NOT NULL OR p_dead OR w.attempts = 12 THEN p_now END
        WHERE w.inbox_id = p_inbox AND w.status = 'Leased' AND w.lease_token = p_token
            AND w.lease_expires_at > p_now
            AND ((w.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid AND w.attempt_id = nullif(current_setting('app.payment_attempt_id', true), '')::uuid AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL) OR (nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND nullif(current_setting('app.status_shop_id', true), '') IS NULL AND nullif(current_setting('app.payment_attempt_id', true), '')::uuid IS NULL AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL AND (p_outcome IS NULL OR p_outcome = 'NoAction')))
        RETURNING w.inbox_id
    )
    SELECT EXISTS (SELECT FROM finished)
$$;


--
-- Name: finish_pending_payment(uuid, timestamp with time zone, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.finish_pending_payment(p_token uuid, p_now timestamp with time zone, p_complete boolean) RETURNS void
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    UPDATE public.pending_payment_attempts p SET completed = p_complete, lease_token = NULL, lease_expires_at = NULL,
        next_attempt_at = p_now + make_interval(secs => least(300, (2 << least(p.attempts, 8))))
    WHERE p.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid AND p.attempt_id = nullif(current_setting('app.payment_attempt_id', true), '')::uuid AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL
        AND p.lease_token = p_token AND p.lease_expires_at > p_now
$$;


--
-- Name: finish_refund(uuid, timestamp with time zone, text, text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.finish_refund(claim uuid, at_time timestamp with time zone, outcome text, detail text, remote text) RETURNS SETOF uuid
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE a public.refund_attempts; previous text; bound text; sent boolean; read_deferred boolean;
BEGIN
    SELECT r.* INTO a FROM public.refund_work w JOIN public.refund_attempts r USING(reservation_id)
        WHERE w.token=claim AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL AND nullif(current_setting('app.billing_actor_id',true),'') IS NULL
 AND nullif(current_setting('app.manual_admin_id',true),'') IS NULL
 AND nullif(current_setting('app.status_shop_id',true),'') IS NULL
 AND nullif(current_setting('app.payment_attempt_id',true),'') IS NULL;
    IF NOT FOUND THEN RETURN; END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('b'||a.account_id::text,0));
    PERFORM public.lock_refund_receipt(a.confirmation_id);
    SELECT w.state INTO previous FROM public.refund_work w WHERE w.token=claim AND w.lease_until>at_time FOR UPDATE;
    IF NOT FOUND THEN RETURN; END IF;
    PERFORM set_config('app.billing_account_id',a.account_id::text,true);
    SELECT EXISTS(SELECT FROM public.refund_sends WHERE reservation_id=a.reservation_id) INTO sent;
    IF outcome NOT IN ('Reserved','Unknown','Pending','Failed','Confirmed') OR
        (outcome='Reserved')=sent OR length(detail)>80 OR length(remote)>120 OR
        (outcome IN ('Pending','Failed','Confirmed') AND (remote IS NULL OR detail<>'OriginalProviderRead')) THEN
        RAISE EXCEPTION 'Invalid refund observation' USING ERRCODE='23514';
    END IF;
    -- Keep unverified POST references in observations. Only an independent read binds the identity.
    IF remote IS NOT NULL AND detail='OriginalProviderRead' AND outcome IN ('Pending','Confirmed','Failed') THEN
        INSERT INTO public.refund_bindings VALUES(a.reservation_id,a.provider_id,a.provider_account_id,a.environment,remote)
            ON CONFLICT DO NOTHING;
        SELECT reference INTO bound FROM public.refund_bindings WHERE reservation_id=a.reservation_id;
        IF bound IS DISTINCT FROM remote THEN outcome:='Unknown'; detail:='ConflictingReference'; END IF;
    END IF;
    INSERT INTO public.refund_observations(reservation_id,observed_at,outcome,reason,refund_reference)
        VALUES(a.reservation_id,at_time,outcome,detail,remote);
    IF previous IN ('Confirmed','Failed') AND outcome IN ('Confirmed','Failed') AND previous<>outcome THEN
        detail:='ConflictingProviderState';
    END IF;
    IF previous='Confirmed' OR (previous='Failed' AND outcome<>'Confirmed') THEN outcome:=previous; END IF;
    -- A missing local credential made no provider read. Keep the read budget available until it returns.
    read_deferred := sent AND outcome='Unknown' AND detail='OriginalAdapterUnavailable';
    UPDATE public.refund_work SET token=NULL,lease_until=NULL,state=outcome,last_check=at_time,
        reason=detail,refund_reference=coalesce(bound,refund_reference),
        candidate_reference=CASE WHEN coalesce(bound,refund_reference) IS NOT NULL THEN NULL
            WHEN wake_pending AND candidate_reference IS NOT NULL AND candidate_reference IS DISTINCT FROM remote THEN candidate_reference
            WHEN detail IN ('CandidateMismatch','ConflictingReference') THEN NULL
            ELSE coalesce(remote,candidate_reference) END,
        checks=CASE WHEN read_deferred THEN checks ELSE least(checks+1,12) END,
        due_at=CASE WHEN wake_pending THEN at_time
            WHEN read_deferred THEN at_time+interval '5 minutes'
            WHEN outcome IN ('Confirmed','Failed')
                OR detail IN ('BelowProviderMinimum','ProviderRefundUnsupported','LiveDispatchDisabled')
                OR (sent AND checks>=11) THEN 'infinity'::timestamptz
            ELSE at_time+interval '1 minute'*(1<<least(checks,5)) END WHERE reservation_id=a.reservation_id;
    IF outcome='Confirmed' AND previous<>'Confirmed' THEN
        RETURN QUERY SELECT * FROM public.apply_confirmed_refund(a,at_time);
    END IF;
END $$;


--
-- Name: guard_billing_document_supersession(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_billing_document_supersession() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    parent public.billing_document_versions%ROWTYPE;
BEGIN
    IF NEW.version_number = 1 THEN
        RETURN NULL;
    END IF;
    SELECT * INTO parent
    FROM public.billing_document_versions p
    WHERE p.account_id = NEW.account_id AND p.version_id = NEW.supersedes_version_id;
    IF NOT FOUND THEN
        RETURN NULL;
    END IF;
    IF parent.order_id <> NEW.order_id
        OR parent.document_id <> NEW.document_id
        OR parent.kind <> NEW.kind
        OR parent.version_number <> NEW.version_number - 1 THEN
        RAISE EXCEPTION 'Superseding billing document predecessor mismatch' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END;
$$;


--
-- Name: guard_billing_invitation_transition(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_billing_invitation_transition() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF OLD.revision <> 1 OR NEW.revision <> 2 OR
        (OLD.id, OLD.account_id, OLD.invited_actor_id, OLD.issuer_actor_id, OLD.issuer_revision,
            OLD.account_name, OLD.proof_hash, OLD.created_at, OLD.expires_at)
        IS DISTINCT FROM
        (NEW.id, NEW.account_id, NEW.invited_actor_id, NEW.issuer_actor_id, NEW.issuer_revision,
            NEW.account_name, NEW.proof_hash, NEW.created_at, NEW.expires_at) THEN
        RAISE EXCEPTION 'Invalid billing invitation transition' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_capacity_increase(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_capacity_increase() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF TG_OP='INSERT' THEN
        IF NOT EXISTS (SELECT FROM public.capacity_change_quotes q JOIN public.checkout_orders o
            ON o.account_id=q.account_id AND o.order_id=q.quote_id
            JOIN public.entitlement_messages e ON e.event_id=NEW.reserve_event AND e.account_id=q.account_id
            WHERE q.quote_id=NEW.order_id AND q.account_id=NEW.account_id AND q.term_id=NEW.term_id AND q.outcome='Payable'
                AND q.snapshot_hash=o.snapshot->'increase'->>'quoteHash' AND q.amount_paise=o.amount_paise
                AND NEW.target_revision-1=(o.snapshot->'increase'->>'sourceRevision')::bigint
                AND NEW.capacity=q.target_capacity AND NEW.members=q.target_member_allowance AND NEW.gross=q.target_gross_paise
                AND NEW.plan_version=q.target_plan_version_id AND e.kind='CapacityReserve'
                AND convert_from(e.payload,'UTF8')::jsonb @> jsonb_build_object('orderId',NEW.order_id,
                    'termId',NEW.term_id,'accountId',NEW.account_id,'targetRevision',NEW.target_revision,
                    'capacity',NEW.capacity,'members',NEW.members,'phase',e.kind)
                AND (EXISTS (SELECT FROM public.verified_payments v WHERE v.attempt_id=NEW.attempt_id
                    AND v.order_id=o.order_id AND v.account_id=o.account_id AND v.amount_paise=q.amount_paise)
                 OR EXISTS (SELECT FROM public.manual_payment_receipts r WHERE r.receipt_id=NEW.receipt_id
                    AND r.account_id=o.account_id AND r.order_id=o.order_id AND r.amount_paise=q.amount_paise))) THEN
            RAISE EXCEPTION 'Capacity increase requires its verified order' USING ERRCODE='23514';
        END IF;
    ELSIF OLD.state<>'Pending' OR (to_jsonb(NEW)-'state'-'reason'-'consume_event') IS DISTINCT FROM
        (to_jsonb(OLD)-'state'-'reason'-'consume_event') OR OLD.consume_event IS NOT NULL AND
        NEW.consume_event IS DISTINCT FROM OLD.consume_event OR NEW.state='Applied' AND NEW.consume_event IS NULL THEN
        RAISE EXCEPTION 'Capacity receipt conflicts with original operation' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_capacity_quote(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_capacity_quote() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE named bigint;
DECLARE hash text;
BEGIN
    SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
    INTO named, hash FROM public.capacity_change_quote_shops s
    WHERE s.account_id = NEW.account_id AND s.quote_id = NEW.quote_id AND s.replay_id = NEW.replay_id;
    IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
        OR (SELECT count(*) FROM public.capacity_change_quote_shops s
            WHERE s.account_id = NEW.account_id AND s.quote_id = NEW.quote_id) <> named
        OR NOT EXISTS (SELECT FROM public.subscription_coverage_terms t
            LEFT JOIN public.trial_history h ON h.account_id = t.account_id AND h.term_id = t.term_id
            LEFT JOIN LATERAL (SELECT e.ends_at FROM public.trial_extensions e
                WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY e.revision DESC LIMIT 1) e ON true
            WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id=t.account_id AND i.term_id=t.term_id
    AND i.state='Applied' ORDER BY i.target_revision DESC LIMIT 1),t.capacity) = NEW.prior_capacity
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


--
-- Name: guard_capacity_quote_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_capacity_quote_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.capacity_change_quotes q WHERE q.account_id = NEW.account_id AND q.quote_id = NEW.quote_id)
        OR NOT EXISTS (SELECT FROM public.shop_affiliations a
            WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
        RAISE EXCEPTION 'Capacity quote shop must be affiliated and inserted before its quote' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_catalogue_draft_version(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_catalogue_draft_version() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    old_facts jsonb := to_jsonb(OLD) - ARRAY['status', 'revision', 'updated_at', 'updated_by_admin_id'];
    new_facts jsonb := to_jsonb(NEW) - ARRAY['status', 'revision', 'updated_at', 'updated_by_admin_id'];
    plan_key uuid := (new_facts ->> 'plan_id')::uuid;
    plan_kind integer;
BEGIN
    IF NEW.revision <> OLD.revision + 1 OR NEW.updated_at < OLD.updated_at OR
        ROW(NEW.id, NEW.version_number, NEW.created_at, NEW.created_by_admin_id, NEW.source, NEW.source_version_id,
            NEW.source_expected_version, plan_key)
        IS DISTINCT FROM
        ROW(OLD.id, OLD.version_number, OLD.created_at, OLD.created_by_admin_id, OLD.source, OLD.source_version_id,
            OLD.source_expected_version, (old_facts ->> 'plan_id')::uuid) THEN
        RAISE EXCEPTION 'A catalogue version keeps its identity' USING ERRCODE = '55000';
    END IF;
    IF OLD.status = 1 AND NEW.status = 1 THEN RETURN NEW; END IF;
    IF old_facts IS DISTINCT FROM new_facts OR NOT (
        (OLD.status = 1 AND NEW.status = 2) OR (OLD.status = 2 AND NEW.status = 3)) THEN
        RAISE EXCEPTION 'Published catalogue facts are immutable' USING ERRCODE = '55000';
    END IF;
    IF NEW.status = 3 THEN RETURN NEW; END IF;
    IF NEW.source = 0 OR NOT EXISTS (
        SELECT FROM public.catalogue_publications publication
        JOIN public.catalogue_preparations prepared USING (admin_id, operation_key)
        WHERE publication.version_id = NEW.id AND prepared.version_id = NEW.id
            AND prepared.plan_id IS NOT DISTINCT FROM plan_key AND prepared.expected_version = OLD.revision
            AND publication.admin_id = NEW.updated_by_admin_id AND publication.published_at = NEW.updated_at
            AND (plan_key IS NULL OR EXISTS (
                SELECT FROM public.trial_policy_versions trial
                WHERE trial.id = prepared.trial_policy_version_id AND trial.status = 2
                    AND trial.source = NEW.source))) THEN
        RAISE EXCEPTION 'Publication requires matching confirmation evidence' USING ERRCODE = '55000';
    END IF;
    IF plan_key IS NULL THEN
        IF NEW.active_member_cap IS NULL OR NEW.max_concurrent_trial_shops IS NULL OR NEW.trial_terms_version IS NULL THEN
            RAISE EXCEPTION 'Publication requires complete trial terms' USING ERRCODE = '55000';
        END IF;
    ELSE
        SELECT kind INTO STRICT plan_kind FROM public.plans WHERE id = plan_key;
        IF NEW.included_shops IS NULL OR NEW.members_per_shop IS NULL OR NEW.extra_shops_offered IS NULL
            OR NEW.customer_terms_version IS NULL OR NEW.refund_policy_version IS NULL
            OR (plan_kind = 3 AND (NEW.negotiation_reason IS NULL OR
                (NEW.monthly_price_paise IS NULL AND NEW.annual_price_paise IS NULL)))
            OR (plan_kind IN (1, 2) AND (NEW.monthly_price_paise IS NULL OR NEW.annual_price_paise IS NULL))
            OR (plan_kind = 1 AND (NEW.included_shops <> 1 OR NEW.extra_shops_offered))
            OR (plan_kind = 2 AND NEW.included_shops < 2)
            OR (plan_kind = 3 AND NEW.extra_shops_offered)
            OR (NEW.extra_shops_offered AND
                (NEW.extra_shop_monthly_price_paise IS NULL OR NEW.extra_shop_annual_price_paise IS NULL)) THEN
            RAISE EXCEPTION 'Publication requires complete plan terms' USING ERRCODE = '55000';
        END IF;
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_catalogue_version_insert(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_catalogue_version_insert() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    plan_key uuid := (to_jsonb(NEW) ->> 'plan_id')::uuid;
    valid_source boolean;
BEGIN
    IF NEW.status IN (2, 3) THEN
        RAISE EXCEPTION 'A new catalogue version begins as a draft' USING ERRCODE = '55000';
    END IF;
    IF NEW.source_version_id IS NULL THEN RETURN NEW; END IF;

    IF plan_key IS NULL THEN
        SELECT EXISTS (SELECT FROM public.trial_policy_versions source_version
            WHERE source_version.id = NEW.source_version_id AND source_version.status IN (2, 3)
                AND source_version.revision = NEW.source_expected_version AND source_version.source = NEW.source
                AND source_version.version_number < NEW.version_number) INTO valid_source;
    ELSE
        SELECT EXISTS (SELECT FROM public.plan_versions source_version
            WHERE source_version.id = NEW.source_version_id AND source_version.plan_id = plan_key AND source_version.status IN (2, 3)
                AND source_version.revision = NEW.source_expected_version AND source_version.source = NEW.source
                AND source_version.version_number < NEW.version_number) INTO valid_source;
    END IF;
    IF NOT valid_source THEN
        RAISE EXCEPTION 'A successor retains its published source' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_checkout_acceptance(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_checkout_acceptance() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid OR NOT EXISTS (
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


--
-- Name: guard_checkout_order(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_checkout_order() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE named bigint;
DECLARE hash text;
DECLARE reduction_id uuid;
DECLARE replacement public.coverage_replacements%ROWTYPE;
BEGIN
    IF NEW.snapshot->'replacement' IS NOT NULL THEN
        SELECT * INTO STRICT replacement FROM public.coverage_replacements r
        WHERE r.account_id=NEW.account_id AND r.term_id=NEW.term_id AND r.state='Scheduled'
            AND r.replacement_id=(NEW.snapshot->'replacement'->>'replacementId')::uuid
            AND r.outgoing_shop_id=(NEW.snapshot->'replacement'->>'outgoingShopId')::uuid
            AND r.incoming_shop_id=(NEW.snapshot->'replacement'->>'incomingShopId')::uuid
            AND r.incoming_affiliation_id=(NEW.snapshot->'replacement'->>'incomingAffiliationId')::uuid
            AND r.consent_event=(NEW.snapshot->'replacement'->>'consentEvent')::uuid
            AND r.effective_at=(NEW.snapshot->>'previewStartsAt')::timestamptz
            AND r.capacity=(NEW.snapshot->>'capacity')::int AND r.member_allowance=(NEW.snapshot->>'membersPerShop')::int
            AND NEW.snapshot->>'startRule'='PriorPaidThrough' AND NEW.snapshot->'reduction' IS NULL
            AND EXISTS(SELECT FROM public.checkout_order_shops s WHERE s.order_id=NEW.order_id AND s.shop_id=r.incoming_shop_id)
            AND NOT EXISTS(SELECT FROM public.checkout_order_shops s WHERE s.order_id=NEW.order_id AND s.shop_id=r.outgoing_shop_id);
    END IF;
    reduction_id := (NEW.snapshot->'reduction'->>'scheduleId')::uuid;
    SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
    INTO named, hash
    FROM public.checkout_order_shops s JOIN public.current_shop_affiliations a
        ON a.request_id = s.affiliation_request_id
    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id AND s.replay_id = NEW.replay_id
        AND a.applied_at <= NEW.created_at
        AND ((s.shop_id=replacement.incoming_shop_id AND s.affiliation_request_id=replacement.incoming_affiliation_id)
            OR (NEW.term_id IS NULL AND (a.term_id IS NULL OR EXISTS (
                SELECT FROM public.paid_slot_handoffs h WHERE h.account_id = a.account_id AND h.term_id = a.term_id
                    AND h.outgoing_shop_id = a.shop_id AND h.cutover_at <= NEW.created_at)))
            OR (NEW.term_id IS NOT NULL AND EXISTS (
                SELECT FROM public.term_effective_roster(NEW.account_id, NEW.term_id, NULL) er
                WHERE er.shop_id = s.shop_id AND er.affiliation_request_id = s.affiliation_request_id)));
    IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid
        OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
        OR (SELECT count(*) FROM public.checkout_order_shops s
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id) <> named
        OR (NEW.term_id IS NOT NULL AND reduction_id IS NULL AND (SELECT count(*)
            FROM public.term_effective_roster(NEW.account_id, NEW.term_id, NULL)) <> named)
        OR (reduction_id IS NOT NULL AND (NEW.term_id IS NULL OR NOT EXISTS (
            SELECT FROM public.scheduled_capacity_reductions r
            WHERE r.account_id = NEW.account_id AND r.schedule_id = reduction_id AND r.term_id = NEW.term_id
                AND r.state = 'Scheduled' AND r.amount_paise = NEW.amount_paise
                AND r.retained_shop_count = NEW.shop_count AND r.retained_shop_hash = NEW.shop_hash
                AND r.snapshot_hash = NEW.snapshot->'reduction'->>'scheduleHash'
                AND (SELECT count(*) FROM public.checkout_order_shops os
                    JOIN public.scheduled_capacity_reduction_shops rs
                        ON rs.account_id = os.account_id AND rs.schedule_id = r.schedule_id
                        AND rs.shop_id = os.shop_id AND rs.affiliation_request_id = os.affiliation_request_id
                        AND rs.retained
                    WHERE os.account_id = NEW.account_id AND os.order_id = NEW.order_id) = NEW.shop_count)))
        OR (NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.payment_fulfilments f
            WHERE f.account_id = NEW.account_id AND f.predecessor_term_id = NEW.term_id))
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


--
-- Name: guard_checkout_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_checkout_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id=NEW.account_id AND q.order_id=NEW.order_id)
        OR NOT EXISTS (SELECT FROM public.current_shop_affiliations a WHERE a.account_id=NEW.account_id
            AND a.shop_id=NEW.shop_id AND a.request_id=NEW.affiliation_request_id AND a.shop_name=NEW.shop_name) THEN
        RAISE EXCEPTION 'Checkout shop must be affiliated and inserted before the quote' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_checkout_trial_source(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_checkout_trial_source() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id)
        OR NOT EXISTS (SELECT FROM public.trial_history h
            LEFT JOIN LATERAL (SELECT revision, ends_at FROM public.trial_extensions e
                WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY revision DESC LIMIT 1) e ON true
            WHERE h.account_id = NEW.account_id AND h.shop_id = NEW.shop_id AND h.term_id = NEW.trial_term_id
                AND NEW.revision = coalesce(e.revision, 0) AND NEW.ends_at = coalesce(e.ends_at, h.ends_at)) THEN
        RAISE EXCEPTION 'Checkout trial source must be current and unsealed' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_closure_reopening(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_closure_reopening() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'public'
    AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
    IF NOT EXISTS (
        SELECT FROM public.active_operational_closure_targets t
        JOIN public.operational_closures c ON c.id = t.closure_id AND c.account_id = t.account_id
        JOIN public.shop_affiliations a ON a.account_id = t.account_id AND a.shop_id = t.shop_id
            AND a.request_id = t.affiliation_request_id
        JOIN public.entitlement_shop_heads h ON h.shop_id = t.shop_id AND h.account_id = t.account_id
        WHERE t.closure_id = NEW.closure_id AND t.account_id = NEW.account_id AND t.shop_id = NEW.shop_id
            AND t.affiliation_request_id = NEW.affiliation_request_id AND h.revision = NEW.prior_revision
            AND NEW.owner_consent_at > c.recorded_at
    ) OR NEW.version <> 1 + (
        SELECT count(*) FROM public.operational_closure_targets t
        WHERE t.account_id = NEW.account_id AND t.shop_id = NEW.shop_id
    ) + (
        SELECT count(*) FROM public.operational_closure_reopenings r
        WHERE r.account_id = NEW.account_id AND r.shop_id = NEW.shop_id
    ) OR NEW.opens_shop <> (NOT EXISTS (
        SELECT FROM public.active_operational_closure_targets t
        WHERE t.account_id = NEW.account_id AND t.shop_id = NEW.shop_id
            AND t.affiliation_request_id = NEW.affiliation_request_id AND t.closure_id <> NEW.closure_id
    )) THEN
        RAISE EXCEPTION 'Reopening requires the active closure and its current entitlement head' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_complimentary_grant_scope(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_complimentary_grant_scope() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF TG_OP <> 'UPDATE' THEN
        RAISE EXCEPTION 'Complimentary grant revisions are retained' USING ERRCODE = '55000';
    END IF;
    IF NEW.account_id <> OLD.account_id OR NEW.revision <> OLD.revision + 1 THEN
        RAISE EXCEPTION 'Complimentary grant revisions only advance' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_complimentary_grant_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_complimentary_grant_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('g' || NEW.shop_id::text, 0));
    IF NOT EXISTS (SELECT FROM public.current_shop_affiliations a WHERE a.request_id = NEW.affiliation_request_id
            AND a.shop_id = NEW.shop_id AND a.account_id = NEW.account_id)
        OR EXISTS (SELECT FROM public.complimentary_grant_shops l WHERE l.shop_id = NEW.shop_id
            AND l.starts_at < NEW.ends_at AND NEW.starts_at < l.ends_at)
        OR EXISTS (SELECT FROM public.coverage_replacement_exceptions e
            WHERE e.account_id = NEW.account_id AND e.incoming_shop_id = NEW.shop_id
                AND e.overlap_disposition = 'CourtesyBridge'
                AND e.state IN ('Preparing', 'Consuming', 'Aborting')
                AND e.exception_id IS DISTINCT FROM nullif(current_setting('app.coverage_exception_applying_id', true), '')::uuid
                AND e.requested_early_start_at < NEW.ends_at AND NEW.starts_at < e.paid_cutover_at) THEN
        RAISE EXCEPTION 'Complimentary grant shop is invalid' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_coverage_exception(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_coverage_exception() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: guard_coverage_exception_approval(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_coverage_exception_approval() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: guard_coverage_replacement(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_coverage_replacement() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF (to_jsonb(NEW)-ARRAY['state','reason','consent_event','revoke_event','successor_term_id','incoming_coverage_from'])
        IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['state','reason','consent_event','revoke_event','successor_term_id','incoming_coverage_from'])
        OR OLD.consent_event IS NOT NULL AND NEW.consent_event IS DISTINCT FROM OLD.consent_event
        OR OLD.revoke_event IS NOT NULL OR OLD.state='Blocked'
        OR OLD.state='AwaitingConsent' AND NEW.state NOT IN ('Scheduled','Blocked')
        OR OLD.state='Scheduled' AND NEW.state NOT IN ('Applied','Blocked')
        OR OLD.state='Applied' AND (NEW.state<>'Applied' OR NEW.revoke_event IS NULL
            OR NEW.successor_term_id IS DISTINCT FROM OLD.successor_term_id
            OR NEW.incoming_coverage_from IS DISTINCT FROM OLD.incoming_coverage_from)
    THEN RAISE EXCEPTION 'Invalid replacement transition' USING ERRCODE='23514'; END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_coverage_transfer_proposal(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_coverage_transfer_proposal() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF (to_jsonb(NEW) - ARRAY['state','retired_reason','retired_at'])
            IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['state','retired_reason','retired_at'])
        OR OLD.state <> 'Open' OR NEW.state <> 'Retired' THEN
        RAISE EXCEPTION 'Invalid coverage transfer proposal transition' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_entitlement_source_revision(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_entitlement_source_revision() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.revision <> OLD.revision + 1 THEN
        RAISE EXCEPTION 'Entitlement source revisions must advance by one' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_manual_payment_receipt(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_manual_payment_receipt() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.checkout_orders q JOIN public.checkout_order_acceptances a USING (account_id, order_id)
        WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
            AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                OR (q.term_id IS NOT NULL AND (
                    (q.snapshot->>'startRule' = 'PriorPaidThrough' AND (q.snapshot->'reduction' IS NULL
                        OR EXISTS (SELECT FROM public.scheduled_capacity_reductions r
                            WHERE r.account_id = q.account_id AND r.term_id = q.term_id
                                AND r.schedule_id = (q.snapshot->'reduction'->>'scheduleId')::uuid
                                AND r.snapshot_hash = q.snapshot->'reduction'->>'scheduleHash'
                                AND r.amount_paise = q.amount_paise)))
                    OR q.snapshot->>'startRule' = 'CapacityIncrease' AND EXISTS (SELECT FROM public.capacity_change_quotes z
                        WHERE z.account_id=q.account_id AND z.quote_id=q.order_id AND z.term_id=q.term_id
                            AND z.snapshot_hash=q.snapshot->'increase'->>'quoteHash' AND z.amount_paise=q.amount_paise
                            AND z.outcome='Payable')
                    OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
            AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
            AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
            AND a.accepted_at < q.expires_at) THEN
        RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_paid_fresh_start(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_paid_fresh_start() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id)
        OR NOT EXISTS (SELECT FROM public.entitlement_account_heads h
            WHERE h.account_id = NEW.account_id
                AND (h.restriction = 'Unrestricted' OR EXISTS (SELECT FROM public.active_operational_restrictions r
    WHERE r.account_id = h.account_id AND r.shop_id IS NULL AND r.mode = h.restriction
        AND r.recorded_at <= NEW.ready_at AND NEW.ready_at < r.effective_at))
                AND h.revision = NEW.account_revision AND h.upstream_verified_at = NEW.upstream_verified_at
                AND h.released_revision IS NOT DISTINCT FROM NEW.released_revision FOR SHARE) THEN
        RAISE EXCEPTION 'Paid fresh start needs current unrestricted restoration evidence' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_paid_slot_handoff(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_paid_slot_handoff() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: guard_payment_attempt(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_attempt() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.checkout_orders q
        JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
        WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
            AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                OR (q.term_id IS NOT NULL AND (
                    (q.snapshot->>'startRule' = 'PriorPaidThrough' AND (q.snapshot->'reduction' IS NULL
                        OR EXISTS (SELECT FROM public.scheduled_capacity_reductions r
                            WHERE r.account_id = q.account_id AND r.term_id = q.term_id
                                AND r.schedule_id = (q.snapshot->'reduction'->>'scheduleId')::uuid
                                AND r.snapshot_hash = q.snapshot->'reduction'->>'scheduleHash'
                                AND r.amount_paise = q.amount_paise AND r.state = 'Scheduled')))
                    OR q.snapshot->>'startRule' = 'CapacityIncrease' AND EXISTS (SELECT FROM public.capacity_change_quotes z
                        WHERE z.account_id=q.account_id AND z.quote_id=q.order_id AND z.term_id=q.term_id
                            AND z.snapshot_hash=q.snapshot->'increase'->>'quoteHash' AND z.amount_paise=q.amount_paise
                            AND z.outcome='Payable' AND NEW.created_at < z.paid_through
                            AND (NEW.provider_id, NEW.provider_account_id, NEW.environment, NEW.configuration_version) =
                            (z.provider_id, z.provider_account_id, z.provider_environment, z.provider_configuration_version)
                            AND coalesce(NEW.selection_revision,0)=z.routing_revision)
                    OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
            AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
            AND q.snapshot_hash = NEW.snapshot_hash AND NEW.created_at < q.expires_at) THEN
        RAISE EXCEPTION 'Payment attempt must match its accepted order' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_payment_confirmation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_confirmation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.facts IS DISTINCT FROM public.payment_confirmation_snapshot(NEW.confirmation_id, NEW.account_id,
        NEW.order_id, NEW.attempt_id, NEW.manual_receipt_id, NEW.issued_at, 'Verification') THEN
        RAISE EXCEPTION 'Confirmation must match verified receipt and accepted order' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_payment_fulfilment_renewal(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_fulfilment_renewal() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    -- A row without exactly one money source is refused by payment_fulfilment_one_source itself.
    IF (NEW.attempt_id IS NULL) = (NEW.manual_receipt_id IS NULL) THEN
        RETURN NEW;
    END IF;
    IF NOT EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id
            AND q.order_id = NEW.order_id AND q.term_id IS NOT DISTINCT FROM NEW.predecessor_term_id) THEN
        RAISE EXCEPTION 'A renewal fulfilment must extend exactly its quoted term' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_payment_fulfilment_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_fulfilment_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    prior uuid;
    is_restored boolean;
    win_start timestamptz;
    win_end timestamptz;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
    PERFORM pg_advisory_xact_lock(hashtextextended('g' || NEW.shop_id::text, 0));
    NEW.replacement_id := NULL;
    IF nullif(current_setting('app.slot_assignment_id',true),'') IS NOT NULL AND EXISTS
        (SELECT FROM public.coverage_slot_assignments a WHERE a.assignment_id=nullif(current_setting('app.slot_assignment_id',true),'')::uuid
            AND a.account_id=NEW.account_id AND a.term_id=NEW.term_id AND a.order_id=NEW.order_id AND a.shop_id=NEW.shop_id
            AND a.affiliation_request_id=NEW.affiliation_request_id AND a.state='Applied' AND a.xmin=pg_current_xact_id()::xid)
    THEN
        SELECT applied_at, assignment_id INTO NEW.coverage_from, NEW.slot_assignment_id
        FROM public.coverage_slot_assignments
        WHERE assignment_id=nullif(current_setting('app.slot_assignment_id',true),'')::uuid;
        RETURN NEW;
    END IF;
    SELECT f.predecessor_term_id,
           EXISTS (SELECT FROM public.paid_fresh_starts pf
               WHERE pf.account_id = f.account_id AND pf.order_id = f.order_id AND pf.kind = 'RestoredAccess')
    INTO prior, is_restored
    FROM public.payment_fulfilments f
    WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.term_id = NEW.term_id;

    IF prior IS NOT NULL AND NOT coalesce(is_restored, false) THEN
        SELECT link.coverage_from, link.slot_assignment_id, link.replacement_id
        INTO NEW.coverage_from, NEW.slot_assignment_id, NEW.replacement_id
        FROM public.payment_fulfilment_shops link
        WHERE link.account_id = NEW.account_id AND link.term_id = prior AND link.shop_id = NEW.shop_id;
        IF NOT FOUND THEN
            SELECT er.interval_starts_at, er.slot_assignment_id INTO NEW.coverage_from, NEW.slot_assignment_id
            FROM public.term_effective_roster(NEW.account_id, prior, NULL) er
            WHERE er.shop_id = NEW.shop_id AND er.affiliation_request_id = NEW.affiliation_request_id
                AND er.handoff_id IS NOT NULL;
            IF NOT FOUND THEN
                SELECT r.incoming_coverage_from, r.replacement_id INTO NEW.coverage_from, NEW.replacement_id
                FROM public.coverage_replacements r WHERE r.account_id=NEW.account_id AND r.term_id=prior
                    AND r.successor_term_id=NEW.term_id AND r.state='Applied' AND r.xmin=pg_current_xact_id()::xid
                    AND r.incoming_shop_id=NEW.shop_id AND r.incoming_affiliation_id=NEW.affiliation_request_id;
            END IF;
        END IF;
    ELSE
        NEW.coverage_from := NULL;
        NEW.slot_assignment_id := NULL;
    END IF;

    IF NOT EXISTS (SELECT FROM public.payment_fulfilments f
            JOIN public.checkout_order_shops s ON s.account_id = f.account_id AND s.order_id = f.order_id
            WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.term_id = NEW.term_id
                AND s.shop_id = NEW.shop_id AND s.affiliation_request_id = NEW.affiliation_request_id)
        OR NOT EXISTS (SELECT FROM public.current_shop_affiliations a WHERE a.account_id = NEW.account_id
            AND a.shop_id = NEW.shop_id AND a.request_id = NEW.affiliation_request_id
            AND (prior IS NOT NULL OR a.term_id IS NULL OR EXISTS (
                SELECT FROM public.paid_slot_handoffs h WHERE h.account_id = a.account_id AND h.term_id = a.term_id
                    AND h.outgoing_shop_id = a.shop_id)))
        OR (prior IS NULL AND EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.shop_id = NEW.shop_id
            AND NOT EXISTS (SELECT FROM public.paid_slot_handoffs h WHERE h.account_id = p.account_id
                AND h.term_id = p.term_id AND h.outgoing_shop_id = p.shop_id)))
        OR (prior IS NOT NULL AND NEW.replacement_id IS NULL AND NOT EXISTS (
            SELECT FROM public.term_effective_roster(NEW.account_id, prior, NULL) er
            WHERE er.shop_id = NEW.shop_id AND er.affiliation_request_id = NEW.affiliation_request_id))
        OR (EXISTS (SELECT FROM public.trial_history h WHERE h.shop_id = NEW.shop_id)
            AND NOT EXISTS (SELECT FROM public.payment_trial_conversions c
                WHERE c.account_id = NEW.account_id AND c.shop_id = NEW.shop_id
                    AND (prior IS NOT NULL OR c.order_id = NEW.order_id))) THEN
        RAISE EXCEPTION 'Paid shop coverage is invalid' USING ERRCODE = '23514';
    END IF;

    SELECT greatest(t.starts_at, coalesce(NEW.coverage_from, t.starts_at)),
           public.term_effective_access_end(t.account_id, t.term_id)
    INTO win_start, win_end
    FROM public.subscription_coverage_terms t
    WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id;

    IF public.shop_has_conflicting_paid_coverage(NEW.account_id, NEW.shop_id, NEW.term_id, win_start, win_end) THEN
        RAISE EXCEPTION 'Paid shop coverage overlaps existing coverage' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_payment_provider_selection(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_provider_selection() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.id <> OLD.id OR NEW.revision <> OLD.revision + 1 OR NOT EXISTS (
        SELECT FROM public.payment_provider_selections e WHERE e.revision = NEW.revision
            AND e.profile_id = NEW.profile_id AND e.pin = NEW.pin
            AND coalesce(e.previous_pin, 'null'::jsonb) = coalesce(OLD.pin, 'null'::jsonb)) THEN
        RAISE EXCEPTION 'An immutable selection audit is required' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_payment_restoration_hold(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_restoration_hold() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id)
        OR NOT EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
            AND q.snapshot->>'startRule' IN ('Fulfilment', 'RestoredAccess'))
        OR (NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.verified_payments v
            WHERE v.attempt_id = NEW.attempt_id AND v.account_id = NEW.account_id AND v.order_id = NEW.order_id
                AND v.captured_at = NEW.effective_at AND v.verified_at = NEW.held_at))
        OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
            WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id AND r.order_id = NEW.order_id
                AND r.received_at = NEW.effective_at AND r.review_reason = 'RestorationPending'))
        OR (NEW.account_revision IS NULL AND EXISTS (SELECT FROM public.entitlement_account_heads h
            WHERE h.account_id = NEW.account_id))
        OR (NEW.account_revision IS NOT NULL AND NOT EXISTS (SELECT FROM public.entitlement_account_heads h
            WHERE h.account_id = NEW.account_id AND h.revision = NEW.account_revision AND h.restriction = NEW.restriction
                AND h.upstream_verified_at = NEW.upstream_verified_at)) THEN
        RAISE EXCEPTION 'Payment restoration hold needs verified funds and current restriction evidence' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_payment_trial_conversion(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_payment_trial_conversion() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.payment_fulfilment_shops p
            WHERE p.account_id = NEW.account_id AND p.order_id = NEW.order_id AND p.shop_id = NEW.shop_id)
        OR NOT EXISTS (SELECT FROM public.paid_fresh_starts f
            JOIN public.checkout_trial_sources s USING (account_id, order_id)
            JOIN public.trial_history h ON h.account_id = s.account_id AND h.shop_id = s.shop_id
            LEFT JOIN LATERAL (SELECT revision, ends_at FROM public.trial_extensions e
                WHERE e.account_id = h.account_id AND e.shop_id = h.shop_id ORDER BY revision DESC LIMIT 1) e ON true
            WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.kind = 'TrialConversion'
                AND s.shop_id = NEW.shop_id AND s.trial_term_id = NEW.trial_term_id
                AND s.revision = coalesce(e.revision, 0) AND s.ends_at = coalesce(e.ends_at, h.ends_at)
                AND NEW.paid_starts_at >= h.starts_at) THEN
        RAISE EXCEPTION 'Trial conversion must use the accepted trial evidence' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_reduction_preview(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_reduction_preview() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE named bigint;
DECLARE hash text;
BEGIN
    SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
    INTO named, hash FROM public.capacity_reduction_preview_shops s
    WHERE s.account_id = NEW.account_id AND s.preview_id = NEW.preview_id AND s.replay_id = NEW.replay_id;
    IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid OR named <> NEW.source_shop_count OR hash IS DISTINCT FROM NEW.source_shop_hash
        OR (SELECT count(*) FROM public.capacity_reduction_preview_shops s
            WHERE s.account_id = NEW.account_id AND s.preview_id = NEW.preview_id) <> named
        OR NOT EXISTS (SELECT FROM public.subscription_coverage_terms t
            JOIN public.subscription_term_facts f USING (account_id, term_id)
            WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND f.basis = 'Paid'
                AND coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = t.account_id
    AND i.term_id = t.term_id AND i.state = 'Applied' ORDER BY i.target_revision DESC LIMIT 1), t.capacity) = NEW.prior_capacity
                AND t.starts_at = NEW.current_starts_at AND t.ends_at = NEW.current_paid_through)
        OR EXISTS (SELECT FROM public.payment_fulfilments pf
            WHERE pf.account_id = NEW.account_id AND pf.predecessor_term_id = NEW.term_id)
        OR (NEW.snapshot->>'accountId')::uuid IS DISTINCT FROM NEW.account_id
        OR (NEW.snapshot->>'previewId')::uuid IS DISTINCT FROM NEW.preview_id
        OR (NEW.snapshot->>'termId')::uuid IS DISTINCT FROM NEW.term_id
        OR (NEW.snapshot->>'amountPaise')::bigint IS DISTINCT FROM NEW.amount_paise
        OR NEW.snapshot->>'snapshotHash' IS DISTINCT FROM NEW.snapshot_hash THEN
        RAISE EXCEPTION 'Reduction preview evidence is incomplete' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_reduction_preview_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_reduction_preview_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.capacity_reduction_previews p WHERE p.account_id = NEW.account_id AND p.preview_id = NEW.preview_id)
        OR NOT EXISTS (SELECT FROM public.shop_affiliations a
            WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
        RAISE EXCEPTION 'Reduction preview shop must be affiliated and inserted before its header' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_refund_access_decision(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_refund_access_decision() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    approval record;
    funded uuid;
    expected_count integer;
    actual_paid timestamptz;
    actual_end timestamptz;
    prior bigint;
BEGIN
    SELECT a.order_id, a.attempt_id, a.manual_receipt_id INTO approval FROM public.refund_approvals a
        WHERE a.account_id = NEW.account_id AND a.approval_id = NEW.approval_id
            AND a.refund_request_id = NEW.refund_request_id AND a.decided_at = NEW.decided_at
            AND a.xmin = pg_current_xact_id()::xid;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Access decision must be recorded with its refund approval' USING ERRCODE = '23514';
    END IF;
    -- Normal writers already hold this account fence. A direct writer may hold a receipt lock,
    -- so never wait in the opposite order.
    IF NOT pg_try_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0)) THEN
        RAISE EXCEPTION 'Refund account is busy' USING ERRCODE = '55P03';
    END IF;
    SELECT coalesce((SELECT f.term_id FROM public.payment_fulfilments f
        WHERE f.account_id = NEW.account_id AND f.order_id = approval.order_id
            AND (f.attempt_id = approval.attempt_id OR f.manual_receipt_id = approval.manual_receipt_id)),
        (SELECT i.term_id FROM public.capacity_increases i WHERE i.account_id = NEW.account_id
            AND i.order_id = approval.order_id AND i.state = 'Applied'
            AND (i.attempt_id = approval.attempt_id OR i.receipt_id = approval.manual_receipt_id))) INTO funded;
    IF funded IS DISTINCT FROM NEW.term_id THEN
        RAISE EXCEPTION 'Decision must identify its funded term' USING ERRCODE = '23514';
    END IF;
    SELECT count(*) INTO expected_count FROM public.refund_target_shops(NEW.account_id, funded, NEW.decided_at);
    IF NEW.shop_count <> expected_count OR EXISTS (
        SELECT x.shop_id, x.shop_name, x.restricted, x.closed, x.scheduled_restriction_at, x.courtesy_ends_at
        FROM public.refund_target_shops(NEW.account_id, funded, NEW.decided_at) x
        EXCEPT ALL
        SELECT j."shopId", j."shopName", j.restricted, j.closed, j."scheduledRestrictionAt", j."courtesyEndsAt"
        FROM jsonb_to_recordset(NEW.shops) j("shopId" uuid, "shopName" text, restricted boolean, closed boolean,
            "scheduledRestrictionAt" timestamptz, "courtesyEndsAt" timestamptz)
    ) THEN
        RAISE EXCEPTION 'Decision must name the current funded shops' USING ERRCODE = '23514';
    END IF;
    IF NEW.treatment = 'Preserve' THEN
        RETURN NEW;
    END IF;
    SELECT (SELECT coalesce(max(r.term_revision), 0) FROM public.refund_access_decisions r WHERE r.account_id = t.account_id AND r.term_id = t.term_id), coalesce(d.new_paid_through, t.ends_at),
        coalesce(d.new_access_ends_at, CASE WHEN c.kind = 'ScheduleCancellation' THEN t.ends_at
            ELSE t.ends_at + interval '168 hours' END)
    INTO prior, actual_paid, actual_end
    FROM public.subscription_coverage_terms t JOIN public.subscription_term_facts f USING (account_id, term_id)
    LEFT JOIN LATERAL (SELECT * FROM public.applied_refund_decisions d WHERE d.account_id = t.account_id
        AND d.term_id = t.term_id AND d.term_revision IS NOT NULL ORDER BY d.term_revision DESC LIMIT 1) d ON true
    LEFT JOIN LATERAL (SELECT kind FROM public.scheduled_subscription_changes c WHERE c.account_id = t.account_id
        AND c.term_id = t.term_id ORDER BY revision DESC LIMIT 1) c ON true
    WHERE t.account_id = NEW.account_id AND t.term_id = funded AND f.basis = 'Paid' AND NEW.effective_at > t.starts_at;
    IF NOT FOUND OR NEW.term_revision <> prior + 1 OR NEW.previous_paid_through <> actual_paid
        OR NEW.previous_access_ends_at <> actual_end OR public.refund_term_committed(NEW.account_id, funded)
        OR (NEW.treatment = 'TermAdjustment' AND NEW.new_access_ends_at <> CASE WHEN actual_end > actual_paid
            THEN least(NEW.effective_at + interval '168 hours', actual_end) ELSE NEW.effective_at END) THEN
        RAISE EXCEPTION 'Access decision must apply to the current unrenewed paid term' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_refund_approval(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_refund_approval() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    prior bigint;
    occupied bigint;
BEGIN
    IF NEW.admin_id IS DISTINCT FROM nullif(current_setting('app.manual_admin_id', true), '')::uuid
        OR NEW.session_id IS DISTINCT FROM nullif(current_setting('app.manual_session_id', true), '')::uuid
        OR NEW.mfa_verified_at IS DISTINCT FROM public.live_refund_session() THEN
        RAISE EXCEPTION 'Live Billing authorization required' USING ERRCODE = '42501';
    END IF;
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


--
-- Name: guard_refund_request(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_refund_request() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM public.payment_confirmations c
        WHERE c.confirmation_id = NEW.confirmation_id
            AND c.account_id = NEW.account_id
            AND c.order_id = NEW.order_id
            AND c.attempt_id IS NOT DISTINCT FROM NEW.attempt_id
            AND c.manual_receipt_id IS NOT DISTINCT FROM NEW.manual_receipt_id
            AND (c.facts->'receipt'->>'amountPaise')::bigint = NEW.payment_amount_paise
            AND c.facts->'receipt'->>'currency' = NEW.currency
            AND c.facts->>'providerId' = NEW.provider_id
            AND c.facts->>'providerAccountId' = NEW.provider_account_id
            AND c.facts->>'environment' = NEW.environment
            AND c.facts->>'configurationVersion' = NEW.configuration_version) THEN
        RAISE EXCEPTION 'Refund request must match a verified payment confirmation for the same account and order'
            USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_scheduled_reduction(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_scheduled_reduction() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE all_count bigint;
DECLARE all_hash text;
DECLARE kept_count bigint;
DECLARE kept_hash text;
BEGIN
    IF TG_OP = 'INSERT' THEN
        SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex'),
            count(*) FILTER (WHERE s.retained),
            encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id) FILTER (WHERE s.retained), 'UTF8')), 'hex')
        INTO all_count, all_hash, kept_count, kept_hash
        FROM public.scheduled_capacity_reduction_shops s
        WHERE s.account_id = NEW.account_id AND s.schedule_id = NEW.schedule_id AND s.replay_id = NEW.replay_id;
        IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid OR NEW.state <> 'Scheduled'
            OR all_count <> NEW.source_shop_count OR all_hash IS DISTINCT FROM NEW.source_shop_hash
            OR kept_count <> NEW.retained_shop_count OR kept_hash IS DISTINCT FROM NEW.retained_shop_hash
            OR EXISTS (SELECT FROM public.scheduled_capacity_reduction_shops s
                WHERE s.account_id = NEW.account_id AND s.schedule_id = NEW.schedule_id AND s.retained
                    AND (s.active_members <= 0 OR s.active_members > NEW.target_member_allowance))
            OR NOT EXISTS (SELECT FROM public.capacity_reduction_previews p
                WHERE p.account_id = NEW.account_id AND p.preview_id = NEW.preview_id AND p.term_id = NEW.term_id
                    AND p.amount_paise = NEW.amount_paise AND p.target_plan_version_id = NEW.target_plan_version_id
                    AND p.target_capacity = NEW.target_capacity AND p.target_member_allowance = NEW.target_member_allowance
                    AND p.target_mode = NEW.target_mode AND p.source_shop_count = NEW.source_shop_count
                    AND p.source_shop_hash = NEW.source_shop_hash AND p.source_hash = NEW.source_hash
                    AND p.current_paid_through = NEW.effective_at AND p.renewal_ends_at = NEW.renewal_ends_at
                    AND NEW.created_at >= p.created_at AND NEW.created_at < p.expires_at) THEN
            RAISE EXCEPTION 'Scheduled reduction must match its unexpired preview and resolved counts' USING ERRCODE = '23514';
        END IF;
    ELSE
        IF OLD.state IN ('Applied', 'NeedsReview')
            OR (to_jsonb(NEW) - 'state' - 'reason' - 'conflicts' - 'resolution_generation' - 'order_id' - 'attempt_id'
                - 'receipt_id' - 'payment_effective_at' - 'grace_days_included' - 'funded_at' - 'reserve_event'
                - 'consume_event' - 'reserve_expires_at' - 'successor_term_id' - 'applied_at')
            IS DISTINCT FROM (to_jsonb(OLD) - 'state' - 'reason' - 'conflicts' - 'resolution_generation' - 'order_id'
                - 'attempt_id' - 'receipt_id' - 'payment_effective_at' - 'grace_days_included' - 'funded_at'
                - 'reserve_event' - 'consume_event' - 'reserve_expires_at' - 'successor_term_id' - 'applied_at')
            OR (OLD.order_id IS NOT NULL AND NEW.order_id IS DISTINCT FROM OLD.order_id)
            OR (OLD.attempt_id IS NOT NULL AND NEW.attempt_id IS DISTINCT FROM OLD.attempt_id)
            OR (OLD.receipt_id IS NOT NULL AND NEW.receipt_id IS DISTINCT FROM OLD.receipt_id)
            OR NEW.resolution_generation < OLD.resolution_generation
            OR (NEW.state = 'Applied' AND (NEW.consume_event IS NULL OR NEW.successor_term_id IS NULL)) THEN
            RAISE EXCEPTION 'Scheduled reduction state transition is invalid' USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_scheduled_reduction_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_scheduled_reduction_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (SELECT FROM public.scheduled_capacity_reductions s WHERE s.account_id = NEW.account_id AND s.schedule_id = NEW.schedule_id)
        OR NOT EXISTS (SELECT FROM public.shop_affiliations a
            WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
        RAISE EXCEPTION 'Scheduled reduction shop must be affiliated and inserted before its schedule' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_service_restoration_adjustment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_service_restoration_adjustment() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.complimentary_grant_receipts r
            JOIN public.payment_fulfilments f ON f.account_id = r.account_id AND f.order_id = r.payment_order_id
            JOIN public.complimentary_grants g ON g.grant_id = r.grant_id AND g.account_id = r.account_id
            WHERE r.grant_id = NEW.grant_id AND r.account_id = NEW.account_id AND f.order_id = NEW.order_id
                AND f.term_id = NEW.paid_term_id AND r.admin_id = nullif(current_setting('app.grant_admin_id', true), '')::uuid
                AND g.shop_capacity = (SELECT count(*) FROM public.complimentary_grant_shops s
                    JOIN public.payment_fulfilment_shops p ON p.account_id = s.account_id AND p.shop_id = s.shop_id
                        AND p.affiliation_request_id = s.affiliation_request_id
                    WHERE s.grant_id = NEW.grant_id AND p.order_id = NEW.order_id)) THEN
        RAISE EXCEPTION 'Restoration adjustment must match the approved grant and paid coverage' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_shop_affiliation_before_increases(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_shop_affiliation_before_increases() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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
            AND t.capacity > (SELECT count(*) FROM public.shop_affiliations a WHERE a.term_id = NEW.term_id)
        )) THEN
        RAISE EXCEPTION 'Shop affiliation selection is invalid' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_shop_affiliation_coverage(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_shop_affiliation_coverage() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: guard_slot_assignment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_slot_assignment() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    at_time timestamptz;
    effective_paid timestamptz;
    cancel_rev bigint;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b'||NEW.account_id::text,0));
    IF TG_OP='UPDATE' THEN
        IF OLD.state<>'AwaitingConsent' OR NEW.state='AwaitingConsent'
            OR (to_jsonb(OLD)-ARRAY['state','reason','applied_at','consent_event']) <>
               (to_jsonb(NEW)-ARRAY['state','reason','applied_at','consent_event']) THEN
            RAISE EXCEPTION 'Slot request is immutable' USING ERRCODE='23514';
        END IF;
        IF NEW.state<>'Applied' THEN RETURN NEW; END IF;
    ELSIF NEW.state<>'AwaitingConsent' THEN
        RAISE EXCEPTION 'Owner consent required' USING ERRCODE='23514';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('g'||NEW.shop_id::text,0));
    at_time := coalesce(NEW.applied_at,NEW.created_at);
    effective_paid := public.term_effective_paid_through(NEW.account_id,NEW.term_id);
    SELECT coalesce(max(c.revision),0) INTO cancel_rev
    FROM public.scheduled_subscription_changes c WHERE c.account_id=NEW.account_id AND c.term_id=NEW.term_id;
    IF effective_paid IS NULL OR cancel_rev % 2 <> 0 OR NEW.cancellation_revision <> cancel_rev
        OR NOT public.slot_shop_eligible(NEW.account_id,NEW.shop_id,at_time,
            public.term_effective_access_end(NEW.account_id,NEW.term_id))
        OR NOT EXISTS(SELECT FROM public.shop_affiliations a WHERE a.account_id=NEW.account_id
            AND a.shop_id=NEW.shop_id AND a.request_id=NEW.affiliation_request_id)
        OR NOT EXISTS(SELECT FROM public.billing_account_members m WHERE m.account_id=NEW.account_id
            AND m.merchant_user_id=NEW.requester_id AND m.status=1 AND m.role IN (1,2) AND m.revision=NEW.requester_revision)
        OR NOT EXISTS(SELECT FROM public.payment_fulfilments f JOIN public.subscription_coverage_terms t USING(account_id,term_id)
            JOIN public.subscription_term_facts s USING(account_id,term_id)
            LEFT JOIN LATERAL (SELECT i.capacity,i.members FROM public.capacity_increases i
                WHERE i.account_id=t.account_id AND i.term_id=t.term_id AND i.state='Applied'
                ORDER BY target_revision DESC LIMIT 1) adj ON true
            WHERE f.order_id=NEW.order_id AND f.account_id=NEW.account_id AND f.term_id=NEW.term_id AND s.basis='Paid'
            AND t.starts_at<=at_time AND NEW.coverage_ends_at=effective_paid AND at_time<effective_paid
            AND NEW.capacity=coalesce(adj.capacity,t.capacity) AND NEW.member_allowance=coalesce(adj.members,s.member_allowance)
            AND public.capacity_slot_available(t.account_id,t.term_id,t.capacity)) THEN
        RAISE EXCEPTION 'Paid slot unavailable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_subscription_cancellation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_subscription_cancellation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: guard_subscription_cancellation_shop(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_subscription_cancellation_shop() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: guard_suspension_apply(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_suspension_apply() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE next_version bigint;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
    SELECT COALESCE(max(l.version), 0) + 1 INTO next_version
    FROM public.operational_restrictions r LEFT JOIN public.operational_restriction_releases l ON l.restriction_id = r.id
    WHERE r.account_id = NEW.account_id AND r.shop_id IS NOT DISTINCT FROM NEW.shop_id;
    IF NEW.version <> next_version OR EXISTS (
        SELECT FROM public.active_operational_restrictions r
        WHERE r.account_id = NEW.account_id AND r.shop_id IS NOT DISTINCT FROM NEW.shop_id
    ) THEN
        RAISE EXCEPTION 'Suspension requires next scope version' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_suspension_release(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_suspension_release() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
    IF NOT EXISTS (
        SELECT FROM public.active_operational_restrictions r
        WHERE r.id = NEW.restriction_id AND r.account_id = NEW.account_id
            AND NEW.version = r.version + 1 AND NEW.released_at >= r.recorded_at
            AND NEW.prior_revision = CASE WHEN r.shop_id IS NULL THEN
                (SELECT revision FROM public.entitlement_account_heads h WHERE h.account_id = r.account_id)
            ELSE (SELECT revision FROM public.entitlement_shop_heads h
                WHERE h.account_id = r.account_id AND h.shop_id = r.shop_id) END
    ) THEN
        RAISE EXCEPTION 'Release requires current suspension and revision' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_trial_against_paid_coverage(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_trial_against_paid_coverage() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
    IF EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.shop_id = NEW.shop_id) THEN
        RAISE EXCEPTION 'Trial coverage conflicts with paid coverage' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_trial_extension(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_trial_extension() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE original_end timestamptz; last_end timestamptz; last_revision bigint;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('t' || NEW.shop_id::text, 0));
    SELECT h.ends_at INTO original_end FROM public.trial_history h
    JOIN public.shop_affiliations a ON a.shop_id = h.shop_id AND a.account_id = h.account_id
    WHERE h.shop_id = NEW.shop_id AND h.account_id = NEW.account_id AND h.term_id = NEW.term_id AND a.term_id IS NULL;
    SELECT e.ends_at, e.revision INTO last_end, last_revision FROM public.trial_extensions e
    WHERE e.shop_id = NEW.shop_id AND e.account_id = NEW.account_id ORDER BY e.revision DESC LIMIT 1;
    IF original_end IS NULL OR NEW.revision <> coalesce(last_revision, 0) + 1
        OR NEW.previous_ends_at <> coalesce(last_end, original_end) THEN
        RAISE EXCEPTION 'Trial extension must advance its original trial' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_unmatched_payment_source(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_unmatched_payment_source() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_attempts a
        WHERE a.account_id = NEW.account_id AND a.order_id = NEW.order_id AND a.attempt_id = NEW.attempt_id
            AND a.amount_paise = NEW.expected_amount_paise AND a.currency = NEW.expected_currency) THEN
        RAISE EXCEPTION 'Review evidence must match the original attempt' USING ERRCODE = '23514';
    END IF;
    IF NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
        WHERE r.account_id = NEW.account_id AND r.order_id = NEW.order_id AND r.receipt_id = NEW.manual_receipt_id
            AND (r.review_reason = NEW.classification OR r.review_reason='CapacityPending' AND EXISTS (SELECT FROM public.capacity_increases i
    WHERE i.account_id=r.account_id AND i.order_id=r.order_id AND i.receipt_id=r.receipt_id
      AND i.state='NeedsReview' AND i.reason=NEW.classification)OR r.review_reason = 'ReductionPending' AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
    WHERE s.account_id = r.account_id AND s.order_id = r.order_id AND s.receipt_id = r.receipt_id
        AND s.state = 'NeedsReview' AND s.reason = NEW.classification)) AND r.amount_paise = NEW.expected_amount_paise
            AND r.amount_paise = NEW.observed_amount_paise AND r.currency = NEW.expected_currency
            AND r.currency = NEW.observed_currency AND r.received_at = NEW.captured_at
            AND r.verified_at = NEW.observed_at AND r.receiving_account_id = NEW.receiving_account_id
            AND r.environment = NEW.environment AND r.configuration_version = NEW.configuration_version
            AND NEW.provider_id = 'manual') THEN
        RAISE EXCEPTION 'Review evidence must match the original manual receipt' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_verified_payment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_verified_payment() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: issue_billing_ownership_transfer(uuid, uuid, integer, uuid, uuid, bytea, text, text, timestamp with time zone, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.issue_billing_ownership_transfer(p_id uuid, p_recipient uuid, p_version integer, p_key uuid, p_request uuid, p_hash bytea, p_reason text, p_policy text, p_at timestamp with time zone, p_until timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
    actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
    owner_member uuid;
    recipient uuid := nullif(p_recipient, actor);
BEGIN
    IF NOT has_table_privilege(session_user, 'public.billing_ownership_proposals', 'INSERT')
        OR NOT public.billing_transfer_has_proof(p_request, actor, account, 'billing.ownership-transfer.issue', p_at)
        THEN RETURN false;
    END IF;
    SELECT m.id INTO owner_member FROM public.billing_account_members m
        WHERE m.account_id = account AND m.merchant_user_id = actor AND m.role = 1
          AND m.status = 1 AND m.revision = p_version FOR SHARE;
    IF NOT FOUND THEN RETURN false; END IF;
    INSERT INTO public.billing_ownership_proposals
        (id, account_id, account_name, owner_actor_id, owner_member_id, owner_revision,
         recipient_actor_id, recipient_memberships, operation_key, request_id, request_hash, reason,
         responsibility_policy_version, created_at, expires_at)
    SELECT p_id, a.id, a.name, actor, owner_member, p_version, recipient,
           CASE WHEN recipient IS NOT NULL THEN public.billing_transfer_membership_snapshot(account, recipient) END,
           p_key, p_request, p_hash, p_reason, p_policy, p_at, p_until
    FROM public.billing_accounts a WHERE a.id = account
    ON CONFLICT (owner_actor_id, operation_key) DO NOTHING;
    RETURN true;
END $$;


--
-- Name: live_refund_session(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.live_refund_session() RETURNS timestamp with time zone
    LANGUAGE sql
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT s.mfa_verified_at FROM public.admin_sessions s JOIN public.admin_identities a ON a.id = s.admin_id
    CROSS JOIN (SELECT public.refund_credential_now() AS at) credential
    WHERE s.id = nullif(current_setting('app.manual_session_id', true), '')::uuid AND a.id = nullif(current_setting('app.manual_admin_id', true), '')::uuid AND s.revoked_at IS NULL
      AND s.mfa_verified_at <= credential.at AND credential.at < s.mfa_verified_at + interval '5 minutes'
      AND credential.at < s.created_at + interval '12 hours' AND credential.at < s.last_seen_at + interval '30 minutes'
      AND a.mfa_enrolled_at IS NOT NULL AND a.deactivated_at IS NULL AND (a.capabilities & 2) = 2
$$;


--
-- Name: lock_billing_removal_target(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.lock_billing_removal_target(p_member uuid) RETURNS TABLE(role integer, status integer, revision integer)
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
    actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
BEGIN
    IF public.billing_removal_owner(account, actor) IS NULL THEN RETURN; END IF;
    RETURN QUERY SELECT m.role, m.status, m.revision FROM public.billing_account_members m
        WHERE m.account_id = account AND m.id = p_member AND m.role = 2 FOR NO KEY UPDATE;
    IF NOT FOUND THEN
        -- Report an Owner without upgrading the caller's share lock.
        RETURN QUERY SELECT m.role, m.status, m.revision FROM public.billing_account_members m
            WHERE m.account_id = account AND m.id = p_member AND m.role = 1;
    END IF;
END $$;


--
-- Name: lock_gateway_inbox_event(uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.lock_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamp with time zone) RETURNS boolean
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (SELECT FROM public.gateway_inbox_work w WHERE w.inbox_id = p_inbox
        AND w.status = 'Leased' AND w.lease_token = p_token AND w.lease_expires_at > p_now
        AND w.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid AND w.attempt_id = nullif(current_setting('app.payment_attempt_id', true), '')::uuid AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND nullif(current_setting('app.status_shop_id', true), '') IS NULL FOR UPDATE)
$$;


--
-- Name: lock_pending_payment(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.lock_pending_payment(p_token uuid, p_now timestamp with time zone) RETURNS boolean
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (SELECT FROM public.pending_payment_attempts p WHERE p.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
        AND p.attempt_id = nullif(current_setting('app.payment_attempt_id', true), '')::uuid AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND NOT p.completed
        AND p.lease_token = p_token AND p.lease_expires_at > p_now FOR UPDATE)
$$;


--
-- Name: lock_refund_receipt(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.lock_refund_receipt(confirmation uuid) RETURNS void
    LANGUAGE sql
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('refund-receipt:' || confirmation::text, 0));
$$;


--
-- Name: original_transfer_blocker(uuid, timestamp with time zone, timestamp with time zone, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.original_transfer_blocker(id uuid, at_time timestamp with time zone, merchant_at timestamp with time zone DEFAULT NULL::timestamp with time zone, merchant_digest text DEFAULT NULL::text) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    r public.coverage_transfer_proposals%ROWTYPE;
    previous text := current_setting('app.billing_account_id', true);
    result text;
BEGIN
    SELECT * INTO r FROM public.coverage_transfer_proposals WHERE transfer_id = id;
    IF NOT FOUND THEN RETURN 'NotFound'; END IF;
    IF r.state = 'Retired' THEN RETURN r.retired_reason; END IF;
    IF r.expires_at <= at_time THEN RETURN 'Expired'; END IF;
    IF r.evidence_digest IS DISTINCT FROM public.coverage_transfer_evidence_digest(r.source_account_id, r.destination_account_id) THEN
        RETURN 'EvidenceChanged';
    END IF;
    PERFORM set_config('app.billing_account_id', r.source_account_id::text, true);
    SELECT CASE
        WHEN NOT EXISTS (SELECT FROM public.term_effective_roster(r.source_account_id, r.source_term_id, NULL) tr
            WHERE tr.shop_id = r.shop_id AND tr.affiliation_request_id = r.source_affiliation_id) THEN 'SourceChanged'
        WHEN r.permit_digest IS DISTINCT FROM public.coverage_transfer_permit_digest(
            r.source_account_id, r.shop_id, r.source_affiliation_id, r.created_at) THEN 'PermitEvidenceChanged'
        WHEN merchant_at IS NOT NULL AND merchant_digest IS DISTINCT FROM public.coverage_transfer_permit_digest(
            r.source_account_id, r.shop_id, r.source_affiliation_id, greatest(r.created_at, merchant_at))
            THEN 'PermitEvidenceChanged'
    END INTO result;
    PERFORM set_config('app.billing_account_id', coalesce(previous, ''), true);
    RETURN result;
END $$;


--
-- Name: payment_confirmation_before_increases(uuid, uuid, uuid, uuid, uuid, timestamp with time zone, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.payment_confirmation_before_increases(cid uuid, account uuid, ordering uuid, attempt uuid, receipt uuid, issued timestamp with time zone, basis text) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
SELECT jsonb_build_object(
    'policy', 'payment-confirmation/1',
    'receipt', jsonb_build_object('confirmationId', cid, 'accountId', account, 'orderId', ordering,
        'source', p.source, 'amountPaise', p.amount_paise, 'currency', p.currency,
        'verifiedAt', to_char(p.verified_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'issuedAt', to_char(issued AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'issuanceBasis', basis),
    'party', jsonb_build_object('accountNameAtIssuance', b.name, 'acceptedByMerchantUserId', a.actor_id),
    'terms', jsonb_build_object(
        'planVersionId', q.snapshot->'planVersionId', 'planVersion', q.snapshot->'planVersion',
        'planName', q.snapshot->'planName', 'period', q.snapshot->'period', 'capacity', q.snapshot->'capacity',
        'membersPerShop', q.snapshot->'membersPerShop', 'shopCount', q.shop_count,
        'customerTermsVersion', q.snapshot->'customerTermsVersion', 'refundPolicyVersion', q.snapshot->'refundPolicyVersion',
        'billingTimeZone', q.snapshot->'billingTimeZone', 'calendarVersion', q.snapshot->'calendarVersion',
        'renewalPolicyVersion', q.snapshot->'renewalPolicyVersion', 'startRule', q.snapshot->'startRule',
        'acceptedAt', to_char(a.accepted_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'quoteRevision', q.quote_revision, 'snapshotHash', q.snapshot_hash,
        'shops', (SELECT jsonb_agg(jsonb_build_object('shopId', s.shop_id, 'name', s.shop_name, 'termId', q.term_id)
            ORDER BY s.shop_id) FROM public.checkout_order_shops s WHERE s.account_id = account AND s.order_id = ordering)),
    'receiptReference', coalesce(attempt, receipt), 'providerId', p.provider_id,
    'providerAccountId', p.provider_account_id, 'environment', p.environment, 'configurationVersion', p.configuration_version,
    'providerOrderReference', p.order_reference, 'providerPaymentReference', p.payment_reference,
    'effectiveAt', to_char(p.effective_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), 'effectiveTimeBasis', p.time_basis,
    'fulfilmentAtIssuance', CASE WHEN EXISTS (SELECT FROM public.payment_fulfilments f
        WHERE f.account_id = account AND f.order_id = ordering AND
            (f.attempt_id = attempt OR f.manual_receipt_id = receipt)) THEN 'Fulfilled' ELSE 'NeedsReview' END)
FROM public.checkout_orders q JOIN public.billing_accounts b ON b.id = q.account_id
JOIN public.checkout_order_acceptances a USING (account_id, order_id)
JOIN LATERAL (
    SELECT 'Online'::text AS source, v.amount_paise, v.currency, v.verified_at, v.provider_id,
        v.provider_account_id, v.environment, v.configuration_version,
        s.provider_order_reference AS order_reference, v.provider_payment_reference AS payment_reference,
        v.captured_at AS effective_at, v.capture_time_basis AS time_basis
    FROM public.verified_payments v JOIN public.payment_attempt_sessions s USING (attempt_id, account_id)
    WHERE v.account_id = account AND v.order_id = ordering AND v.attempt_id = attempt AND receipt IS NULL
    UNION ALL
    SELECT 'Manual', m.amount_paise, m.currency, m.verified_at, 'manual', m.receiving_account_id,
        m.environment, m.configuration_version, NULL, NULL, m.received_at, 'VerifiedManualEvidence'
    FROM public.manual_payment_receipts m WHERE m.account_id = account AND m.order_id = ordering
        AND m.receipt_id = receipt AND attempt IS NULL
) p ON true
WHERE q.account_id = account AND q.order_id = ordering AND issued >= p.verified_at;
$$;


--
-- Name: payment_confirmation_before_reductions(uuid, uuid, uuid, uuid, uuid, timestamp with time zone, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.payment_confirmation_before_reductions(cid uuid, account uuid, ordering uuid, attempt uuid, receipt uuid, issued timestamp with time zone, basis text) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT public.payment_confirmation_before_increases(cid,account,ordering,attempt,receipt,issued,basis) ||
        CASE WHEN EXISTS (SELECT FROM public.capacity_increases i WHERE i.account_id=account AND i.order_id=ordering
            AND (i.attempt_id=attempt OR i.receipt_id=receipt))
        THEN jsonb_build_object('fulfilmentAtIssuance','Pending') ELSE '{}'::jsonb END
$$;


--
-- Name: payment_confirmation_snapshot(uuid, uuid, uuid, uuid, uuid, timestamp with time zone, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.payment_confirmation_snapshot(cid uuid, account uuid, ordering uuid, attempt uuid, receipt uuid, issued timestamp with time zone, basis text) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT public.payment_confirmation_before_reductions(cid,account,ordering,attempt,receipt,issued,basis) ||
        CASE WHEN EXISTS (SELECT FROM public.scheduled_capacity_reductions s
            WHERE s.account_id = account AND s.order_id = ordering
                AND (s.attempt_id = attempt OR s.receipt_id = receipt)
                AND NOT EXISTS (SELECT FROM public.payment_fulfilments f
                    WHERE f.account_id = account AND f.order_id = ordering))
        THEN jsonb_build_object('fulfilmentAtIssuance', 'Pending') ELSE '{}'::jsonb END
$$;


--
-- Name: pre_transfer_conflict(uuid, uuid, uuid, timestamp with time zone, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.pre_transfer_conflict(ac uuid, shop uuid, exclude_term uuid, win_start timestamp with time zone, win_end timestamp with time zone) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    WITH candidate_terms AS (
        SELECT DISTINCT p.term_id FROM public.payment_fulfilment_shops p
        WHERE p.account_id = ac AND p.shop_id = shop
        UNION
        SELECT DISTINCT a.term_id FROM public.current_shop_affiliations a
        WHERE a.account_id = ac AND a.shop_id = shop AND a.term_id IS NOT NULL
        UNION
        SELECT DISTINCT h.term_id FROM public.paid_slot_handoffs h
        WHERE h.account_id = ac AND h.incoming_shop_id = shop
    )
    SELECT EXISTS (
        SELECT FROM candidate_terms ct
        CROSS JOIN LATERAL public.term_paid_slot_intervals(ac, ct.term_id) i
        WHERE (exclude_term IS NULL OR ct.term_id <> exclude_term)
            AND i.shop_id = shop
            AND i.interval_starts_at < win_end
            AND win_start < CASE WHEN i.is_final_holder THEN public.term_effective_access_end(ac, ct.term_id)
                ELSE i.interval_ends_at END
    ) OR EXISTS (
        SELECT FROM public.coverage_replacement_exceptions e
        WHERE e.account_id = ac AND e.incoming_shop_id = shop
            AND (exclude_term IS NULL OR e.term_id <> exclude_term)
            AND e.state IN ('Preparing', 'Consuming', 'Aborting')
            AND e.paid_cutover_at < win_end
            AND win_start < public.term_effective_access_end(e.account_id, e.term_id)
    )
$$;


--
-- Name: pre_transfer_paid_slot_intervals(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.pre_transfer_paid_slot_intervals(ac uuid, scope_term uuid) RETURNS TABLE(slot_origin_shop_id uuid, shop_id uuid, affiliation_request_id uuid, interval_starts_at timestamp with time zone, interval_ends_at timestamp with time zone, slot_assignment_id uuid, handoff_id uuid, handoff_generation bigint, is_final_holder boolean)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    WITH eff AS (
        SELECT t.starts_at AS term_start, public.term_effective_paid_through(ac, scope_term) AS term_end
        FROM public.subscription_coverage_terms t
        WHERE t.account_id = ac AND t.term_id = scope_term
    ), base_slots AS (
        SELECT p.shop_id AS origin_shop_id, p.affiliation_request_id AS origin_aff_id,
            greatest(e.term_start, coalesce(p.coverage_from, e.term_start)) AS base_start,
            p.slot_assignment_id
        FROM public.payment_fulfilment_shops p CROSS JOIN eff e
        WHERE p.account_id = ac AND p.term_id = scope_term
        UNION ALL
        SELECT a.shop_id, a.request_id, e.term_start, NULL::uuid
        FROM public.shop_affiliations a CROSS JOIN eff e
        WHERE a.account_id = ac AND a.term_id = scope_term
            AND NOT EXISTS (SELECT FROM public.payment_fulfilment_shops p
                WHERE p.account_id = ac AND p.term_id = scope_term AND p.shop_id = a.shop_id)
    ), ordered_handoffs AS (
        SELECT h.*,
            lead(h.cutover_at) OVER (PARTITION BY h.slot_origin_shop_id ORDER BY h.slot_generation) AS next_cutover
        FROM public.paid_slot_handoffs h
        WHERE h.account_id = ac AND h.term_id = scope_term
    )
    -- Unsplit slots
    SELECT b.origin_shop_id, b.origin_shop_id, b.origin_aff_id, b.base_start, e.term_end,
        b.slot_assignment_id, NULL::uuid, NULL::bigint, true
    FROM base_slots b CROSS JOIN eff e
    WHERE NOT EXISTS (SELECT FROM ordered_handoffs h WHERE h.slot_origin_shop_id = b.origin_shop_id)
    UNION ALL
    -- Initial holder segment of a split slot (before generation 1 cutover)
    SELECT b.origin_shop_id, h.outgoing_shop_id, h.outgoing_affiliation_id, b.base_start, h.cutover_at,
        b.slot_assignment_id, h.handoff_id, h.generation, false
    FROM base_slots b
    JOIN ordered_handoffs h ON h.slot_origin_shop_id = b.origin_shop_id AND h.slot_generation = 1
    UNION ALL
    -- Incoming holder segments of every generation
    SELECT h.slot_origin_shop_id, h.incoming_shop_id, h.incoming_affiliation_id, h.cutover_at,
        coalesce(h.next_cutover, e.term_end), NULL::uuid, h.handoff_id, h.generation, (h.next_cutover IS NULL)
    FROM ordered_handoffs h CROSS JOIN eff e
$$;


--
-- Name: pre_transfer_slot_available(uuid, uuid, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.pre_transfer_slot_available(account uuid, term uuid, original_capacity integer) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT NOT EXISTS (SELECT FROM public.capacity_increases WHERE account_id = account AND term_id = term AND state = 'Pending')
        AND NOT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id = account AND predecessor_term_id = term)
        AND NOT EXISTS (SELECT FROM public.coverage_replacement_exceptions WHERE account_id = account AND term_id = term
            AND state IN ('Preparing', 'Consuming', 'Aborting'))
        AND coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = account AND i.term_id = term
            AND i.state = 'Applied' ORDER BY target_revision DESC LIMIT 1), original_capacity) > (
            SELECT count(*) FROM public.term_effective_roster(account, term, NULL))
$$;


--
-- Name: prepare_approved_refund(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.prepare_approved_refund() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE a public.refund_approvals;
BEGIN
    SELECT * INTO STRICT a FROM public.refund_approvals WHERE account_id = NEW.account_id AND approval_id = NEW.approval_id;
    IF a.attempt_id IS NULL THEN
        INSERT INTO public.refund_applications VALUES (a.account_id, NEW.decision_id,'Applied',NEW.decided_at);
        RETURN NULL;
    END IF;
    INSERT INTO public.refund_attempts SELECT a.reservation_id,a.account_id,a.approval_id,NEW.decision_id,
        a.refund_request_id,a.confirmation_id,a.order_id,a.attempt_id,a.provider_id,a.provider_account_id,
        a.environment,a.configuration_version,s.provider_order_reference,v.provider_payment_reference,
        coalesce((SELECT jsonb_object_agg(shop_id,request_id) FROM public.refund_target_shops(
            a.account_id, NEW.term_id, NEW.decided_at)), '{}'::jsonb),
        (SELECT decision_id FROM public.applied_refund_decisions WHERE account_id=a.account_id AND term_id=NEW.term_id
            AND term_revision IS NOT NULL ORDER BY term_revision DESC LIMIT 1),
        coalesce((SELECT max(i.target_revision) FROM public.capacity_increases i
            WHERE i.account_id=a.account_id AND i.term_id=NEW.term_id AND i.state='Applied'), 0),
        a.amount_paise,a.currency,a.payment_amount_paise
    FROM public.payment_confirmations c JOIN public.verified_payments v USING (account_id,order_id,attempt_id)
    JOIN public.payment_attempt_sessions s USING (account_id,attempt_id)
    WHERE c.confirmation_id=a.confirmation_id AND c.account_id=a.account_id AND c.attempt_id=a.attempt_id
        AND s.provider_order_reference=c.facts->>'providerOrderReference'
        AND v.provider_payment_reference=c.facts->>'providerPaymentReference'
        AND (v.provider_id,v.provider_account_id,v.environment,v.configuration_version,v.amount_paise,v.currency)
            = (a.provider_id,a.provider_account_id,a.environment,a.configuration_version,a.payment_amount_paise,a.currency);
    IF NOT FOUND THEN RAISE EXCEPTION 'Original verified payment required' USING ERRCODE='23514'; END IF;
    INSERT INTO public.refund_work(reservation_id,due_at) VALUES(a.reservation_id,a.decided_at);
    RETURN NULL;
END $$;


--
-- Name: preserve_admin_invitation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.preserve_admin_invitation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF TG_OP <> 'UPDATE' THEN
        RAISE EXCEPTION 'Admin invitations cannot be removed' USING ERRCODE = '55000';
    END IF;
    IF OLD.accepted_at IS NOT NULL OR OLD.revoked_at IS NOT NULL THEN
        RAISE EXCEPTION 'Settled admin invitations are permanent' USING ERRCODE = '55000';
    END IF;
    IF NEW.id <> OLD.id OR NEW.email <> OLD.email OR NEW.token_digest <> OLD.token_digest
        OR NEW.invited_by <> OLD.invited_by OR NEW.issued_at <> OLD.issued_at
        OR NEW.expires_at <> OLD.expires_at OR NEW.failed_attempts < OLD.failed_attempts THEN
        RAISE EXCEPTION 'Admin invitation evidence is immutable' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: preserve_initial_admin_bootstrap(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.preserve_initial_admin_bootstrap() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF TG_OP <> 'UPDATE' THEN
        RAISE EXCEPTION 'Bootstrap state cannot be removed' USING ERRCODE = '55000';
    END IF;
    IF OLD.consumed_at IS NOT NULL THEN
        RAISE EXCEPTION 'Consumed bootstrap state is permanent' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: protect_admin_recovery_evidence(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.protect_admin_recovery_evidence() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF OLD.kind = 2 AND OLD.redeemed_at IS NULL AND OLD.retired_at IS NULL
            AND num_nonnulls(NEW.redeemed_at, NEW.retired_at) = 1
            AND (NEW.redeemed_at IS NULL OR NEW.redeemed_at < OLD.grant_expires_at)
            AND (to_jsonb(NEW) - ARRAY['redeemed_at', 'retired_at'])
                = (to_jsonb(OLD) - ARRAY['redeemed_at', 'retired_at']) THEN
            RETURN NEW;
        END IF;
    END IF;
    RAISE EXCEPTION 'Administrator recovery evidence is immutable' USING ERRCODE = '55000';
END $$;


--
-- Name: queue_pending_payment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.queue_pending_payment() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    INSERT INTO public.pending_payment_attempts (attempt_id, account_id, next_attempt_at)
    VALUES (NEW.attempt_id, NEW.account_id, NEW.created_at + interval '30 seconds');
    RETURN NEW;
END $$;


--
-- Name: receive_gateway_inbox_event(uuid, text, text, text, text, text, text, text, text, bytea, bytea, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.receive_gateway_inbox_event(p_inbox uuid, p_provider text, p_account text, p_environment text, p_version text, p_event text, p_kind text, p_order text, p_payment text, p_hash bytea, p_evidence bytea, p_now timestamp with time zone) RETURNS boolean
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH stored AS (
        INSERT INTO public.gateway_inbox_events
            (inbox_id, provider_id, provider_account_id, environment, configuration_version, provider_event_id,
             kind, provider_order_reference, provider_payment_reference, body_sha256, protected_evidence, received_at)
        SELECT p_inbox, p_provider, p_account, p_environment, p_version, p_event, p_kind, p_order, p_payment,
            p_hash, p_evidence, p_now WHERE nullif(current_setting('app.billing_actor_id', true), '') IS NULL AND nullif(current_setting('app.status_shop_id', true), '') IS NULL AND nullif(current_setting('app.payment_attempt_id', true), '')::uuid IS NULL AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        ON CONFLICT (provider_id, provider_account_id, environment, provider_event_id) DO NOTHING
        RETURNING inbox_id
    ), queued AS (
        INSERT INTO public.gateway_inbox_work (inbox_id, next_attempt_at)
        SELECT inbox_id, p_now FROM stored RETURNING inbox_id
    )
    SELECT EXISTS (SELECT FROM queued)
$$;


--
-- Name: receive_refund_hint(uuid, text, text, text, text, text, text, text, text, bytea, bytea, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.receive_refund_hint(id uuid, provider text, account text, env text, version text, event_id text, payment text, remote text, receipt text, hash bytea, evidence bytea, at_time timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT public.receive_gateway_inbox_event(id,provider,account,env,version,event_id,'RefundChanged',
        NULL,payment,hash,evidence,at_time) THEN RETURN false; END IF;
    UPDATE public.refund_work w SET due_at=least(w.due_at,at_time),wake_pending=(w.token IS NOT NULL),
        candidate_reference=CASE WHEN w.refund_reference IS NULL AND remote IS NOT NULL AND length(remote) BETWEEN 1 AND 120
            THEN remote ELSE w.candidate_reference END
    FROM public.refund_attempts a JOIN public.refund_sends s USING(reservation_id)
    WHERE w.reservation_id=a.reservation_id AND a.provider_id=provider AND a.provider_account_id=account
        AND a.environment=env AND a.payment_reference=payment
        AND (w.refund_reference IS NULL OR w.refund_reference=remote)
        AND (receipt IS NULL OR receipt=replace(a.reservation_id::text,'-',''))
        AND (w.refund_reference=remote OR receipt=replace(a.reservation_id::text,'-',''));
    RETURN true;
END $$;


--
-- Name: recheck_refund(uuid, uuid, timestamp with time zone, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.recheck_refund(acct uuid, request uuid, at_time timestamp with time zone, simulated text) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE a public.refund_attempts;
BEGIN
    SELECT * INTO a FROM public.refund_attempts WHERE account_id=acct AND refund_request_id=request
        AND acct=nullif(current_setting('app.billing_account_id',true),'')::uuid AND public.live_refund_session() IS NOT NULL;
    IF NOT FOUND THEN RETURN false; END IF;
    IF simulated IS NOT NULL THEN
        IF a.provider_id<>'development-simulator' THEN RETURN false; END IF;
        INSERT INTO public.development_refund_controls VALUES(replace(a.reservation_id::text,'-',''),a.provider_account_id,
            a.environment,a.configuration_version,simulated,at_time+interval '30 seconds') ON CONFLICT(receipt)
            DO UPDATE SET outcome=excluded.outcome,available_at=excluded.available_at;
    END IF;
    UPDATE public.refund_work w SET due_at=least(due_at,at_time), wake_pending=(token IS NOT NULL) WHERE w.reservation_id=a.reservation_id
        AND EXISTS(SELECT FROM public.refund_sends WHERE reservation_id=a.reservation_id);
    RETURN FOUND OR simulated IS NOT NULL;
END $$;


--
-- Name: reduction_review_settled(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.reduction_review_settled(p_account uuid, p_schedule uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT p_account = nullif(current_setting('app.billing_account_id', true), '')::uuid
        AND has_table_privilege(session_user, 'public.scheduled_capacity_reductions', 'SELECT')
        AND EXISTS (
            SELECT FROM public.scheduled_capacity_reductions r
            JOIN public.unmatched_payments c
                ON c.account_id = r.account_id AND c.order_id = r.order_id
                AND ((r.attempt_id IS NOT NULL AND c.attempt_id = r.attempt_id)
                    OR (r.receipt_id IS NOT NULL AND c.manual_receipt_id = r.receipt_id))
            JOIN public.payment_review_decisions d
                ON d.account_id = c.account_id AND d.case_id = c.case_id
            WHERE r.account_id = p_account AND r.schedule_id = p_schedule
                AND r.state = 'NeedsReview'
                AND d.action IN ('RecordNoAccessDecision', 'ReferForRefundReview')
        )
$$;


--
-- Name: refund_boundary(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_boundary(scope_account uuid, scope_term uuid) RETURNS TABLE(new_paid_through timestamp with time zone, new_access_ends_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER ROWS 1
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT d.new_paid_through, d.new_access_ends_at FROM public.applied_refund_decisions d
    WHERE d.account_id = scope_account AND d.term_id = scope_term AND d.term_revision IS NOT NULL
        AND (scope_account = nullif(current_setting('app.billing_account_id', true), '')::uuid OR EXISTS (
            SELECT FROM public.shop_affiliations a
            WHERE a.account_id = scope_account AND a.shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid AND (a.term_id = scope_term OR EXISTS (
                SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = scope_account
                    AND p.term_id = scope_term AND p.shop_id = a.shop_id AND p.affiliation_request_id = a.request_id))))
    ORDER BY d.term_revision DESC LIMIT 1
$$;


--
-- Name: refund_credential_now(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_credential_now() RETURNS timestamp with time zone
    LANGUAGE sql
    SET search_path TO 'pg_catalog'
    AS $$ SELECT clock_timestamp() $$;


--
-- Name: refund_finance_observation(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_finance_observation(acct uuid, request uuid) RETURNS TABLE(refund_reference text, last_checked_at timestamp with time zone, observation_reason text)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT w.refund_reference,w.last_check,w.reason FROM public.refund_attempts a
    JOIN public.refund_work w USING(reservation_id)
    WHERE a.account_id=acct AND a.refund_request_id=request
        AND acct=nullif(current_setting('app.billing_account_id',true),'')::uuid
        AND public.live_refund_session() IS NOT NULL
$$;


--
-- Name: refund_outcome(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_outcome(acct uuid, request uuid) RETURNS TABLE(status text, approved_amount_paise bigint, provider_state text, access_disposition text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT CASE WHEN w.state IN ('Confirmed','Failed','Unknown') THEN w.state ELSE 'Pending' END,a.amount_paise,
        coalesce(w.state, CASE WHEN a.attempt_id IS NULL THEN 'Manual' ELSE 'Historical' END),
        coalesce(x.disposition,'AwaitingConfirmation')
    FROM public.refund_approvals a JOIN public.refund_access_decisions d USING(account_id,approval_id)
    LEFT JOIN public.refund_work w USING(reservation_id) LEFT JOIN public.refund_applications x USING(account_id,decision_id)
    WHERE a.account_id=acct AND a.refund_request_id=request AND acct=nullif(current_setting('app.billing_account_id',true),'')::uuid
$$;


--
-- Name: refund_receipt_occupied_paise(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_receipt_occupied_paise(scope_account uuid, scope_confirmation uuid) RETURNS bigint
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT coalesce(sum(held.amount_paise), 0)::bigint FROM (
        SELECT DISTINCT ON (a.reservation_id) a.amount_paise FROM public.refund_approvals a
        WHERE a.account_id = scope_account AND a.confirmation_id = scope_confirmation
        ORDER BY a.reservation_id) held;
$$;


--
-- Name: refund_target_shops(uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_target_shops(acct uuid, term uuid, at_time timestamp with time zone) RETURNS TABLE(shop_id uuid, request_id uuid, shop_name text, restricted boolean, scheduled_restriction_at timestamp with time zone, closed boolean, courtesy_ends_at timestamp with time zone)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: refund_term_committed(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refund_term_committed(acct uuid, term uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (SELECT FROM public.payment_fulfilments s WHERE s.account_id = acct AND s.predecessor_term_id = term)
        OR EXISTS (SELECT FROM public.capacity_increases i WHERE i.account_id = acct AND i.term_id = term AND i.state = 'Pending')
        OR EXISTS (SELECT FROM public.scheduled_capacity_reductions r WHERE r.account_id = acct AND r.term_id = term
            AND r.state IN ('Scheduled', 'Funded', 'Applying', 'NeedsResolution', 'NeedsReview'))
$$;


--
-- Name: refuse_billing_account_evidence_loss(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refuse_billing_account_evidence_loss() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Billing account facts are retained' USING ERRCODE = '55000';
END $$;


--
-- Name: refuse_billing_delegation_use_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refuse_billing_delegation_use_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Merchant billing delegation use is immutable' USING ERRCODE = '55000';
END $$;


--
-- Name: refuse_catalogue_evidence_loss(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refuse_catalogue_evidence_loss() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Catalogue facts are retained' USING ERRCODE = '55000';
END $$;


--
-- Name: refuse_converted_trial_extension(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refuse_converted_trial_extension() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
    IF EXISTS (SELECT FROM public.payment_trial_conversions x WHERE x.account_id = NEW.account_id
        AND x.shop_id = NEW.shop_id) THEN
        RAISE EXCEPTION 'A converted trial cannot be extended' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: refuse_provider_audit_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.refuse_provider_audit_mutation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Provider audit is append-only' USING ERRCODE = '55000';
END;
$$;


--
-- Name: remove_billing_manager(uuid, integer, uuid, uuid, bytea, text, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.remove_billing_manager(p_member uuid, p_revision integer, p_key uuid, p_request uuid, p_hash bytea, p_reason text, p_at timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
    actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
    owner_revision integer := public.billing_removal_owner(account, actor);
BEGIN
    IF owner_revision IS NULL THEN RETURN false; END IF;

    UPDATE public.billing_account_members
    SET status = 2, revision = revision + 1
    WHERE account_id = account AND id = p_member AND role = 2 AND status = 1 AND revision = p_revision;
    IF NOT FOUND THEN RETURN false; END IF;

    INSERT INTO public.billing_member_removals
        (account_id, member_id, actor_id, actor_revision, operation_key, request_id, request_hash, reason,
         removed_at, before_revision, after_revision)
    VALUES (account, p_member, actor, owner_revision, p_key, p_request, p_hash, p_reason,
        p_at, p_revision, p_revision + 1);
    RETURN true;
END $$;


--
-- Name: replacement_activation(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.replacement_activation(id uuid, at_time timestamp with time zone) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE r public.coverage_replacements%ROWTYPE;
        old_account text := current_setting('app.billing_account_id',true);
        outcome text;
BEGIN
    SELECT * INTO r FROM public.coverage_replacements WHERE replacement_id=id;
    IF NOT FOUND OR r.state<>'Applied' THEN RETURN 'Pending'; END IF;
    IF r.revoke_event IS NOT NULL THEN RETURN 'NeedsReview'; END IF;
    IF r.incoming_coverage_from>at_time THEN RETURN 'Waiting'; END IF;
    PERFORM set_config('app.billing_account_id',r.account_id::text,true);
    SELECT CASE
        WHEN o.status IN ('NeedsReview','DeadLetter') THEN 'NeedsReview'
        WHEN h.snap->'shop'->>'state'='Closed' THEN 'Closed'
        WHEN (h.snap->'shop'->'suspension'->>'effectiveAt')::timestamptz<=at_time
            OR (h.snap->'account'->'suspension'->>'effectiveAt')::timestamptz<=at_time THEN 'Suspended'
        WHEN h.snap->'shop'->>'replacementId'=r.replacement_id::text AND h.snap->'shop'->>'state' IN ('Active','Grace')
            AND (h.snap->'shop'->>'accessEndsAt')::timestamptz>at_time AND EXISTS(
                SELECT FROM public.applied_entitlement_shop_receipts a
                JOIN public.entitlement_merchant_inbox i ON i.event_id=s.event_id AND i.kind='Acknowledgement'
                WHERE convert_from(i.payload,'UTF8')::jsonb->>'activatedReplacementId'=r.replacement_id::text AND a.account_id=s.account_id
                    AND a.shop_id=s.shop_id AND a.affiliation_request_id=s.affiliation_request_id AND a.shop_revision=s.shop_revision)
            THEN 'Acknowledged'
        WHEN h.snap->'shop'->>'state' IN ('Expired','Restricted') THEN 'TermEnded'
        ELSE 'Applying' END INTO outcome
    FROM public.entitlement_snapshots s JOIN public.entitlement_messages m USING(event_id)
    JOIN public.entitlement_outbox o USING(event_id)
    CROSS JOIN LATERAL (SELECT convert_from(m.payload,'UTF8')::jsonb AS snap) h
    WHERE s.account_id=r.account_id AND s.shop_id=r.incoming_shop_id AND s.affiliation_request_id=r.incoming_affiliation_id
    ORDER BY s.shop_revision DESC LIMIT 1;
    PERFORM set_config('app.billing_account_id',coalesce(old_account,''),true);
    RETURN coalesce(outcome,'Applying');
END $$;


--
-- Name: replacement_owner_current(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.replacement_owner_current(ac uuid, id uuid) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE r public.coverage_replacements%ROWTYPE;
        previous_actor text := current_setting('app.billing_actor_id',true);
        authorized bool;
BEGIN
    SELECT * INTO r FROM public.coverage_replacements WHERE account_id=ac AND replacement_id=id;
    IF NOT FOUND THEN RETURN false; END IF;
    PERFORM set_config('app.billing_actor_id',r.requester_id::text,true);
    SELECT EXISTS(SELECT FROM public.billing_account_members m WHERE m.account_id=ac AND m.status=1
        AND m.role=1 AND m.revision=r.requester_revision AND m.merchant_user_id=r.requester_id) INTO authorized;
    PERFORM set_config('app.billing_actor_id',coalesce(previous_actor,''),true);
    RETURN authorized;
END $$;


--
-- Name: replacement_permit_end(uuid, uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.replacement_permit_end(ac uuid, shop uuid, affiliation uuid, at_time timestamp with time zone) RETURNS timestamp with time zone
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT max(valid_until) FROM public.offline_device_observations
    WHERE account_id=ac AND shop_id=shop AND affiliation_request_id=affiliation AND valid_until>at_time
$$;


--
-- Name: replacement_stale(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.replacement_stale(id uuid, at_time timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE r public.coverage_replacements%ROWTYPE;
        previous_account text := current_setting('app.billing_account_id',true);
        stale bool;
BEGIN
    SELECT * INTO r FROM public.coverage_replacements WHERE replacement_id=id;
    IF NOT FOUND OR r.state NOT IN ('AwaitingConsent','Scheduled') THEN RETURN false; END IF;
    PERFORM set_config('app.billing_account_id',r.account_id::text,true);
    SELECT r.expires_at<=at_time OR NOT public.replacement_owner_current(r.account_id,r.replacement_id)
        OR NOT public.replacement_term_available(r.account_id,r.term_id)
        OR NOT public.slot_shop_eligible(r.account_id,r.incoming_shop_id,at_time,'infinity')
        OR r.effective_at IS DISTINCT FROM public.term_effective_paid_through(r.account_id,r.term_id)
        OR EXISTS(SELECT FROM (SELECT i.capacity,i.members FROM public.capacity_increases i
            WHERE i.account_id=r.account_id AND i.term_id=r.term_id AND i.state='Applied'
            ORDER BY i.target_revision DESC LIMIT 1) adj
            WHERE adj.capacity<>r.capacity OR adj.members<>r.member_allowance) INTO stale;
    PERFORM set_config('app.billing_account_id',coalesce(previous_account,''),true);
    RETURN stale;
END $$;


--
-- Name: replacement_term_available(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.replacement_term_available(ac uuid, source_term uuid) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id = ac AND term_id = source_term)
        AND NOT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id = ac AND predecessor_term_id = source_term)
        AND NOT EXISTS (SELECT FROM public.refund_boundary(ac, source_term))
        AND NOT EXISTS (SELECT FROM public.capacity_increases WHERE account_id = ac AND term_id = source_term AND state = 'Pending')
        AND NOT EXISTS (SELECT FROM public.scheduled_capacity_reductions WHERE account_id = ac AND term_id = source_term
            AND state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview'))
        AND NOT EXISTS (SELECT FROM public.coverage_replacement_exceptions WHERE account_id = ac AND term_id = source_term
            AND state IN ('Preparing','Consuming','Aborting'))
$$;


--
-- Name: requeue_gateway_inbox_event(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.requeue_gateway_inbox_event(p_inbox uuid, p_now timestamp with time zone) RETURNS boolean
    LANGUAGE sql
    SET search_path TO 'pg_catalog'
    AS $$
    WITH queued AS (
        UPDATE public.gateway_inbox_work SET status = 'Pending', next_attempt_at = p_now, attempts = 0,
            finished_at = NULL
        WHERE inbox_id = p_inbox AND status = 'DeadLetter'
        RETURNING inbox_id
    )
    SELECT EXISTS (SELECT FROM queued)
$$;


--
-- Name: require_capacity_quote_seal(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_capacity_quote_seal() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.capacity_change_quotes q WHERE q.account_id = NEW.account_id
          AND q.quote_id = NEW.quote_id AND q.xmin = pg_current_xact_id()::xid AND q.replay_id = NEW.replay_id) THEN
        RAISE EXCEPTION 'Capacity quote shop and its sealed quote must be inserted together' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_catalogue_offer(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_catalogue_offer() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF OLD.status = 1 AND NEW.status = 2
        AND EXISTS (SELECT FROM public.plans WHERE id = NEW.plan_id AND kind IN (1, 2))
        AND NOT EXISTS (SELECT FROM public.published_catalogue_offers WHERE version_id = NEW.id) THEN
        RAISE EXCEPTION 'A public version requires a catalogue offer' USING ERRCODE = '55000';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_catalogue_publication_state(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_catalogue_publication_state() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM public.catalogue_preparations prepared
        WHERE prepared.admin_id = NEW.admin_id AND prepared.operation_key = NEW.operation_key
            AND prepared.version_id = NEW.version_id
            AND ((prepared.plan_id IS NULL AND EXISTS (
                SELECT FROM public.trial_policy_versions version
                WHERE version.id = NEW.version_id AND version.status = 2
                    AND version.revision = prepared.expected_version + 1))
              OR (prepared.plan_id IS NOT NULL AND EXISTS (
                SELECT FROM public.plan_versions version
                WHERE version.id = NEW.version_id AND version.plan_id = prepared.plan_id AND version.status = 2
                    AND version.revision = prepared.expected_version + 1)))) THEN
        RAISE EXCEPTION 'Catalogue confirmation requires a published version' USING ERRCODE = '55000';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_catalogue_retirement_state(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_catalogue_retirement_state() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM public.plan_versions version JOIN public.catalogue_publications publication ON publication.version_id = version.id
        WHERE version.id = NEW.version_id AND version.plan_id = NEW.plan_id AND version.status = 3
            AND version.revision - 1 = NEW.expected_version
            AND version.updated_by_admin_id = NEW.admin_id AND version.updated_at = NEW.retired_at
            AND publication.published_at <= NEW.retired_at)
        OR (EXISTS (SELECT FROM public.published_catalogue_offers WHERE version_id = NEW.version_id)
            AND NOT EXISTS (SELECT FROM public.withdrawn_catalogue_offers WHERE version_id = NEW.version_id)) THEN
        RAISE EXCEPTION 'A retirement needs matching version state and public withdrawal' USING ERRCODE = '55000';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_checkout_shop_seal(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_checkout_shop_seal() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
          AND q.xmin = pg_current_xact_id()::xid AND q.replay_id = NEW.replay_id) THEN
        RAISE EXCEPTION 'Checkout shop and its sealed quote must be inserted in the same transaction' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_checkout_trial_sources(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_checkout_trial_sources() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE summary jsonb := NEW.snapshot->'freshStart';
DECLARE frozen bigint;
DECLARE trial_count bigint;
DECLARE digest text;
BEGIN
    SELECT count(*), encode(sha256(convert_to('["paid-fresh-start/1","trials",[' ||
        coalesce(string_agg('["' || s.shop_id || '","' || s.trial_term_id || '",' || s.revision ||
            ',"' || to_char(s.ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') || '"]',
            ',' ORDER BY s.shop_id), '') || ']]', 'UTF8')), 'hex')
    INTO frozen, digest FROM public.checkout_trial_sources s
    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id;
    SELECT count(*) INTO trial_count FROM public.checkout_order_shops s
    JOIN public.trial_history h USING (account_id, shop_id)
    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id;
    IF (summary IS NULL AND frozen <> 0)
        OR (summary IS NOT NULL AND (summary->>'policy' IS DISTINCT FROM 'paid-fresh-start/1'
            OR summary->>'trialSourceHash' IS DISTINCT FROM digest
            OR (summary->>'trialShopCount')::int IS DISTINCT FROM frozen
            OR (NEW.term_id IS NULL AND (NEW.snapshot->>'startRule' IS DISTINCT FROM 'Fulfilment' OR frozen <> trial_count))
            OR (NEW.term_id IS NOT NULL AND (NEW.snapshot->>'startRule' IS DISTINCT FROM 'RestoredAccess' OR frozen <> 0)))) THEN
        RAISE EXCEPTION 'Checkout fresh-start source set is incomplete' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: require_complete_complimentary_grant(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_complete_complimentary_grant() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.complimentary_grant_receipts r WHERE r.grant_id = NEW.grant_id)
        OR (SELECT count(*) FROM public.complimentary_grant_shops l WHERE l.grant_id = NEW.grant_id) <> NEW.shop_capacity
        OR NOT EXISTS (SELECT FROM public.complimentary_grant_scopes s
            WHERE s.account_id = NEW.account_id AND s.revision >= NEW.granted_version) THEN
        RAISE EXCEPTION 'Complimentary grant evidence is incomplete' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_complete_manual_payment_receipt(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_complete_manual_payment_receipt() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.term_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_fulfilments f
        WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id AND f.manual_receipt_id = NEW.receipt_id
            AND f.term_id = NEW.term_id) THEN
        RAISE EXCEPTION 'Manual receipt fulfilment is incomplete' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_complete_payment_fulfilment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_complete_payment_fulfilment() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE fresh_kind text;
DECLARE applied_reduction uuid;
BEGIN
    SELECT kind INTO fresh_kind FROM public.paid_fresh_starts f
    WHERE f.account_id = NEW.account_id AND f.order_id = NEW.order_id;
    SELECT r.schedule_id INTO applied_reduction FROM public.scheduled_capacity_reductions r
    WHERE r.account_id = NEW.account_id AND r.order_id = NEW.order_id AND r.term_id = NEW.predecessor_term_id
        AND r.successor_term_id = NEW.term_id AND r.state = 'Applied' AND r.consume_event IS NOT NULL
        AND r.attempt_id IS NOT DISTINCT FROM NEW.attempt_id
        AND r.receipt_id IS NOT DISTINCT FROM NEW.manual_receipt_id
        AND EXISTS (SELECT FROM public.entitlement_merchant_inbox e
            WHERE e.account_id = r.account_id AND e.event_id = r.consume_event AND e.kind = 'ReductionReceipt');
    IF NOT EXISTS (SELECT FROM public.checkout_orders q
            JOIN public.subscription_term_facts t ON t.account_id = q.account_id AND t.term_id = NEW.term_id
            JOIN public.subscription_coverage_terms c ON c.account_id = q.account_id AND c.term_id = NEW.term_id
            WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id AND t.basis = 'Paid'
                AND t.price_paise = q.amount_paise AND c.capacity = (q.snapshot->>'capacity')::int
                AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                    WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = q.shop_count)
        OR ((NEW.attempt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.payment_events e
            WHERE e.attempt_id = NEW.attempt_id AND e.account_id = NEW.account_id
                AND e.order_id = NEW.order_id AND e.kind = 'OrderFulfilled'))
            OR (NEW.manual_receipt_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.manual_payment_receipts r
                WHERE r.receipt_id = NEW.manual_receipt_id AND r.account_id = NEW.account_id
                    AND r.order_id = NEW.order_id AND (r.term_id = NEW.term_id AND r.review_reason IS NULL
                        OR r.review_reason = 'ReductionPending' AND applied_reduction IS NOT NULL
                        OR r.review_reason = 'RestorationPending' AND EXISTS (SELECT FROM public.payment_restoration_holds h
                            WHERE h.account_id = r.account_id AND h.order_id = r.order_id AND h.manual_receipt_id = r.receipt_id)
                        OR r.review_reason = 'CoverageChanged' AND EXISTS (SELECT FROM public.unmatched_payments c
                            JOIN public.payment_review_decisions d USING (account_id, case_id)
                            WHERE c.account_id = r.account_id AND c.order_id = r.order_id AND c.manual_receipt_id = r.receipt_id
                                AND c.classification = 'CoverageChanged' AND c.observed_amount_paise = r.amount_paise
                                AND c.observed_currency = r.currency AND c.captured_at = r.received_at
                                AND d.action = 'RecoverAcceptedTerms' AND d.term_id = NEW.term_id)))))
        OR EXISTS (SELECT FROM public.unmatched_payments c
            JOIN public.verified_payments v ON v.attempt_id = c.attempt_id
                AND v.provider_payment_reference = c.provider_payment_reference AND v.account_id = c.account_id
            WHERE c.account_id = NEW.account_id AND c.order_id = NEW.order_id AND c.attempt_id = NEW.attempt_id
                AND c.classification = 'CoverageChanged'
                AND NOT EXISTS (SELECT FROM public.payment_restoration_holds h
                    WHERE h.account_id = NEW.account_id AND h.order_id = NEW.order_id AND h.attempt_id = NEW.attempt_id)
                AND NOT EXISTS (SELECT FROM public.payment_review_decisions d WHERE d.account_id = c.account_id
                    AND d.case_id = c.case_id AND d.action = 'RecoverAcceptedTerms' AND d.term_id = NEW.term_id))
        OR EXISTS (SELECT FROM public.unmatched_payments c JOIN public.payment_review_decisions d USING (account_id, case_id)
            WHERE c.account_id = NEW.account_id AND c.order_id = NEW.order_id
                AND (c.attempt_id = NEW.attempt_id OR c.manual_receipt_id = NEW.manual_receipt_id)
                AND c.classification = 'RestorationPending'
                AND d.action IN ('RecordNoAccessDecision', 'ReferForRefundReview'))
        OR NOT public.complete_fresh_start(NEW)
        OR (NEW.predecessor_term_id IS NOT NULL AND NOT EXISTS (SELECT FROM public.subscription_term_facts pf
            JOIN public.subscription_coverage_terms pc ON pc.account_id = pf.account_id AND pc.term_id = pf.term_id
            JOIN public.subscription_term_facts sf ON sf.account_id = pf.account_id AND sf.term_id = NEW.term_id
            JOIN public.subscription_coverage_terms sc ON sc.account_id = pf.account_id AND sc.term_id = NEW.term_id
            WHERE pf.account_id = NEW.account_id AND pf.term_id = NEW.predecessor_term_id AND pf.basis = 'Paid'
                AND sf.time_zone = pf.time_zone AND sf.billing_period = pf.billing_period
                AND sf.calendar_version = pf.calendar_version
                AND (CASE WHEN fresh_kind = 'RestoredAccess' THEN sf.period_ordinal = 0
                    AND sc.starts_at >= coalesce((SELECT d.new_access_ends_at FROM public.applied_refund_decisions d WHERE d.account_id = pc.account_id AND d.term_id = pc.term_id AND d.term_revision IS NOT NULL ORDER BY d.term_revision DESC LIMIT 1), pc.ends_at + interval '168 hours')
                    AND NEW.payment_effective_at >= coalesce((SELECT d.new_access_ends_at FROM public.applied_refund_decisions d WHERE d.account_id = pc.account_id AND d.term_id = pc.term_id AND d.term_revision IS NOT NULL ORDER BY d.term_revision DESC LIMIT 1), pc.ends_at + interval '168 hours')
                    AND NEW.grace_days_included = 0
                    ELSE sf.anchor_local = pf.anchor_local AND sf.period_ordinal = pf.period_ordinal + 1
                        AND sc.starts_at = pc.ends_at END)
                AND (CASE WHEN applied_reduction IS NOT NULL THEN EXISTS (
                        SELECT FROM public.scheduled_capacity_reductions r
                        WHERE r.account_id = NEW.account_id AND r.schedule_id = applied_reduction
                            AND sc.capacity = r.target_capacity AND sc.mode = r.target_mode
                            AND sf.member_allowance = r.target_member_allowance
                            AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                                WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = r.retained_shop_count
                            AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                                JOIN public.scheduled_capacity_reduction_shops rs
                                    ON rs.account_id = l.account_id AND rs.schedule_id = r.schedule_id
                                    AND rs.shop_id = l.shop_id AND rs.affiliation_request_id = l.affiliation_request_id
                                    AND rs.retained
                                WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id) = r.retained_shop_count)
                    ELSE sc.capacity = coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = pc.account_id
    AND i.term_id = pc.term_id AND i.state = 'Applied' ORDER BY i.target_revision DESC LIMIT 1), pc.capacity)
                        AND sc.mode = CASE WHEN (SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = pc.account_id
    AND i.term_id = pc.term_id AND i.state = 'Applied' ORDER BY i.target_revision DESC LIMIT 1) > 1 THEN 'Grouped' ELSE pc.mode END
                        AND NOT EXISTS (SELECT FROM public.scheduled_capacity_reductions r
                            WHERE r.account_id = NEW.account_id AND r.term_id = NEW.predecessor_term_id
                                AND (r.state IN ('Funded', 'Applying', 'NeedsResolution')
                                    OR (r.state = 'NeedsReview' AND (fresh_kind IS DISTINCT FROM 'RestoredAccess'
                                        OR NOT public.reduction_review_settled(r.account_id, r.schedule_id)))))
                        AND (SELECT count(*) FROM public.payment_fulfilment_shops l
                            WHERE l.account_id = NEW.account_id AND l.order_id = NEW.order_id)
                            = (SELECT count(*) FROM public.shop_affiliations a
                                WHERE a.account_id = NEW.account_id AND a.term_id = NEW.predecessor_term_id)
                            + (SELECT count(*) FROM public.payment_fulfilment_shops p
                                WHERE p.account_id = NEW.account_id AND p.term_id = NEW.predecessor_term_id)
                    END))) THEN
        RAISE EXCEPTION 'Payment fulfilment evidence is incomplete' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_payment_review_recovery(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_payment_review_recovery() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.action = 'RecoverAcceptedTerms' AND NOT EXISTS (SELECT FROM public.unmatched_payments c
        JOIN public.checkout_orders q USING (account_id, order_id)
        JOIN public.payment_fulfilments f USING (account_id, order_id)
        WHERE c.account_id = NEW.account_id AND c.case_id = NEW.case_id AND f.term_id = NEW.term_id
            AND q.snapshot->>'sourceHash' = NEW.accepted_source_hash
            AND c.classification IN ('CoverageChanged', 'RestorationPending')
            AND c.attempt_id IS NOT DISTINCT FROM f.attempt_id
            AND c.manual_receipt_id IS NOT DISTINCT FROM f.manual_receipt_id) THEN
        RAISE EXCEPTION 'Reviewed recovery must fulfil the selected original receipt' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_plan_retirement_evidence(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_plan_retirement_evidence() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.catalogue_retirements retirement WHERE retirement.version_id = NEW.id)
        AND NOT EXISTS (SELECT FROM public.plan_versions successor
            WHERE successor.plan_id = NEW.plan_id AND successor.version_number > NEW.version_number AND successor.status = 2) THEN
        RAISE EXCEPTION 'A plan retirement needs evidence or a published successor' USING ERRCODE = '55000';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_recovery_enrollment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_recovery_enrollment() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT NEW.issued_for_recovery AND EXISTS (
        SELECT FROM public.admin_recoveries WHERE admin_id = NEW.owner_id
    ) THEN
        RAISE EXCEPTION 'Recovery enrollment proof is required' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: require_reduction_preview_seal(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_reduction_preview_seal() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.capacity_reduction_previews p WHERE p.account_id = NEW.account_id
        AND p.preview_id = NEW.preview_id AND p.xmin = pg_current_xact_id()::xid AND p.replay_id = NEW.replay_id) THEN
        RAISE EXCEPTION 'Reduction preview shop and header must be inserted together' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_refund_access_decision(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_refund_access_decision() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF (SELECT count(*) FROM public.refund_access_decisions d
        WHERE d.account_id = NEW.account_id AND d.approval_id = NEW.approval_id
            AND d.refund_request_id = NEW.refund_request_id) <> 1 THEN
        RAISE EXCEPTION 'Refund approval requires its explicit access decision' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_scheduled_reduction_seal(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_scheduled_reduction_seal() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.scheduled_capacity_reductions s WHERE s.account_id = NEW.account_id
        AND s.schedule_id = NEW.schedule_id AND s.xmin = pg_current_xact_id()::xid AND s.replay_id = NEW.replay_id) THEN
        RAISE EXCEPTION 'Scheduled reduction shop and header must be inserted together' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_service_restoration_adjustment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_service_restoration_adjustment() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.service_restoration_adjustments a
        WHERE a.grant_id = NEW.grant_id AND a.account_id = NEW.account_id AND a.order_id = NEW.payment_order_id) THEN
        RAISE EXCEPTION 'Restoration grant linkage is incomplete' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: require_trial_extension_receipt(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.require_trial_extension_receipt() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NOT EXISTS (SELECT FROM public.trial_extension_receipts r WHERE r.extension_id = NEW.extension_id
        AND r.expected_version = NEW.revision - 1) THEN
        RAISE EXCEPTION 'Trial extension requires a matching receipt' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;


--
-- Name: reserve_complimentary_coverage(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.reserve_complimentary_coverage() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.complimentary_grants g
        WHERE g.account_id = NEW.account_id AND g.term_id = NEW.term_id) THEN
        RAISE EXCEPTION 'Complimentary coverage belongs to its named shops' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: reserve_paid_fulfilment_coverage(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.reserve_paid_fulfilment_coverage() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.payment_fulfilments f
        WHERE f.account_id = NEW.account_id AND f.term_id = NEW.term_id) THEN
        RAISE EXCEPTION 'Paid coverage belongs to its named shops' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: reserve_trial_coverage(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.reserve_trial_coverage() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.trial_history h
        WHERE h.account_id = NEW.account_id AND h.term_id = NEW.term_id) THEN
        RAISE EXCEPTION 'Trial coverage belongs to its originating shop' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: retain_entitlement_evidence(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.retain_entitlement_evidence() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Entitlement evidence is immutable' USING ERRCODE = '55000';
END $$;


--
-- Name: shop_effective_paid_link(uuid, uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.shop_effective_paid_link(ac uuid, shop uuid, request uuid, at_time timestamp with time zone) RETURNS TABLE(term_id uuid, coverage_from timestamp with time zone, coverage_ends_at timestamp with time zone, slot_assignment_id uuid, handoff_id uuid, handoff_generation bigint, is_released boolean)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    WITH candidate_terms AS (
        SELECT DISTINCT p.term_id FROM public.payment_fulfilment_shops p
        WHERE p.account_id = ac AND p.shop_id = shop
            AND (request IS NULL OR p.affiliation_request_id = request)
        UNION
        SELECT DISTINCT a.term_id FROM public.shop_affiliations a
        WHERE a.account_id = ac AND a.shop_id = shop AND a.term_id IS NOT NULL
            AND (request IS NULL OR a.request_id = request)
        UNION
        SELECT DISTINCT h.term_id FROM public.paid_slot_handoffs h
        WHERE h.account_id = ac AND (h.incoming_shop_id = shop OR h.outgoing_shop_id = shop)
            AND (request IS NULL OR (h.incoming_shop_id = shop AND h.incoming_affiliation_id = request)
                OR (h.outgoing_shop_id = shop AND h.outgoing_affiliation_id = request))
    ), links AS (
        SELECT ct.term_id,
            i.interval_starts_at,
            CASE WHEN i.handoff_id IS NOT NULL AND i.is_final_holder THEN i.interval_starts_at
                ELSE pfs.coverage_from END AS reported_from,
            CASE WHEN i.is_final_holder THEN NULL::timestamptz ELSE i.interval_ends_at END AS handoff_ends_at,
            i.slot_assignment_id,
            i.handoff_id,
            i.handoff_generation,
            NOT i.is_final_holder AS is_released,
            coalesce(pf.fulfilled_at, t.starts_at) AS fulfilled_at,
            t.ends_at AS term_ends_at,
            CASE WHEN i.handoff_id IS NOT NULL AND i.is_final_holder
                THEN greatest(coalesce(pf.fulfilled_at, t.starts_at), i.interval_starts_at)
                ELSE greatest(coalesce(pf.fulfilled_at, t.starts_at),
                    coalesce(pfs.coverage_from, coalesce(pf.fulfilled_at, t.starts_at))) END AS rank_at
        FROM candidate_terms ct
        JOIN public.subscription_coverage_terms t ON t.account_id = ac AND t.term_id = ct.term_id
        LEFT JOIN public.payment_fulfilments pf ON pf.account_id = ac AND pf.term_id = ct.term_id
        CROSS JOIN LATERAL public.term_paid_slot_intervals(ac, ct.term_id) i
        LEFT JOIN public.payment_fulfilment_shops pfs
            ON pfs.account_id = ac AND pfs.term_id = ct.term_id
            AND pfs.shop_id = i.shop_id AND pfs.affiliation_request_id = i.affiliation_request_id
        WHERE i.shop_id = shop
            AND (request IS NULL OR i.affiliation_request_id = request)
            AND (at_time IS NULL
                OR (coalesce(pf.fulfilled_at, t.starts_at) <= at_time
                    AND (pfs.coverage_from IS NULL OR pfs.coverage_from <= at_time)))
    )
    SELECT l.term_id, l.reported_from AS coverage_from, l.handoff_ends_at AS coverage_ends_at,
        l.slot_assignment_id, l.handoff_id, l.handoff_generation, l.is_released
    FROM links l
    ORDER BY l.rank_at DESC, l.term_ends_at DESC
    LIMIT 1
$$;


--
-- Name: shop_has_conflicting_paid_coverage(uuid, uuid, uuid, timestamp with time zone, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.shop_has_conflicting_paid_coverage(ac uuid, shop uuid, exclude_term uuid, win_start timestamp with time zone, win_end timestamp with time zone) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT public.pre_transfer_conflict(ac,shop,exclude_term,win_start,win_end) OR EXISTS(
        SELECT FROM public.coverage_transfer_bindings b WHERE b.destination_account_id=ac AND b.shop_id=shop
            AND b.cutover_at<b.paid_through_at AND b.cutover_at<win_end AND b.paid_through_at>win_start)
$$;


--
-- Name: shop_paid_link(uuid, uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.shop_paid_link(ac uuid, shop uuid, request uuid, at_time timestamp with time zone) RETURNS TABLE(term_id uuid, coverage_from timestamp with time zone, slot_assignment_id uuid)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT e.term_id, e.coverage_from, e.slot_assignment_id
    FROM public.shop_effective_paid_link(ac, shop, request, at_time) e
$$;


--
-- Name: slot_assignment_status(uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.slot_assignment_status(scope_account uuid, scope_assignment uuid, at_time timestamp with time zone) RETURNS TABLE(projected_state text, activation text, merchant_receipt text, effective_paid_through timestamp with time zone, effective_access_ends_at timestamp with time zone)
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    prev_account text := current_setting('app.billing_account_id', true);
    a record;
    term_paid timestamptz;
    term_access timestamptz;
    cancel_rev bigint;
    superseded boolean;
    tail_term uuid;
    tail_paid timestamptz;
    tail_access timestamptz;
    initial_rev bigint;
    initial_event uuid;
    latest_status text;
    latest_ack_outcome text;
    confirmed_usable boolean;
    change_outstanding boolean;
    has_any_applied boolean;
    receipt_out text;
    act_out text;
BEGIN
    SELECT * INTO a FROM public.coverage_slot_assignments r
    WHERE r.account_id = scope_account AND r.assignment_id = scope_assignment;
    IF NOT FOUND THEN RETURN; END IF;
    IF NOT (scope_account = nullif(current_setting('app.billing_account_id', true), '')::uuid
        OR (a.assignment_id = nullif(current_setting('app.slot_assignment_id', true), '')::uuid
            AND a.shop_id = nullif(current_setting('app.affiliation_shop_id', true), '')::uuid))
    THEN
        RETURN;
    END IF;

    BEGIN
        IF nullif(prev_account, '')::uuid IS DISTINCT FROM scope_account THEN
            PERFORM set_config('app.billing_account_id', scope_account::text, true);
        END IF;

        term_paid := public.term_effective_paid_through(a.account_id, a.term_id);
        term_access := public.term_effective_access_end(a.account_id, a.term_id);

        IF a.state = 'AwaitingConsent' THEN
            SELECT coalesce(max(c.revision), 0) INTO cancel_rev
            FROM public.scheduled_subscription_changes c WHERE c.account_id = a.account_id AND c.term_id = a.term_id;
            SELECT EXISTS (SELECT FROM public.payment_fulfilments s
                WHERE s.account_id = a.account_id AND s.predecessor_term_id = a.term_id) INTO superseded;
            IF least(a.expires_at, term_paid) <= at_time THEN
                projected_state := 'Expired';
            ELSIF a.coverage_ends_at <> term_paid OR cancel_rev <> a.cancellation_revision OR cancel_rev % 2 <> 0 OR superseded THEN
                projected_state := 'Stale';
            ELSE
                projected_state := 'AwaitingConsent';
            END IF;
            activation := 'NotApplicable';
            merchant_receipt := 'None';
            effective_paid_through := term_paid;
            effective_access_ends_at := term_access;
            PERFORM set_config('app.billing_account_id', coalesce(prev_account, ''), true);
            RETURN NEXT;
            RETURN;
        END IF;

        IF a.state <> 'Applied' THEN
            projected_state := a.state;
            activation := 'NotApplicable';
            merchant_receipt := 'None';
            effective_paid_through := term_paid;
            effective_access_ends_at := term_access;
            PERFORM set_config('app.billing_account_id', coalesce(prev_account, ''), true);
            RETURN NEXT;
            RETURN;
        END IF;

        -- The retained assignment id follows every contiguous renewal of this shop's link and is cleared by a
        -- restoration, so the lineage tail needs no bounded walk across the supported billing periods.
        SELECT ps.term_id INTO tail_term
        FROM public.payment_fulfilment_shops ps
        JOIN public.subscription_coverage_terms t ON t.account_id = ps.account_id AND t.term_id = ps.term_id
        WHERE ps.account_id = a.account_id AND ps.shop_id = a.shop_id AND ps.slot_assignment_id = a.assignment_id
        ORDER BY t.ends_at DESC LIMIT 1;
        tail_term := coalesce(tail_term, a.term_id);

        tail_paid := public.term_effective_paid_through(a.account_id, tail_term);
        tail_access := public.term_effective_access_end(a.account_id, tail_term);

        SELECT s.shop_revision, d.event_id INTO initial_rev, initial_event
        FROM public.coverage_slot_assignment_deliveries d
        JOIN public.entitlement_snapshots s ON s.event_id = d.event_id
        WHERE d.assignment_id = a.assignment_id;

        -- A closure can supersede the slot without carrying a slot id. Read every head in this affiliation
        -- after assignment, but only a head backed by this assignment can confirm its operating access.
        WITH shop_heads AS (
            SELECT s.event_id, s.shop_revision, o.status AS outbox_status, x.snap,
                   CASE WHEN ack.payload IS NOT NULL THEN convert_from(ack.payload, 'UTF8')::jsonb END AS receipt,
                   -- An undated suspension has no effective instant to compare and is never usable.
                   coalesce((s.event_id = initial_event OR x.snap->'shop'->>'slotAssignmentId' = a.assignment_id::text)
                    AND x.snap->'shop'->>'state' IN ('Active', 'Grace')
                    AND (x.snap->'shop'->'suspension' IS NULL
                         OR (x.snap->'shop'->'suspension'->>'effectiveAt')::timestamptz > x.signed_at)
                    AND (x.snap->'account'->>'restriction' = 'Unrestricted'
                         OR (x.snap->'account'->'suspension' IS NOT NULL
                             AND (x.snap->'account'->'suspension'->>'effectiveAt')::timestamptz > x.signed_at)), false) AS usable
            FROM public.entitlement_snapshots s
            JOIN public.entitlement_messages m USING (event_id, account_id, shop_id)
            JOIN public.entitlement_outbox o ON o.event_id = s.event_id
            CROSS JOIN LATERAL (SELECT j.snap, greatest((j.snap->'shop'->>'upstreamVerifiedAt')::timestamptz,
                    (j.snap->'account'->>'upstreamVerifiedAt')::timestamptz) AS signed_at
                FROM (SELECT convert_from(m.payload, 'UTF8')::jsonb AS snap) j) x
            LEFT JOIN public.entitlement_merchant_inbox ack
                ON ack.event_id = s.event_id AND ack.account_id = s.account_id AND ack.kind = 'Acknowledgement'
            WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id
              AND s.affiliation_request_id = a.affiliation_request_id
              AND initial_rev IS NOT NULL AND s.shop_revision >= initial_rev
        ), confirmed AS (
            SELECT h.shop_revision, h.usable, h.snap FROM shop_heads h
            JOIN public.applied_entitlement_shop_receipts r ON r.account_id = a.account_id
                AND r.shop_id = a.shop_id AND r.affiliation_request_id = a.affiliation_request_id
                AND r.shop_revision = h.shop_revision
            ORDER BY h.shop_revision DESC LIMIT 1
        )
        SELECT
            (SELECT outbox_status FROM shop_heads ORDER BY shop_revision DESC LIMIT 1),
            (SELECT receipt->>'outcome' FROM shop_heads ORDER BY shop_revision DESC LIMIT 1),
            coalesce((SELECT c.usable AND (c.snap->'shop'->>'accessEndsAt')::timestamptz > at_time
                AND (c.snap->'shop'->'suspension' IS NULL
                     OR (c.snap->'shop'->'suspension'->>'effectiveAt')::timestamptz > at_time)
                AND (c.snap->'account'->'suspension' IS NULL
                     OR (c.snap->'account'->'suspension'->>'effectiveAt')::timestamptz > at_time)
                FROM confirmed c), false),
            EXISTS (SELECT FROM shop_heads l, confirmed c WHERE l.shop_revision > c.shop_revision AND l.usable <> c.usable),
            EXISTS (SELECT FROM shop_heads l WHERE l.receipt->>'outcome' = 'Applied'
                AND (l.event_id = initial_event OR l.snap->'shop'->>'slotAssignmentId' = a.assignment_id::text))
        INTO latest_status, latest_ack_outcome, confirmed_usable, change_outstanding, has_any_applied;

        -- A failed newest delivery is reported before any earlier receipt, so it cannot be masked.
        IF latest_status = 'NeedsReview' OR latest_ack_outcome = 'NeedsReview' THEN
            receipt_out := 'NeedsReview';
        ELSIF latest_status = 'DeadLetter' THEN
            receipt_out := 'DeadLetter';
        ELSIF has_any_applied THEN
            receipt_out := 'Applied';
        ELSE
            receipt_out := 'Pending';
        END IF;

        IF EXISTS (SELECT FROM public.active_operational_closure_targets c
            WHERE c.account_id = a.account_id AND c.shop_id = a.shop_id
              AND c.affiliation_request_id = a.affiliation_request_id) THEN
            act_out := 'Closed';
        ELSIF EXISTS (SELECT FROM public.active_operational_restrictions r
            WHERE r.account_id = a.account_id AND (r.shop_id IS NULL OR r.shop_id = a.shop_id)
              AND r.effective_at <= at_time) THEN
            act_out := 'Suspended';
        ELSIF at_time >= tail_access THEN
            act_out := 'TermEnded';
        ELSIF receipt_out IN ('NeedsReview', 'DeadLetter') THEN
            act_out := 'NeedsReview';
        ELSIF NOT confirmed_usable OR change_outstanding THEN
            act_out := 'Applying';
        ELSIF at_time >= tail_paid THEN
            act_out := 'Grace';
        ELSE
            act_out := 'Acknowledged';
        END IF;

        projected_state := 'Applied';
        activation := act_out;
        merchant_receipt := receipt_out;
        effective_paid_through := tail_paid;
        effective_access_ends_at := tail_access;
        PERFORM set_config('app.billing_account_id', coalesce(prev_account, ''), true);
        RETURN NEXT;
    EXCEPTION WHEN OTHERS THEN
        PERFORM set_config('app.billing_account_id', coalesce(prev_account, ''), true);
        RAISE;
    END;
END $$;


--
-- Name: slot_shop_eligible(uuid, uuid, timestamp with time zone, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.slot_shop_eligible(ac uuid, shop uuid, at_time timestamp with time zone, until_time timestamp with time zone) RETURNS boolean
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT EXISTS (SELECT FROM public.current_shop_affiliations a WHERE a.account_id = ac AND a.shop_id = shop
            AND (a.term_id IS NULL
                OR public.term_effective_access_end(a.account_id, a.term_id) <= at_time
                OR EXISTS (SELECT FROM public.paid_slot_handoffs h WHERE h.account_id = a.account_id
                    AND h.term_id = a.term_id AND h.outgoing_shop_id = a.shop_id AND h.cutover_at <= at_time)))
        AND NOT public.shop_has_conflicting_paid_coverage(ac, shop, NULL, at_time, until_time)
        AND NOT EXISTS (SELECT FROM public.complimentary_grant_shops g WHERE g.shop_id = shop
            AND g.starts_at < until_time AND at_time < g.ends_at)
        AND NOT EXISTS (SELECT FROM public.trial_history h WHERE h.shop_id = shop AND NOT EXISTS (
            SELECT FROM public.payment_trial_conversions c WHERE c.shop_id = h.shop_id AND c.paid_starts_at <= at_time))
        AND NOT EXISTS (SELECT FROM public.active_operational_closure_targets c WHERE c.account_id = ac AND c.shop_id = shop)
        AND NOT EXISTS (SELECT FROM public.coverage_replacement_exceptions e WHERE e.incoming_shop_id = shop
            AND e.state IN ('AwaitingConsents', 'ReadyForReview', 'Preparing', 'Consuming', 'Aborting')
            AND NOT public.coverage_exception_stale(e.exception_id, at_time))
$$;


--
-- Name: start_refund_send(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.start_refund_send(claim uuid, at_time timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE id uuid;
BEGIN
    SELECT reservation_id INTO id FROM public.refund_work WHERE token=claim AND lease_until>at_time
        AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL AND nullif(current_setting('app.billing_actor_id',true),'') IS NULL
 AND nullif(current_setting('app.manual_admin_id',true),'') IS NULL
 AND nullif(current_setting('app.status_shop_id',true),'') IS NULL
 AND nullif(current_setting('app.payment_attempt_id',true),'') IS NULL FOR UPDATE;
    IF NOT FOUND THEN RETURN false; END IF;
    INSERT INTO public.refund_sends VALUES(id,at_time) ON CONFLICT DO NOTHING;
    IF NOT FOUND THEN RETURN false; END IF;
    UPDATE public.refund_work SET checks=0,state='Unknown',reason='DispatchStarted' WHERE reservation_id=id;
    RETURN true;
END $$;


--
-- Name: subscription_cancellation_scope(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.subscription_cancellation_scope(scope_account uuid, scope_term uuid) RETURNS TABLE(shop_id uuid, request_id uuid, shop_name text, applied_at timestamp with time zone, coverage_ends_at timestamp with time zone)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
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


--
-- Name: term_effective_access_end(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.term_effective_access_end(scope_account uuid, scope_term uuid) RETURNS timestamp with time zone
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT coalesce(
        (SELECT new_access_ends_at FROM public.refund_boundary(scope_account, scope_term)),
        CASE WHEN f.basis = 'Paid'
             AND NOT EXISTS (SELECT FROM public.payment_fulfilments s
                 WHERE s.account_id = t.account_id AND s.predecessor_term_id = t.term_id
                   AND NOT EXISTS (SELECT FROM public.paid_fresh_starts pf
                       WHERE pf.account_id = s.account_id AND pf.order_id = s.order_id
                         AND pf.kind = 'RestoredAccess'))
             AND NOT EXISTS (SELECT FROM public.scheduled_subscription_changes c
                 WHERE c.account_id = t.account_id AND c.term_id = t.term_id
                   AND c.kind = 'ScheduleCancellation'
                   AND NOT EXISTS (SELECT FROM public.scheduled_subscription_changes w
                       WHERE w.account_id = c.account_id AND w.withdraws_change_id = c.change_id))
            THEN t.ends_at + interval '168 hours'
            ELSE t.ends_at END)
    FROM public.subscription_coverage_terms t
    LEFT JOIN public.subscription_term_facts f USING (account_id, term_id)
    WHERE t.account_id = scope_account AND t.term_id = scope_term
$$;


--
-- Name: term_effective_paid_through(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.term_effective_paid_through(scope_account uuid, scope_term uuid) RETURNS timestamp with time zone
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT coalesce(
        (SELECT new_paid_through FROM public.refund_boundary(scope_account, scope_term)),
        t.ends_at)
    FROM public.subscription_coverage_terms t
    WHERE t.account_id = scope_account AND t.term_id = scope_term
$$;


--
-- Name: term_effective_roster(uuid, uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.term_effective_roster(ac uuid, scope_term uuid, at_time timestamp with time zone) RETURNS TABLE(slot_origin_shop_id uuid, shop_id uuid, affiliation_request_id uuid, interval_starts_at timestamp with time zone, interval_ends_at timestamp with time zone, slot_assignment_id uuid, handoff_id uuid, handoff_generation bigint, is_final_holder boolean)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    WITH eff AS (
        SELECT t.starts_at AS term_start, public.term_effective_paid_through(ac, scope_term) AS term_end
        FROM public.subscription_coverage_terms t
        WHERE t.account_id = ac AND t.term_id = scope_term
    ), intervals AS (
        SELECT * FROM public.term_paid_slot_intervals(ac, scope_term)
    )
    SELECT i.* FROM intervals i CROSS JOIN eff e
    WHERE (at_time IS NULL AND i.is_final_holder)
        OR (at_time IS NOT NULL AND at_time >= e.term_end AND i.is_final_holder)
        OR (at_time IS NOT NULL AND at_time < e.term_start AND i.interval_starts_at = (
            SELECT min(x.interval_starts_at) FROM intervals x WHERE x.slot_origin_shop_id = i.slot_origin_shop_id))
        OR (at_time IS NOT NULL AND at_time >= e.term_start AND at_time < e.term_end
            AND i.interval_starts_at <= at_time AND at_time < i.interval_ends_at)
$$;


--
-- Name: term_paid_slot_intervals(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.term_paid_slot_intervals(ac uuid, scope_term uuid) RETURNS TABLE(slot_origin_shop_id uuid, shop_id uuid, affiliation_request_id uuid, interval_starts_at timestamp with time zone, interval_ends_at timestamp with time zone, slot_assignment_id uuid, handoff_id uuid, handoff_generation bigint, is_final_holder boolean)
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT i.slot_origin_shop_id,i.shop_id,i.affiliation_request_id,i.interval_starts_at,least(i.interval_ends_at,b.cutover_at),
        i.slot_assignment_id,i.handoff_id,i.handoff_generation,i.is_final_holder AND b.transfer_id IS NULL
    FROM public.pre_transfer_paid_slot_intervals(ac,scope_term) i LEFT JOIN public.coverage_transfer_bindings b
        ON b.source_account_id=ac AND b.source_term_id=scope_term AND b.predecessor_id=i.affiliation_request_id
$$;


--
-- Name: transfer_activation_state(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.transfer_activation_state(id uuid) RETURNS text
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE previous text:=current_setting('app.billing_account_id',true); destination uuid; outcome text;
BEGIN
    SELECT destination_account_id INTO destination FROM public.coverage_transfer_bindings WHERE transfer_id=id AND source_account_id=nullif(current_setting('app.billing_account_id',true),'')::uuid;
    IF destination IS NULL THEN RETURN 'PendingActivation'; END IF;
    PERFORM set_config('app.billing_account_id',destination::text,true);
    SELECT CASE WHEN i.event_id IS NULL THEN 'PendingActivation'
            WHEN convert_from(i.payload,'UTF8')::jsonb @> jsonb_build_object('outcome','Applied','disposition','Applied')
                AND (convert_from(i.payload,'UTF8')::jsonb->>'appliedShopRevision')::bigint=s.shop_revision THEN 'Applied'
            ELSE 'NeedsReview' END INTO outcome FROM public.entitlement_snapshots s
        JOIN public.entitlement_outbox o ON o.event_id=s.event_id
        LEFT JOIN public.entitlement_merchant_inbox i ON i.event_id=s.event_id AND i.kind='Acknowledgement'
        WHERE s.event_id=id AND s.account_id=destination AND s.affiliation_request_id=id;
    PERFORM set_config('app.billing_account_id',previous,true);
    RETURN coalesce(outcome,'PendingActivation');
END $$;


--
-- Name: transfer_funding_digest(uuid, uuid, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.transfer_funding_digest(ac uuid, term uuid, shop uuid, affiliation uuid) RETURNS text
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT encode(sha256(convert_to(jsonb_build_array(r.slot_origin_shop_id,r.handoff_generation,r.slot_assignment_id,
        r.interval_starts_at,r.interval_ends_at,coalesce(i.target_revision,0),coalesce(i.members,f.member_allowance))::text,
        'UTF8')),'hex')
    FROM public.term_paid_slot_intervals(ac,term) r JOIN public.subscription_term_facts f
        ON f.account_id=ac AND f.term_id=term AND f.basis='Paid'
    LEFT JOIN LATERAL (SELECT target_revision,members FROM public.capacity_increases
        WHERE account_id=ac AND term_id=term AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
    WHERE r.shop_id=shop AND r.affiliation_request_id=affiliation AND r.is_final_holder
$$;


--
-- Name: operational_closure_reopenings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_closure_reopenings (
    id uuid NOT NULL,
    closure_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    version bigint NOT NULL,
    prior_revision bigint NOT NULL,
    opens_shop boolean NOT NULL,
    term_id uuid NOT NULL,
    basis text NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    paid_through timestamp with time zone NOT NULL,
    access_ends_at timestamp with time zone NOT NULL,
    impact_digest text NOT NULL,
    owner_consent_action text NOT NULL,
    owner_consent_user_id uuid NOT NULL,
    owner_consent_at timestamp with time zone NOT NULL,
    owner_consent_reference text NOT NULL,
    owner_consent_evidence text NOT NULL,
    reason text NOT NULL,
    evaluated_at timestamp with time zone NOT NULL,
    reopened_at timestamp with time zone NOT NULL,
    reopened_by uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    operation_key uuid NOT NULL,
    payload_digest text NOT NULL,
    event_id uuid NOT NULL,
    CONSTRAINT operational_closure_reopenings_access_ends_at_check CHECK (isfinite(access_ends_at)),
    CONSTRAINT operational_closure_reopenings_basis_check CHECK ((basis = ANY (ARRAY['Paid'::text, 'Complimentary'::text, 'Temporary'::text, 'Transition'::text]))),
    CONSTRAINT operational_closure_reopenings_check CHECK (((starts_at <= evaluated_at) AND (evaluated_at < paid_through) AND (paid_through <= access_ends_at) AND (owner_consent_at <= reopened_at))),
    CONSTRAINT operational_closure_reopenings_evaluated_at_check CHECK (isfinite(evaluated_at)),
    CONSTRAINT operational_closure_reopenings_impact_digest_check CHECK ((impact_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT operational_closure_reopenings_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT operational_closure_reopenings_owner_consent_action_check CHECK ((owner_consent_action = 'ReopenShop'::text)),
    CONSTRAINT operational_closure_reopenings_owner_consent_at_check CHECK (isfinite(owner_consent_at)),
    CONSTRAINT operational_closure_reopenings_owner_consent_evidence_check CHECK (((length(TRIM(BOTH FROM owner_consent_evidence)) >= 1) AND (length(TRIM(BOTH FROM owner_consent_evidence)) <= 2000))),
    CONSTRAINT operational_closure_reopenings_owner_consent_reference_check CHECK (((length(TRIM(BOTH FROM owner_consent_reference)) >= 1) AND (length(TRIM(BOTH FROM owner_consent_reference)) <= 120))),
    CONSTRAINT operational_closure_reopenings_paid_through_check CHECK (isfinite(paid_through)),
    CONSTRAINT operational_closure_reopenings_payload_digest_check CHECK ((payload_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT operational_closure_reopenings_prior_revision_check CHECK ((prior_revision > 0)),
    CONSTRAINT operational_closure_reopenings_reason_check CHECK (((length(TRIM(BOTH FROM reason)) >= 1) AND (length(TRIM(BOTH FROM reason)) <= 1000))),
    CONSTRAINT operational_closure_reopenings_reopened_at_check CHECK (isfinite(reopened_at)),
    CONSTRAINT operational_closure_reopenings_starts_at_check CHECK (isfinite(starts_at)),
    CONSTRAINT operational_closure_reopenings_version_check CHECK ((version > 1))
);

ALTER TABLE ONLY public.operational_closure_reopenings FORCE ROW LEVEL SECURITY;


--
-- Name: operational_closure_targets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_closure_targets (
    closure_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    shop_name text NOT NULL,
    consent_id uuid NOT NULL,
    work_digest text NOT NULL,
    event_id uuid NOT NULL,
    CONSTRAINT operational_closure_targets_work_digest_check CHECK ((work_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.operational_closure_targets FORCE ROW LEVEL SECURITY;


--
-- Name: active_operational_closure_targets; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.active_operational_closure_targets WITH (security_invoker='true') AS
 SELECT closure_id,
    account_id,
    shop_id,
    affiliation_request_id,
    shop_name,
    consent_id,
    work_digest,
    event_id
   FROM public.operational_closure_targets t
  WHERE (NOT (EXISTS ( SELECT
           FROM public.operational_closure_reopenings r
          WHERE ((r.closure_id = t.closure_id) AND (r.shop_id = t.shop_id)))));


--
-- Name: operational_restriction_releases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_restriction_releases (
    id uuid NOT NULL,
    restriction_id uuid NOT NULL,
    account_id uuid NOT NULL,
    version bigint NOT NULL,
    prior_revision bigint NOT NULL,
    released_at timestamp with time zone NOT NULL,
    released_by uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    reason text NOT NULL,
    operation_key uuid NOT NULL,
    payload_digest text NOT NULL,
    CONSTRAINT operational_restriction_releases_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT operational_restriction_releases_payload_digest_check CHECK ((payload_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT operational_restriction_releases_prior_revision_check CHECK ((prior_revision > 0)),
    CONSTRAINT operational_restriction_releases_reason_check CHECK (((length(TRIM(BOTH FROM reason)) >= 1) AND (length(TRIM(BOTH FROM reason)) <= 1000))),
    CONSTRAINT operational_restriction_releases_released_at_check CHECK (isfinite(released_at)),
    CONSTRAINT operational_restriction_releases_version_check CHECK ((version > 1))
);

ALTER TABLE ONLY public.operational_restriction_releases FORCE ROW LEVEL SECURITY;


--
-- Name: operational_restrictions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_restrictions (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid,
    version bigint NOT NULL,
    mode text NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    customer_explanation text NOT NULL,
    internal_reason text NOT NULL,
    review_reference text NOT NULL,
    actor_id uuid NOT NULL,
    session_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    payload_digest text NOT NULL,
    CONSTRAINT operational_restrictions_check CHECK ((isfinite(recorded_at) AND (recorded_at <= effective_at))),
    CONSTRAINT operational_restrictions_customer_explanation_check CHECK (((length(TRIM(BOTH FROM customer_explanation)) >= 1) AND (length(TRIM(BOTH FROM customer_explanation)) <= 500))),
    CONSTRAINT operational_restrictions_effective_at_check CHECK (isfinite(effective_at)),
    CONSTRAINT operational_restrictions_internal_reason_check CHECK (((length(TRIM(BOTH FROM internal_reason)) >= 1) AND (length(TRIM(BOTH FROM internal_reason)) <= 1000))),
    CONSTRAINT operational_restrictions_mode_check CHECK ((mode = ANY (ARRAY['Trading'::text, 'Security'::text]))),
    CONSTRAINT operational_restrictions_payload_digest_check CHECK ((payload_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT operational_restrictions_review_reference_check CHECK (((length(TRIM(BOTH FROM review_reference)) >= 1) AND (length(TRIM(BOTH FROM review_reference)) <= 120))),
    CONSTRAINT operational_restrictions_version_check CHECK ((version > 0))
);

ALTER TABLE ONLY public.operational_restrictions FORCE ROW LEVEL SECURITY;


--
-- Name: active_operational_restrictions; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.active_operational_restrictions WITH (security_invoker='true') AS
 SELECT id,
    account_id,
    shop_id,
    version,
    mode,
    effective_at,
    recorded_at,
    customer_explanation,
    internal_reason,
    review_reference,
    actor_id,
    session_id,
    operation_key,
    payload_digest
   FROM public.operational_restrictions r
  WHERE (NOT (EXISTS ( SELECT
           FROM public.operational_restriction_releases l
          WHERE (l.restriction_id = r.id))));


--
-- Name: admin_identities; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_identities (
    id uuid NOT NULL,
    email character varying(254) NOT NULL,
    password_hash character varying(60) NOT NULL,
    verification_reference character varying(128) NOT NULL,
    verified_at timestamp with time zone NOT NULL,
    mfa_enrolled_at timestamp with time zone,
    deactivated_at timestamp with time zone,
    recovery_continuation_digest character varying(64),
    capabilities integer DEFAULT 0 NOT NULL,
    CONSTRAINT ck_admin_capabilities CHECK (((capabilities >= 0) AND (capabilities <= 31))),
    CONSTRAINT ck_admin_deactivation_date CHECK ((deactivated_at >= verified_at)),
    CONSTRAINT ck_admin_mfa_date CHECK ((mfa_enrolled_at >= verified_at)),
    CONSTRAINT ck_admin_recovery_continuation CHECK (((recovery_continuation_digest IS NULL) OR ((mfa_enrolled_at IS NULL) AND ((recovery_continuation_digest)::text ~ '^[0-9A-F]{64}$'::text))))
);


--
-- Name: admin_invitations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_invitations (
    id uuid NOT NULL,
    email character varying(254) NOT NULL,
    token_digest character varying(64) NOT NULL,
    invited_by uuid NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    accepted_admin_id uuid,
    revoked_at timestamp with time zone,
    failed_attempts integer NOT NULL,
    CONSTRAINT ck_admin_invitation_acceptance CHECK ((((accepted_at IS NULL) AND (accepted_admin_id IS NULL)) OR ((accepted_at IS NOT NULL) AND (accepted_admin_id IS NOT NULL) AND (revoked_at IS NULL) AND (accepted_at >= issued_at) AND (accepted_at < expires_at)))),
    CONSTRAINT ck_admin_invitation_evidence CHECK ((((token_digest)::text ~ '^[0-9A-F]{64}$'::text) AND (issued_at < expires_at) AND ((failed_attempts >= 0) AND (failed_attempts <= 5)) AND ((revoked_at IS NULL) OR (revoked_at >= issued_at))))
);


--
-- Name: admin_recoveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_recoveries (
    id uuid NOT NULL,
    admin_id uuid NOT NULL,
    kind integer NOT NULL,
    started_at timestamp with time zone NOT NULL,
    reviewer_id uuid,
    reason integer,
    verification integer,
    evidence_reference character varying(64),
    grant_digest character varying(64),
    grant_expires_at timestamp with time zone,
    redeemed_at timestamp with time zone,
    retired_at timestamp with time zone,
    CONSTRAINT ck_admin_recovery_evidence CHECK ((((kind = 1) AND (redeemed_at = started_at) AND (num_nonnulls(reviewer_id, reason, verification, evidence_reference, grant_digest, grant_expires_at, retired_at) = 0)) OR ((kind = 2) AND (num_nonnulls(reviewer_id, reason, verification, evidence_reference, grant_digest, grant_expires_at) = 6) AND (reviewer_id <> admin_id) AND ((reason >= 1) AND (reason <= 3)) AND ((verification >= 1) AND (verification <= 3)) AND ((evidence_reference)::text ~ '^[A-Za-z0-9][A-Za-z0-9._:#/-]{2,63}$'::text) AND ((grant_digest)::text ~ '^[0-9A-F]{64}$'::text) AND (started_at < grant_expires_at) AND ((redeemed_at IS NULL) OR (retired_at IS NULL)) AND ((redeemed_at IS NULL) OR (redeemed_at >= started_at)) AND ((retired_at IS NULL) OR (retired_at >= started_at)))))
);


--
-- Name: admin_recovery_codes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_recovery_codes (
    admin_id uuid NOT NULL,
    digest character varying(64) NOT NULL,
    consumed_at timestamp with time zone,
    CONSTRAINT ck_admin_recovery_digest CHECK (((digest)::text ~ '^[0-9A-F]{64}$'::text))
);


--
-- Name: admin_refresh_credentials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_refresh_credentials (
    id uuid NOT NULL,
    session_id uuid NOT NULL,
    parent_id uuid,
    token_hash character(64) NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    CONSTRAINT ck_admin_refresh_credential_dates CHECK (((expires_at > issued_at) AND ((consumed_at IS NULL) OR (consumed_at >= issued_at)))),
    CONSTRAINT ck_admin_refresh_credential_hash CHECK ((token_hash ~ '^[0-9A-F]{64}$'::text)),
    CONSTRAINT ck_admin_refresh_credential_parent CHECK (((parent_id IS NULL) OR (parent_id <> id)))
);


--
-- Name: admin_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_sessions (
    id uuid NOT NULL,
    admin_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    CONSTRAINT ck_admin_session_dates CHECK (((mfa_verified_at >= created_at) AND (last_seen_at >= created_at) AND ((revoked_at IS NULL) OR (revoked_at >= created_at))))
);


--
-- Name: admin_sign_in_buckets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_sign_in_buckets (
    subject character varying(64) NOT NULL,
    window_ends_at timestamp with time zone NOT NULL,
    attempts integer NOT NULL,
    CONSTRAINT ck_admin_sign_in_attempts CHECK ((attempts > 0))
);


--
-- Name: admin_totp; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_totp (
    admin_id uuid NOT NULL,
    key_id character varying(32) NOT NULL,
    encrypted_secret bytea NOT NULL,
    last_accepted_step bigint,
    attempt_window_ends_at timestamp with time zone,
    attempts integer NOT NULL,
    retired_at timestamp with time zone,
    recovery_key_id character varying(32),
    CONSTRAINT ck_admin_totp_attempts CHECK ((((attempts = 0) AND (attempt_window_ends_at IS NULL)) OR ((attempts >= 1) AND (attempts <= 5) AND (attempt_window_ends_at IS NOT NULL)))),
    CONSTRAINT ck_admin_totp_secret CHECK (((octet_length(encrypted_secret) = 48) AND (last_accepted_step >= 0)))
);


--
-- Name: entitlement_merchant_inbox; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_merchant_inbox (
    event_id uuid NOT NULL,
    kind text NOT NULL,
    account_id uuid NOT NULL,
    digest text NOT NULL,
    payload bytea NOT NULL,
    assertion text NOT NULL,
    public_key text NOT NULL,
    CONSTRAINT entitlement_merchant_inbox_assertion_check CHECK (((length(assertion) >= 1) AND (length(assertion) <= 4096))),
    CONSTRAINT entitlement_merchant_inbox_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT entitlement_merchant_inbox_kind_check CHECK ((kind = ANY (ARRAY['Acknowledgement'::text, 'Reconciliation'::text, 'DeviceObservation'::text, 'CapacityReceipt'::text, 'ReductionReceipt'::text, 'CoverageSlotConsent'::text, 'CoverageReplacementConsent'::text, 'CoverageReplacementExceptionConsent'::text, 'CoverageReplacementExceptionReceipt'::text, 'CoverageTransferConsent'::text, 'TransferReceipt'::text]))),
    CONSTRAINT entitlement_merchant_inbox_payload_check CHECK (((octet_length(payload) >= 1) AND (octet_length(payload) <= 8192))),
    CONSTRAINT entitlement_merchant_inbox_public_key_check CHECK (((length(public_key) >= 1) AND (length(public_key) <= 2048)))
);

ALTER TABLE ONLY public.entitlement_merchant_inbox FORCE ROW LEVEL SECURITY;


--
-- Name: entitlement_snapshots; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_snapshots (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    account_revision bigint NOT NULL,
    shop_revision bigint NOT NULL,
    closure_revision bigint,
    publisher_protocol smallint DEFAULT 1 NOT NULL,
    CONSTRAINT entitlement_snapshots_account_revision_check CHECK ((account_revision > 0)),
    CONSTRAINT entitlement_snapshots_check CHECK (((closure_revision > 1) AND (closure_revision <= shop_revision))),
    CONSTRAINT entitlement_snapshots_publisher_protocol_check CHECK ((publisher_protocol = ANY (ARRAY[1, 2]))),
    CONSTRAINT entitlement_snapshots_shop_revision_check CHECK ((shop_revision > 0))
);

ALTER TABLE ONLY public.entitlement_snapshots FORCE ROW LEVEL SECURITY;


--
-- Name: applied_entitlement_shop_receipts; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.applied_entitlement_shop_receipts WITH (security_invoker='true') AS
 SELECT s.account_id,
    s.shop_id,
    s.affiliation_request_id,
    s.shop_revision,
    s.closure_revision,
    ((receipt.body ->> 'appliedAt'::text))::timestamp with time zone AS applied_at
   FROM ((public.entitlement_snapshots s
     JOIN public.entitlement_merchant_inbox ack ON (((ack.event_id = s.event_id) AND (ack.account_id = s.account_id) AND (ack.kind = 'Acknowledgement'::text))))
     CROSS JOIN LATERAL ( SELECT (convert_from(ack.payload, 'UTF8'::name))::jsonb AS body) receipt)
  WHERE (((receipt.body ->> 'disposition'::text) = 'Applied'::text) AND (((receipt.body ->> 'appliedShopRevision'::text))::bigint = s.shop_revision));


--
-- Name: refund_access_decisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_access_decisions (
    account_id uuid NOT NULL,
    decision_id uuid NOT NULL,
    approval_id uuid NOT NULL,
    refund_request_id uuid NOT NULL,
    treatment text NOT NULL,
    term_id uuid,
    term_revision bigint,
    effective_at timestamp with time zone,
    previous_paid_through timestamp with time zone,
    previous_access_ends_at timestamp with time zone,
    new_paid_through timestamp with time zone,
    new_access_ends_at timestamp with time zone,
    shop_count integer NOT NULL,
    shops jsonb NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    CONSTRAINT refund_access_decisions_check CHECK (((jsonb_typeof(shops) = 'array'::text) AND (jsonb_array_length(shops) = shop_count))),
    CONSTRAINT refund_access_decisions_check1 CHECK ((((treatment = 'Preserve'::text) = (effective_at IS NULL)) AND ((treatment = 'Preserve'::text) = (term_revision IS NULL)))),
    CONSTRAINT refund_access_decisions_check2 CHECK (((treatment <> 'Preserve'::text) OR ((NOT (new_paid_through IS DISTINCT FROM previous_paid_through)) AND (NOT (new_access_ends_at IS DISTINCT FROM previous_access_ends_at))))),
    CONSTRAINT refund_access_decisions_check3 CHECK (((treatment = 'Preserve'::text) OR ((term_id IS NOT NULL) AND (shop_count > 0) AND (effective_at > decided_at) AND (previous_paid_through IS NOT NULL) AND (previous_access_ends_at IS NOT NULL) AND (new_paid_through IS NOT NULL) AND (new_access_ends_at IS NOT NULL) AND (new_paid_through <= previous_paid_through) AND (new_access_ends_at <= previous_access_ends_at)))),
    CONSTRAINT refund_access_decisions_check4 CHECK (((treatment <> 'EndAt'::text) OR ((effective_at <= previous_access_ends_at) AND (new_access_ends_at = effective_at) AND (new_paid_through = LEAST(previous_paid_through, effective_at))))),
    CONSTRAINT refund_access_decisions_check5 CHECK (((treatment <> 'TermAdjustment'::text) OR ((effective_at < previous_paid_through) AND (new_paid_through = effective_at) AND (new_access_ends_at >= effective_at)))),
    CONSTRAINT refund_access_decisions_decided_at_check CHECK (isfinite(decided_at)),
    CONSTRAINT refund_access_decisions_effective_at_check CHECK (isfinite(effective_at)),
    CONSTRAINT refund_access_decisions_shop_count_check CHECK (((shop_count >= 0) AND (shop_count <= 100000))),
    CONSTRAINT refund_access_decisions_term_revision_check CHECK ((term_revision > 0)),
    CONSTRAINT refund_access_decisions_treatment_check CHECK ((treatment = ANY (ARRAY['Preserve'::text, 'EndAt'::text, 'TermAdjustment'::text])))
);

ALTER TABLE ONLY public.refund_access_decisions FORCE ROW LEVEL SECURITY;


--
-- Name: refund_applications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_applications (
    account_id uuid NOT NULL,
    decision_id uuid NOT NULL,
    disposition text NOT NULL,
    observed_at timestamp with time zone NOT NULL,
    CONSTRAINT refund_applications_disposition_check CHECK ((disposition = ANY (ARRAY['Applied'::text, 'NeedsReview'::text])))
);

ALTER TABLE ONLY public.refund_applications FORCE ROW LEVEL SECURITY;


--
-- Name: applied_refund_decisions; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.applied_refund_decisions WITH (security_invoker='true') AS
 SELECT d.account_id,
    d.decision_id,
    d.approval_id,
    d.refund_request_id,
    d.treatment,
    d.term_id,
    d.term_revision,
    d.effective_at,
    d.previous_paid_through,
    d.previous_access_ends_at,
    d.new_paid_through,
    d.new_access_ends_at,
    d.shop_count,
    d.shops,
    d.decided_at
   FROM (public.refund_access_decisions d
     JOIN public.refund_applications x USING (account_id, decision_id))
  WHERE (x.disposition = 'Applied'::text);


--
-- Name: billing_account_invitations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_account_invitations (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    invited_actor_id uuid NOT NULL,
    issuer_actor_id uuid NOT NULL,
    issuer_revision integer NOT NULL,
    account_name character varying(120) NOT NULL,
    proof_hash bytea NOT NULL,
    sealed_proof text,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revision integer DEFAULT 1 NOT NULL,
    accepted_at timestamp with time zone,
    accepted_member_id uuid,
    CONSTRAINT billing_account_invitations_check CHECK ((expires_at > created_at)),
    CONSTRAINT billing_account_invitations_check1 CHECK ((invited_actor_id <> issuer_actor_id)),
    CONSTRAINT billing_account_invitations_check2 CHECK ((((revision = 1) AND (accepted_at IS NULL) AND (accepted_member_id IS NULL) AND (sealed_proof IS NOT NULL)) OR ((revision = 2) AND (accepted_at IS NOT NULL) AND (accepted_member_id IS NOT NULL) AND (accepted_at >= created_at) AND (accepted_at < expires_at) AND (sealed_proof IS NULL)))),
    CONSTRAINT billing_account_invitations_issuer_revision_check CHECK ((issuer_revision > 0)),
    CONSTRAINT billing_account_invitations_proof_hash_check CHECK ((octet_length(proof_hash) = 32))
);

ALTER TABLE ONLY public.billing_account_invitations FORCE ROW LEVEL SECURITY;


--
-- Name: billing_account_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_account_members (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    merchant_user_id uuid NOT NULL,
    role integer NOT NULL,
    status integer NOT NULL,
    revision integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_billing_account_members_ids CHECK (((id <> '00000000-0000-0000-0000-000000000000'::uuid) AND (merchant_user_id <> '00000000-0000-0000-0000-000000000000'::uuid))),
    CONSTRAINT ck_billing_account_members_revision CHECK ((revision >= 1)),
    CONSTRAINT ck_billing_account_members_role CHECK ((role = ANY (ARRAY[1, 2]))),
    CONSTRAINT ck_billing_account_members_status CHECK ((status = ANY (ARRAY[1, 2])))
);

ALTER TABLE ONLY public.billing_account_members FORCE ROW LEVEL SECURITY;


--
-- Name: billing_accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_accounts (
    id uuid NOT NULL,
    name character varying(120) NOT NULL,
    created_by_merchant_user_id uuid NOT NULL,
    creation_key uuid NOT NULL,
    creation_request_hash bytea NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_billing_accounts_creation_request_hash CHECK ((octet_length(creation_request_hash) = 32)),
    CONSTRAINT ck_billing_accounts_ids CHECK (((id <> '00000000-0000-0000-0000-000000000000'::uuid) AND (created_by_merchant_user_id <> '00000000-0000-0000-0000-000000000000'::uuid) AND (creation_key <> '00000000-0000-0000-0000-000000000000'::uuid))),
    CONSTRAINT ck_billing_accounts_name CHECK ((((name)::text = btrim((name)::text)) AND ((char_length((name)::text) >= 1) AND (char_length((name)::text) <= 120))))
);

ALTER TABLE ONLY public.billing_accounts FORCE ROW LEVEL SECURITY;


--
-- Name: billing_document_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_document_versions (
    account_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_id uuid NOT NULL,
    version_number integer NOT NULL,
    supersedes_version_id uuid,
    order_id uuid NOT NULL,
    kind text NOT NULL,
    media_type text NOT NULL,
    file_name text NOT NULL,
    byte_length integer NOT NULL,
    content_sha256 text NOT NULL,
    content bytea NOT NULL,
    issuer_name text NOT NULL,
    issuer_reference text NOT NULL,
    issued_on date NOT NULL,
    operation_key uuid NOT NULL,
    payload_hash text NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    uploaded_at timestamp with time zone NOT NULL,
    supersession_reason text,
    CONSTRAINT billing_document_bytes CHECK (((octet_length(content) = byte_length) AND (content_sha256 = encode(sha256(content), 'hex'::text)) AND (SUBSTRING(content FROM 1 FOR 5) = '\x255044462d'::bytea))),
    CONSTRAINT billing_document_issue_date CHECK (((issued_on >= '2000-01-01'::date) AND (issued_on <= ((uploaded_at AT TIME ZONE 'Asia/Kolkata'::text))::date))),
    CONSTRAINT billing_document_lineage CHECK (((version_number = 1) = (supersedes_version_id IS NULL))),
    CONSTRAINT billing_document_supersession_reason CHECK ((((version_number = 1) AND (supersession_reason IS NULL)) OR ((version_number > 1) AND (supersession_reason IS NOT NULL) AND ((length(supersession_reason) >= 1) AND (length(supersession_reason) <= 400)) AND (supersession_reason = btrim(supersession_reason)) AND (supersession_reason !~ '[[:cntrl:]]'::text)))),
    CONSTRAINT billing_document_versions_byte_length_check CHECK (((byte_length >= 1) AND (byte_length <= 10485760))),
    CONSTRAINT billing_document_versions_content_sha256_check CHECK ((content_sha256 ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT billing_document_versions_file_name_check CHECK ((file_name ~ '^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,114}\.[Pp][Dd][Ff]$'::text)),
    CONSTRAINT billing_document_versions_issuer_name_check CHECK (((length(issuer_name) >= 1) AND (length(issuer_name) <= 200) AND (issuer_name = btrim(issuer_name)) AND (issuer_name !~ '[[:cntrl:]]'::text))),
    CONSTRAINT billing_document_versions_issuer_reference_check CHECK (((length(issuer_reference) >= 1) AND (length(issuer_reference) <= 64) AND (issuer_reference = btrim(issuer_reference)) AND (issuer_reference !~ '[[:cntrl:]]'::text))),
    CONSTRAINT billing_document_versions_kind_check CHECK ((kind = 'AccountingInvoice'::text)),
    CONSTRAINT billing_document_versions_media_type_check CHECK ((media_type = 'application/pdf'::text)),
    CONSTRAINT billing_document_versions_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT billing_document_versions_payload_hash_check CHECK ((payload_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT billing_document_versions_uploaded_at_check CHECK (isfinite(uploaded_at)),
    CONSTRAINT billing_document_versions_version_number_check CHECK ((version_number >= 1))
);

ALTER TABLE ONLY public.billing_document_versions FORCE ROW LEVEL SECURITY;


--
-- Name: billing_invitation_audit; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_invitation_audit (
    invitation_id uuid NOT NULL,
    account_id uuid NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    actor_id uuid NOT NULL,
    request_id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    action text NOT NULL,
    invitation_revision integer NOT NULL,
    member_id uuid,
    CONSTRAINT billing_invitation_audit_check CHECK ((((action = 'Issued'::text) AND (invitation_revision = 1) AND (member_id IS NULL)) OR ((action = 'Accepted'::text) AND (invitation_revision = 2) AND (member_id IS NOT NULL)))),
    CONSTRAINT billing_invitation_audit_version_check CHECK ((version = 1))
);

ALTER TABLE ONLY public.billing_invitation_audit FORCE ROW LEVEL SECURITY;


--
-- Name: billing_invitation_issue_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_invitation_issue_receipts (
    account_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash bytea NOT NULL,
    CONSTRAINT billing_invitation_issue_receipts_request_hash_check CHECK ((octet_length(request_hash) = 32))
);

ALTER TABLE ONLY public.billing_invitation_issue_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: billing_member_removals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_member_removals (
    version integer DEFAULT 1 NOT NULL,
    account_id uuid NOT NULL,
    member_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    actor_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    request_id uuid NOT NULL,
    request_hash bytea NOT NULL,
    reason text NOT NULL,
    removed_at timestamp with time zone NOT NULL,
    before_role integer DEFAULT 2 NOT NULL,
    after_role integer DEFAULT 2 NOT NULL,
    before_status integer DEFAULT 1 NOT NULL,
    after_status integer DEFAULT 2 NOT NULL,
    before_revision integer NOT NULL,
    after_revision integer NOT NULL,
    CONSTRAINT billing_member_removals_actor_revision_check CHECK ((actor_revision > 0)),
    CONSTRAINT billing_member_removals_after_role_check CHECK ((after_role = 2)),
    CONSTRAINT billing_member_removals_after_status_check CHECK ((after_status = 2)),
    CONSTRAINT billing_member_removals_before_revision_check CHECK ((before_revision > 0)),
    CONSTRAINT billing_member_removals_before_role_check CHECK ((before_role = 2)),
    CONSTRAINT billing_member_removals_before_status_check CHECK ((before_status = 1)),
    CONSTRAINT billing_member_removals_check CHECK ((after_revision = (before_revision + 1))),
    CONSTRAINT billing_member_removals_reason_check CHECK (((char_length(reason) >= 1) AND (char_length(reason) <= 500))),
    CONSTRAINT billing_member_removals_request_hash_check CHECK ((octet_length(request_hash) = 32)),
    CONSTRAINT billing_member_removals_version_check CHECK ((version = 1))
);

ALTER TABLE ONLY public.billing_member_removals FORCE ROW LEVEL SECURITY;


--
-- Name: billing_ownership_acceptances; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_ownership_acceptances (
    transfer_id uuid NOT NULL,
    account_id uuid NOT NULL,
    recipient_actor_id uuid NOT NULL,
    recipient_member_id uuid NOT NULL,
    recipient_before_revision integer NOT NULL,
    recipient_after_revision integer NOT NULL,
    owner_after_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    request_id uuid NOT NULL,
    request_hash bytea NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    CONSTRAINT billing_ownership_acceptances_check CHECK ((recipient_after_revision = (recipient_before_revision + 1))),
    CONSTRAINT billing_ownership_acceptances_owner_after_revision_check CHECK ((owner_after_revision > 1)),
    CONSTRAINT billing_ownership_acceptances_recipient_before_revision_check CHECK ((recipient_before_revision >= 0)),
    CONSTRAINT billing_ownership_acceptances_request_hash_check CHECK ((octet_length(request_hash) = 32))
);

ALTER TABLE ONLY public.billing_ownership_acceptances FORCE ROW LEVEL SECURITY;


--
-- Name: billing_ownership_proposals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.billing_ownership_proposals (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    account_name character varying(120) NOT NULL,
    owner_actor_id uuid NOT NULL,
    owner_member_id uuid NOT NULL,
    owner_revision integer NOT NULL,
    recipient_actor_id uuid,
    recipient_memberships jsonb,
    operation_key uuid NOT NULL,
    request_id uuid NOT NULL,
    request_hash bytea NOT NULL,
    reason text NOT NULL,
    responsibility_policy_version text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT billing_ownership_proposals_check CHECK ((recipient_actor_id <> owner_actor_id)),
    CONSTRAINT billing_ownership_proposals_check1 CHECK ((expires_at > created_at)),
    CONSTRAINT billing_ownership_proposals_check2 CHECK (((recipient_actor_id IS NULL) = (recipient_memberships IS NULL))),
    CONSTRAINT billing_ownership_proposals_owner_revision_check CHECK ((owner_revision > 0)),
    CONSTRAINT billing_ownership_proposals_reason_check CHECK (((char_length(reason) >= 1) AND (char_length(reason) <= 500))),
    CONSTRAINT billing_ownership_proposals_request_hash_check CHECK ((octet_length(request_hash) = 32)),
    CONSTRAINT billing_ownership_proposals_responsibility_policy_version_check CHECK ((responsibility_policy_version = 'prospective-billing-owner/1'::text))
);

ALTER TABLE ONLY public.billing_ownership_proposals FORCE ROW LEVEL SECURITY;


--
-- Name: bootstrap_enrollment_credentials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bootstrap_enrollment_credentials (
    id uuid NOT NULL,
    owner_id uuid NOT NULL,
    digest character varying(64) NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    issued_for_recovery boolean DEFAULT false NOT NULL,
    CONSTRAINT ck_bootstrap_enrollment_evidence CHECK ((((digest)::text ~ '^[0-9A-F]{64}$'::text) AND (issued_at < expires_at) AND ((revoked_at IS NULL) OR (revoked_at >= issued_at))))
);


--
-- Name: bootstrap_rate_buckets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bootstrap_rate_buckets (
    id integer NOT NULL,
    window_ends_at timestamp with time zone NOT NULL,
    attempts integer NOT NULL,
    CONSTRAINT ck_bootstrap_rate_bucket CHECK (((id >= 1) AND (id <= 5) AND (attempts >= 0)))
);


--
-- Name: capacity_change_quote_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.capacity_change_quote_shops (
    account_id uuid NOT NULL,
    quote_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    affiliation_request_id uuid NOT NULL,
    replay_id uuid NOT NULL,
    CONSTRAINT capacity_change_quote_shops_shop_name_check CHECK (((length(shop_name) >= 1) AND (length(shop_name) <= 250)))
);

ALTER TABLE ONLY public.capacity_change_quote_shops FORCE ROW LEVEL SECURITY;


--
-- Name: capacity_change_quotes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.capacity_change_quotes (
    account_id uuid NOT NULL,
    quote_id uuid NOT NULL,
    term_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    outcome text NOT NULL,
    reason text,
    source_plan_version_id uuid,
    target_plan_version_id uuid NOT NULL,
    prior_capacity integer NOT NULL,
    target_capacity integer NOT NULL,
    prior_member_allowance integer NOT NULL,
    target_member_allowance integer NOT NULL,
    shop_count integer NOT NULL,
    shop_hash text NOT NULL,
    source_hash text NOT NULL,
    purchased_gross_paise bigint,
    target_gross_paise bigint NOT NULL,
    difference_paise bigint,
    amount_paise bigint,
    interval_starts_at timestamp with time zone NOT NULL,
    paid_through timestamp with time zone NOT NULL,
    total_seconds bigint,
    remaining_seconds bigint,
    proration_version text NOT NULL,
    provider_id text,
    provider_account_id text,
    provider_environment text,
    provider_configuration_version text,
    routing_revision bigint,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    snapshot_hash text NOT NULL,
    snapshot jsonb NOT NULL,
    CONSTRAINT capacity_change_quotes_check CHECK (((target_capacity >= prior_capacity) AND (target_capacity <= 100000))),
    CONSTRAINT capacity_change_quotes_check1 CHECK (((target_member_allowance >= prior_member_allowance) AND (target_member_allowance <= 100000))),
    CONSTRAINT capacity_change_quotes_check10 CHECK (((outcome <> 'Payable'::text) OR ((num_nonnulls(purchased_gross_paise, difference_paise, total_seconds, remaining_seconds, provider_id, provider_account_id, provider_environment, provider_configuration_version, routing_revision) = 9) AND (created_at >= interval_starts_at) AND (created_at < paid_through)))),
    CONSTRAINT capacity_change_quotes_check11 CHECK (((outcome = 'Payable'::text) OR (num_nonnulls(provider_id, provider_account_id, provider_environment, provider_configuration_version, routing_revision) = 0))),
    CONSTRAINT capacity_change_quotes_check12 CHECK ((((difference_paise IS NULL) = (total_seconds IS NULL)) AND ((total_seconds IS NULL) = (remaining_seconds IS NULL)))),
    CONSTRAINT capacity_change_quotes_check2 CHECK (((shop_count >= 1) AND (shop_count <= prior_capacity))),
    CONSTRAINT capacity_change_quotes_check3 CHECK ((difference_paise = (target_gross_paise - purchased_gross_paise))),
    CONSTRAINT capacity_change_quotes_check4 CHECK (((amount_paise >= 1) AND (amount_paise <= difference_paise))),
    CONSTRAINT capacity_change_quotes_check5 CHECK ((isfinite(paid_through) AND (paid_through > interval_starts_at))),
    CONSTRAINT capacity_change_quotes_check6 CHECK (((total_seconds)::numeric = EXTRACT(epoch FROM (paid_through - interval_starts_at)))),
    CONSTRAINT capacity_change_quotes_check7 CHECK (((remaining_seconds >= 0) AND (remaining_seconds <= total_seconds))),
    CONSTRAINT capacity_change_quotes_check8 CHECK ((expires_at = (created_at + '00:15:00'::interval))),
    CONSTRAINT capacity_change_quotes_check9 CHECK (((outcome = 'Payable'::text) = (amount_paise IS NOT NULL))),
    CONSTRAINT capacity_change_quotes_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT capacity_change_quotes_interval_starts_at_check CHECK (isfinite(interval_starts_at)),
    CONSTRAINT capacity_change_quotes_outcome_check CHECK ((outcome = ANY (ARRAY['Payable'::text, 'AtRenewalOrCourtesy'::text, 'RenewalRequired'::text, 'NewTermRequired'::text, 'TrialConversion'::text, 'PaymentUnavailable'::text]))),
    CONSTRAINT capacity_change_quotes_prior_capacity_check CHECK (((prior_capacity >= 1) AND (prior_capacity <= 100000))),
    CONSTRAINT capacity_change_quotes_prior_member_allowance_check CHECK (((prior_member_allowance >= 0) AND (prior_member_allowance <= 100000))),
    CONSTRAINT capacity_change_quotes_proration_version_check CHECK ((proration_version = 'proration/1'::text)),
    CONSTRAINT capacity_change_quotes_provider_account_id_check CHECK (((length(provider_account_id) >= 1) AND (length(provider_account_id) <= 200))),
    CONSTRAINT capacity_change_quotes_provider_configuration_version_check CHECK (((length(provider_configuration_version) >= 1) AND (length(provider_configuration_version) <= 200))),
    CONSTRAINT capacity_change_quotes_provider_environment_check CHECK (((length(provider_environment) >= 1) AND (length(provider_environment) <= 40))),
    CONSTRAINT capacity_change_quotes_provider_id_check CHECK (((length(provider_id) >= 1) AND (length(provider_id) <= 80))),
    CONSTRAINT capacity_change_quotes_purchased_gross_paise_check CHECK (((purchased_gross_paise >= 0) AND (purchased_gross_paise <= '1000000000000'::bigint))),
    CONSTRAINT capacity_change_quotes_reason_check CHECK (((length(reason) >= 1) AND (length(reason) <= 80))),
    CONSTRAINT capacity_change_quotes_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_change_quotes_routing_revision_check CHECK ((routing_revision >= 0)),
    CONSTRAINT capacity_change_quotes_shop_hash_check CHECK ((shop_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_change_quotes_snapshot_check CHECK ((jsonb_typeof(snapshot) = 'object'::text)),
    CONSTRAINT capacity_change_quotes_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_change_quotes_source_hash_check CHECK ((source_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_change_quotes_target_gross_paise_check CHECK (((target_gross_paise >= 1) AND (target_gross_paise <= '1000000000000'::bigint)))
);

ALTER TABLE ONLY public.capacity_change_quotes FORCE ROW LEVEL SECURITY;


--
-- Name: capacity_increases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.capacity_increases (
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    order_id uuid NOT NULL,
    attempt_id uuid,
    receipt_id uuid,
    reserve_event uuid NOT NULL,
    consume_event uuid,
    target_revision bigint NOT NULL,
    state text NOT NULL,
    reason text,
    capacity integer NOT NULL,
    members integer NOT NULL,
    gross bigint NOT NULL,
    plan_version uuid NOT NULL,
    CONSTRAINT capacity_increases_capacity_check CHECK (((capacity >= 1) AND (capacity <= 100000))),
    CONSTRAINT capacity_increases_check CHECK ((num_nonnulls(attempt_id, receipt_id) = 1)),
    CONSTRAINT capacity_increases_check1 CHECK (((state = 'NeedsReview'::text) = (reason IS NOT NULL))),
    CONSTRAINT capacity_increases_gross_check CHECK (((gross >= 1) AND (gross <= '1000000000000'::bigint))),
    CONSTRAINT capacity_increases_members_check CHECK (((members >= 1) AND (members <= 100000))),
    CONSTRAINT capacity_increases_state_check CHECK ((state = ANY (ARRAY['Pending'::text, 'Applied'::text, 'NeedsReview'::text]))),
    CONSTRAINT capacity_increases_target_revision_check CHECK ((target_revision > 0))
);

ALTER TABLE ONLY public.capacity_increases FORCE ROW LEVEL SECURITY;


--
-- Name: capacity_reduction_preview_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.capacity_reduction_preview_shops (
    account_id uuid NOT NULL,
    preview_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    affiliation_request_id uuid NOT NULL,
    active_members integer NOT NULL,
    seat_hash text NOT NULL,
    exceeds_allowance boolean NOT NULL,
    replay_id uuid NOT NULL,
    CONSTRAINT capacity_reduction_preview_shops_active_members_check CHECK ((active_members >= 0)),
    CONSTRAINT capacity_reduction_preview_shops_seat_hash_check CHECK ((seat_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_reduction_preview_shops_shop_name_check CHECK (((length(shop_name) >= 1) AND (length(shop_name) <= 250)))
);

ALTER TABLE ONLY public.capacity_reduction_preview_shops FORCE ROW LEVEL SECURITY;


--
-- Name: capacity_reduction_previews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.capacity_reduction_previews (
    account_id uuid NOT NULL,
    preview_id uuid NOT NULL,
    term_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    source_plan_version_id uuid,
    target_plan_version_id uuid NOT NULL,
    prior_capacity integer NOT NULL,
    target_capacity integer NOT NULL,
    prior_member_allowance integer NOT NULL,
    target_member_allowance integer NOT NULL,
    target_mode text NOT NULL,
    source_shop_count integer NOT NULL,
    source_shop_hash text NOT NULL,
    source_hash text NOT NULL,
    observation_digest text NOT NULL,
    amount_paise bigint NOT NULL,
    current_starts_at timestamp with time zone NOT NULL,
    current_paid_through timestamp with time zone NOT NULL,
    renewal_ends_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    snapshot_hash text NOT NULL,
    snapshot jsonb NOT NULL,
    CONSTRAINT capacity_reduction_previews_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT capacity_reduction_previews_check CHECK (((target_capacity >= 1) AND (target_capacity <= prior_capacity))),
    CONSTRAINT capacity_reduction_previews_check1 CHECK (((target_member_allowance >= 1) AND (target_member_allowance <= prior_member_allowance))),
    CONSTRAINT capacity_reduction_previews_check2 CHECK (((target_capacity < prior_capacity) OR (target_member_allowance < prior_member_allowance))),
    CONSTRAINT capacity_reduction_previews_check3 CHECK (((source_shop_count >= 1) AND (source_shop_count <= prior_capacity))),
    CONSTRAINT capacity_reduction_previews_check4 CHECK ((isfinite(current_paid_through) AND (current_paid_through > current_starts_at))),
    CONSTRAINT capacity_reduction_previews_check5 CHECK ((isfinite(renewal_ends_at) AND (renewal_ends_at > current_paid_through))),
    CONSTRAINT capacity_reduction_previews_check6 CHECK ((expires_at = (created_at + '00:15:00'::interval))),
    CONSTRAINT capacity_reduction_previews_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT capacity_reduction_previews_current_starts_at_check CHECK (isfinite(current_starts_at)),
    CONSTRAINT capacity_reduction_previews_observation_digest_check CHECK ((observation_digest ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_reduction_previews_prior_capacity_check CHECK (((prior_capacity >= 1) AND (prior_capacity <= 100000))),
    CONSTRAINT capacity_reduction_previews_prior_member_allowance_check CHECK (((prior_member_allowance >= 1) AND (prior_member_allowance <= 100000))),
    CONSTRAINT capacity_reduction_previews_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_reduction_previews_snapshot_check CHECK ((jsonb_typeof(snapshot) = 'object'::text)),
    CONSTRAINT capacity_reduction_previews_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_reduction_previews_source_hash_check CHECK ((source_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_reduction_previews_source_shop_hash_check CHECK ((source_shop_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT capacity_reduction_previews_target_mode_check CHECK ((target_mode = ANY (ARRAY['Independent'::text, 'Grouped'::text])))
);

ALTER TABLE ONLY public.capacity_reduction_previews FORCE ROW LEVEL SECURITY;


--
-- Name: catalogue_draft_changes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.catalogue_draft_changes (
    id uuid NOT NULL,
    target_kind integer NOT NULL,
    target_id uuid NOT NULL,
    revision integer NOT NULL,
    actor_admin_id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    reason character varying(500),
    CONSTRAINT ck_catalogue_draft_changes_reason CHECK ((((reason)::text = btrim((reason)::text)) AND ((char_length((reason)::text) >= 1) AND (char_length((reason)::text) <= 500)))),
    CONSTRAINT ck_catalogue_draft_changes_revision CHECK ((revision >= 1)),
    CONSTRAINT ck_catalogue_draft_changes_target CHECK ((target_kind = ANY (ARRAY[1, 2])))
);

ALTER TABLE ONLY public.catalogue_draft_changes FORCE ROW LEVEL SECURITY;


--
-- Name: catalogue_preparations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.catalogue_preparations (
    admin_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    plan_id uuid,
    version_id uuid NOT NULL,
    expected_version integer NOT NULL,
    trial_policy_version_id uuid,
    reason character varying(500) NOT NULL,
    configuration_revision character varying(80) NOT NULL,
    snapshot text NOT NULL,
    snapshot_hash character varying(64) NOT NULL,
    prepared_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_catalogue_preparation CHECK (((expected_version > 0) AND ((snapshot_hash)::text ~ '^[A-F0-9]{64}$'::text) AND ((char_length((reason)::text) >= 1) AND (char_length((reason)::text) <= 500)) AND ((plan_id IS NULL) = (trial_policy_version_id IS NULL))))
);

ALTER TABLE ONLY public.catalogue_preparations FORCE ROW LEVEL SECURITY;


--
-- Name: catalogue_publications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.catalogue_publications (
    version_id uuid NOT NULL,
    admin_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    published_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY public.catalogue_publications FORCE ROW LEVEL SECURITY;


--
-- Name: catalogue_retirements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.catalogue_retirements (
    version_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    admin_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    expected_version integer NOT NULL,
    reason character varying(500) NOT NULL,
    retired_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_catalogue_retirement CHECK (((expected_version > 0) AND ((char_length(btrim((reason)::text)) >= 1) AND (char_length(btrim((reason)::text)) <= 500))))
);

ALTER TABLE ONLY public.catalogue_retirements FORCE ROW LEVEL SECURITY;


--
-- Name: checkout_order_acceptances; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkout_order_acceptances (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_revision integer NOT NULL,
    request_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    quote_revision integer NOT NULL,
    snapshot_hash text NOT NULL,
    customer_terms_version text NOT NULL,
    refund_policy_version text NOT NULL,
    accepted_at timestamp with time zone NOT NULL,
    CONSTRAINT checkout_order_acceptances_accepted_at_check CHECK (isfinite(accepted_at)),
    CONSTRAINT checkout_order_acceptances_customer_terms_version_check CHECK (((length(customer_terms_version) >= 1) AND (length(customer_terms_version) <= 80))),
    CONSTRAINT checkout_order_acceptances_membership_revision_check CHECK ((membership_revision > 0)),
    CONSTRAINT checkout_order_acceptances_quote_revision_check CHECK ((quote_revision = 1)),
    CONSTRAINT checkout_order_acceptances_refund_policy_version_check CHECK (((length(refund_policy_version) >= 1) AND (length(refund_policy_version) <= 80))),
    CONSTRAINT checkout_order_acceptances_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT checkout_order_acceptances_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text))
);

ALTER TABLE ONLY public.checkout_order_acceptances FORCE ROW LEVEL SECURITY;


--
-- Name: checkout_order_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkout_order_shops (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    affiliation_request_id uuid NOT NULL,
    replay_id uuid NOT NULL,
    CONSTRAINT checkout_order_shops_shop_name_check CHECK (((length(shop_name) >= 1) AND (length(shop_name) <= 250)))
);

ALTER TABLE ONLY public.checkout_order_shops FORCE ROW LEVEL SECURITY;


--
-- Name: checkout_orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkout_orders (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    term_id uuid,
    quote_revision integer NOT NULL,
    amount_paise bigint NOT NULL,
    shop_count integer NOT NULL,
    shop_hash text NOT NULL,
    snapshot_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    snapshot jsonb NOT NULL,
    CONSTRAINT checkout_orders_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT checkout_orders_check CHECK ((isfinite(expires_at) AND (expires_at = (created_at + '00:15:00'::interval)))),
    CONSTRAINT checkout_orders_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT checkout_orders_quote_revision_check CHECK ((quote_revision = 1)),
    CONSTRAINT checkout_orders_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT checkout_orders_shop_count_check CHECK (((shop_count >= 1) AND (shop_count <= 100000))),
    CONSTRAINT checkout_orders_shop_hash_check CHECK ((shop_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT checkout_orders_snapshot_check CHECK ((jsonb_typeof(snapshot) = 'object'::text)),
    CONSTRAINT checkout_orders_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text))
);

ALTER TABLE ONLY public.checkout_orders FORCE ROW LEVEL SECURITY;


--
-- Name: checkout_trial_sources; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkout_trial_sources (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    trial_term_id uuid NOT NULL,
    revision integer NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    CONSTRAINT checkout_trial_sources_ends_at_check CHECK (isfinite(ends_at)),
    CONSTRAINT checkout_trial_sources_revision_check CHECK ((revision >= 0))
);

ALTER TABLE ONLY public.checkout_trial_sources FORCE ROW LEVEL SECURITY;


--
-- Name: complimentary_grant_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.complimentary_grant_receipts (
    grant_id uuid NOT NULL,
    account_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    reason character varying(500) NOT NULL,
    payload_digest text NOT NULL,
    source_hash text NOT NULL,
    expected_version bigint NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    payment_order_id uuid,
    CONSTRAINT complimentary_grant_receipts_expected_version_check CHECK ((expected_version >= 0)),
    CONSTRAINT complimentary_grant_receipts_payload_digest_check CHECK ((payload_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT complimentary_grant_receipts_reason_check CHECK (((length(btrim((reason)::text)) > 0) AND ((reason)::text !~ '[[:cntrl:]]'::text))),
    CONSTRAINT complimentary_grant_receipts_source_hash_check CHECK ((source_hash ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.complimentary_grant_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: complimentary_grant_scopes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.complimentary_grant_scopes (
    account_id uuid NOT NULL,
    revision bigint NOT NULL,
    CONSTRAINT complimentary_grant_scopes_revision_check CHECK ((revision > 0))
);

ALTER TABLE ONLY public.complimentary_grant_scopes FORCE ROW LEVEL SECURITY;


--
-- Name: complimentary_grant_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.complimentary_grant_shops (
    grant_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY public.complimentary_grant_shops FORCE ROW LEVEL SECURITY;


--
-- Name: complimentary_grants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.complimentary_grants (
    grant_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    basis text NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    coverage_mode text NOT NULL,
    shop_capacity integer NOT NULL,
    member_allowance integer NOT NULL,
    policy_version text NOT NULL,
    granted_version bigint NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT complimentary_grants_basis_check CHECK ((basis = 'Complimentary'::text)),
    CONSTRAINT complimentary_grants_check CHECK ((isfinite(ends_at) AND (ends_at > starts_at))),
    CONSTRAINT complimentary_grants_check1 CHECK ((((coverage_mode = 'Independent'::text) AND (shop_capacity = 1)) OR ((coverage_mode = 'Grouped'::text) AND (shop_capacity > 1)))),
    CONSTRAINT complimentary_grants_granted_version_check CHECK ((granted_version > 0)),
    CONSTRAINT complimentary_grants_member_allowance_check CHECK (((member_allowance >= 1) AND (member_allowance <= 100000))),
    CONSTRAINT complimentary_grants_policy_version_check CHECK ((policy_version = 'complimentary-access/1'::text)),
    CONSTRAINT complimentary_grants_shop_capacity_check CHECK (((shop_capacity >= 1) AND (shop_capacity <= 100))),
    CONSTRAINT complimentary_grants_starts_at_check CHECK (isfinite(starts_at))
);

ALTER TABLE ONLY public.complimentary_grants FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_replacement_exception_approvals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_replacement_exception_approvals (
    approval_id uuid NOT NULL,
    exception_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    generation bigint NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    authorized_at timestamp with time zone NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    operation_key uuid NOT NULL,
    payload_digest text NOT NULL,
    expected_proposal_revision bigint NOT NULL,
    expected_source_hash text NOT NULL,
    expected_terms_digest text NOT NULL,
    expected_grant_version bigint NOT NULL,
    disposition text NOT NULL,
    reason character varying(500) NOT NULL,
    prepare_event uuid NOT NULL,
    prepare_expires_at timestamp with time zone NOT NULL,
    consume_event uuid,
    abort_event uuid,
    state text NOT NULL,
    outcome_reason text,
    merchant_permit_count integer,
    merchant_max_permit_end timestamp with time zone,
    merchant_permit_digest text,
    consume_receipt_event uuid,
    courtesy_grant_id uuid,
    courtesy_term_id uuid,
    applied_at timestamp with time zone,
    CONSTRAINT coverage_replacement_exception_ap_merchant_max_permit_end_check CHECK (((merchant_max_permit_end IS NULL) OR isfinite(merchant_max_permit_end))),
    CONSTRAINT coverage_replacement_exception_app_expected_grant_version_check CHECK ((expected_grant_version >= 0)),
    CONSTRAINT coverage_replacement_exception_app_merchant_permit_digest_check CHECK (((merchant_permit_digest IS NULL) OR (merchant_permit_digest ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT coverage_replacement_exception_appr_expected_terms_digest_check CHECK ((expected_terms_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_replacement_exception_appr_merchant_permit_count_check CHECK (((merchant_permit_count IS NULL) OR (merchant_permit_count >= 0))),
    CONSTRAINT coverage_replacement_exception_appro_expected_source_hash_check CHECK ((expected_source_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_replacement_exception_approva_prepare_expires_at_check CHECK (isfinite(prepare_expires_at)),
    CONSTRAINT coverage_replacement_exception_approvals_applied_at_check CHECK (((applied_at IS NULL) OR isfinite(applied_at))),
    CONSTRAINT coverage_replacement_exception_approvals_check CHECK ((isfinite(authorized_at) AND (authorized_at >= mfa_verified_at) AND (authorized_at < (mfa_verified_at + '00:05:00'::interval)))),
    CONSTRAINT coverage_replacement_exception_approvals_check1 CHECK (((state = 'Applied'::text) = (applied_at IS NOT NULL))),
    CONSTRAINT coverage_replacement_exception_approvals_check2 CHECK (((courtesy_grant_id IS NULL) = (courtesy_term_id IS NULL))),
    CONSTRAINT coverage_replacement_exception_approvals_check3 CHECK (((disposition <> 'Wait'::text) OR (courtesy_grant_id IS NULL))),
    CONSTRAINT coverage_replacement_exception_approvals_decided_at_check CHECK (isfinite(decided_at)),
    CONSTRAINT coverage_replacement_exception_approvals_disposition_check CHECK ((disposition = ANY (ARRAY['Wait'::text, 'CourtesyBridge'::text]))),
    CONSTRAINT coverage_replacement_exception_approvals_generation_check CHECK ((generation > 0)),
    CONSTRAINT coverage_replacement_exception_approvals_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT coverage_replacement_exception_approvals_outcome_reason_check CHECK (((outcome_reason IS NULL) OR ((length(outcome_reason) >= 1) AND (length(outcome_reason) <= 120)))),
    CONSTRAINT coverage_replacement_exception_approvals_payload_digest_check CHECK ((payload_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_replacement_exception_approvals_reason_check CHECK (((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500) AND ((reason)::text = btrim((reason)::text)) AND ((reason)::text !~ '[[:cntrl:]]'::text))),
    CONSTRAINT coverage_replacement_exception_approvals_state_check CHECK ((state = ANY (ARRAY['Preparing'::text, 'Consuming'::text, 'Applied'::text, 'Refused'::text, 'Aborting'::text, 'Aborted'::text]))),
    CONSTRAINT coverage_replacement_exception_expected_proposal_revision_check CHECK ((expected_proposal_revision > 0))
);

ALTER TABLE ONLY public.coverage_replacement_exception_approvals FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_replacement_exceptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_replacement_exceptions (
    exception_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    order_id uuid,
    proposal_revision bigint NOT NULL,
    outgoing_shop_id uuid NOT NULL,
    outgoing_shop_name text NOT NULL,
    outgoing_affiliation_id uuid NOT NULL,
    incoming_shop_id uuid NOT NULL,
    incoming_shop_name text NOT NULL,
    incoming_affiliation_id uuid NOT NULL,
    capacity integer NOT NULL,
    member_allowance integer NOT NULL,
    normal_renewal_at timestamp with time zone NOT NULL,
    requested_early_start_at timestamp with time zone NOT NULL,
    paid_cutover_at timestamp with time zone NOT NULL,
    overlap_disposition text NOT NULL,
    observed_permit_deadline timestamp with time zone,
    observed_at timestamp with time zone NOT NULL,
    last_contact_at timestamp with time zone,
    last_synchronized_at timestamp with time zone,
    observed_permit_count integer DEFAULT 0 NOT NULL,
    request_reason character varying(500) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    requester_id uuid NOT NULL,
    requester_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    source_hash text NOT NULL,
    terms_digest text NOT NULL,
    state text NOT NULL,
    reason text,
    outgoing_consent_event uuid,
    outgoing_revoke_event uuid,
    outgoing_actor_id uuid,
    outgoing_authority_version bigint,
    outgoing_active_members integer,
    outgoing_seat_hash text,
    outgoing_consented_at timestamp with time zone,
    incoming_consent_event uuid,
    incoming_revoke_event uuid,
    incoming_actor_id uuid,
    incoming_authority_version bigint,
    incoming_active_members integer,
    incoming_seat_hash text,
    incoming_consented_at timestamp with time zone,
    approved_at timestamp with time zone,
    applied_at timestamp with time zone,
    CONSTRAINT coverage_replacement_exceptions_capacity_check CHECK ((capacity > 0)),
    CONSTRAINT coverage_replacement_exceptions_check CHECK ((isfinite(expires_at) AND (created_at < expires_at) AND (expires_at <= paid_cutover_at) AND (expires_at <= normal_renewal_at))),
    CONSTRAINT coverage_replacement_exceptions_check1 CHECK ((outgoing_shop_id <> incoming_shop_id)),
    CONSTRAINT coverage_replacement_exceptions_check2 CHECK (((created_at < paid_cutover_at) AND (requested_early_start_at <= paid_cutover_at) AND (paid_cutover_at < normal_renewal_at))),
    CONSTRAINT coverage_replacement_exceptions_check3 CHECK (((overlap_disposition <> 'CourtesyBridge'::text) OR (requested_early_start_at < paid_cutover_at))),
    CONSTRAINT coverage_replacement_exceptions_check4 CHECK (((observed_permit_deadline IS NULL) OR (paid_cutover_at >= observed_permit_deadline))),
    CONSTRAINT coverage_replacement_exceptions_check5 CHECK (((state = 'Applied'::text) = (applied_at IS NOT NULL))),
    CONSTRAINT coverage_replacement_exceptions_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT coverage_replacement_exceptions_incoming_active_members_check CHECK (((incoming_active_members IS NULL) OR (incoming_active_members >= 1))),
    CONSTRAINT coverage_replacement_exceptions_incoming_seat_hash_check CHECK (((incoming_seat_hash IS NULL) OR (incoming_seat_hash ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT coverage_replacement_exceptions_incoming_shop_name_check CHECK (((length(incoming_shop_name) >= 1) AND (length(incoming_shop_name) <= 250))),
    CONSTRAINT coverage_replacement_exceptions_last_contact_at_check CHECK (((last_contact_at IS NULL) OR isfinite(last_contact_at))),
    CONSTRAINT coverage_replacement_exceptions_last_synchronized_at_check CHECK (((last_synchronized_at IS NULL) OR isfinite(last_synchronized_at))),
    CONSTRAINT coverage_replacement_exceptions_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT coverage_replacement_exceptions_normal_renewal_at_check CHECK (isfinite(normal_renewal_at)),
    CONSTRAINT coverage_replacement_exceptions_observed_at_check CHECK (isfinite(observed_at)),
    CONSTRAINT coverage_replacement_exceptions_observed_permit_count_check CHECK ((observed_permit_count >= 0)),
    CONSTRAINT coverage_replacement_exceptions_observed_permit_deadline_check CHECK (((observed_permit_deadline IS NULL) OR isfinite(observed_permit_deadline))),
    CONSTRAINT coverage_replacement_exceptions_outgoing_active_members_check CHECK (((outgoing_active_members IS NULL) OR (outgoing_active_members >= 1))),
    CONSTRAINT coverage_replacement_exceptions_outgoing_seat_hash_check CHECK (((outgoing_seat_hash IS NULL) OR (outgoing_seat_hash ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT coverage_replacement_exceptions_outgoing_shop_name_check CHECK (((length(outgoing_shop_name) >= 1) AND (length(outgoing_shop_name) <= 250))),
    CONSTRAINT coverage_replacement_exceptions_overlap_disposition_check CHECK ((overlap_disposition = ANY (ARRAY['Wait'::text, 'CourtesyBridge'::text]))),
    CONSTRAINT coverage_replacement_exceptions_paid_cutover_at_check CHECK (isfinite(paid_cutover_at)),
    CONSTRAINT coverage_replacement_exceptions_proposal_revision_check CHECK ((proposal_revision > 0)),
    CONSTRAINT coverage_replacement_exceptions_reason_check CHECK (((reason IS NULL) OR ((length(reason) >= 1) AND (length(reason) <= 120)))),
    CONSTRAINT coverage_replacement_exceptions_request_hash_check CHECK ((request_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_replacement_exceptions_request_reason_check CHECK (((length(btrim((request_reason)::text)) >= 1) AND (length(btrim((request_reason)::text)) <= 500) AND ((request_reason)::text !~ '[[:cntrl:]]'::text))),
    CONSTRAINT coverage_replacement_exceptions_requested_early_start_at_check CHECK (isfinite(requested_early_start_at)),
    CONSTRAINT coverage_replacement_exceptions_requester_revision_check CHECK ((requester_revision >= 0)),
    CONSTRAINT coverage_replacement_exceptions_source_hash_check CHECK ((source_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_replacement_exceptions_state_check CHECK ((state = ANY (ARRAY['AwaitingConsents'::text, 'ReadyForReview'::text, 'Preparing'::text, 'Consuming'::text, 'Applied'::text, 'Refused'::text, 'Blocked'::text, 'Aborting'::text]))),
    CONSTRAINT coverage_replacement_exceptions_terms_digest_check CHECK ((terms_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.coverage_replacement_exceptions FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_replacements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_replacements (
    replacement_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    outgoing_shop_id uuid NOT NULL,
    outgoing_shop_name text NOT NULL,
    outgoing_affiliation_id uuid NOT NULL,
    incoming_shop_id uuid NOT NULL,
    incoming_shop_name text NOT NULL,
    incoming_affiliation_id uuid NOT NULL,
    capacity integer NOT NULL,
    member_allowance integer NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    permit_deadline timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    requester_id uuid NOT NULL,
    requester_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    source_hash text NOT NULL,
    state text NOT NULL,
    reason text,
    consent_event uuid,
    revoke_event uuid,
    successor_term_id uuid,
    incoming_coverage_from timestamp with time zone,
    CONSTRAINT coverage_replacements_capacity_check CHECK ((capacity > 0)),
    CONSTRAINT coverage_replacements_check CHECK (((created_at < expires_at) AND (effective_at <= expires_at))),
    CONSTRAINT coverage_replacements_check1 CHECK ((incoming_shop_id <> outgoing_shop_id)),
    CONSTRAINT coverage_replacements_check2 CHECK (((state = 'Applied'::text) = (successor_term_id IS NOT NULL))),
    CONSTRAINT coverage_replacements_check3 CHECK (((successor_term_id IS NULL) = (incoming_coverage_from IS NULL))),
    CONSTRAINT coverage_replacements_check4 CHECK ((incoming_coverage_from >= effective_at)),
    CONSTRAINT coverage_replacements_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT coverage_replacements_state_check CHECK ((state = ANY (ARRAY['AwaitingConsent'::text, 'Scheduled'::text, 'Applied'::text, 'Blocked'::text])))
);

ALTER TABLE ONLY public.coverage_replacements FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_slot_assignment_deliveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_slot_assignment_deliveries (
    assignment_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    event_id uuid NOT NULL
);

ALTER TABLE ONLY public.coverage_slot_assignment_deliveries FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_slot_assignments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_slot_assignments (
    assignment_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    order_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    shop_name text NOT NULL,
    requester_id uuid NOT NULL,
    requester_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    capacity integer NOT NULL,
    member_allowance integer NOT NULL,
    cancellation_revision bigint DEFAULT 0 NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    coverage_ends_at timestamp with time zone NOT NULL,
    state text NOT NULL,
    reason text,
    consent_event uuid,
    applied_at timestamp with time zone,
    CONSTRAINT coverage_slot_assignments_cancellation_revision_check CHECK (((cancellation_revision >= 0) AND ((cancellation_revision % (2)::bigint) = 0))),
    CONSTRAINT coverage_slot_assignments_capacity_check CHECK ((capacity > 0)),
    CONSTRAINT coverage_slot_assignments_check CHECK (((created_at < expires_at) AND (expires_at <= coverage_ends_at))),
    CONSTRAINT coverage_slot_assignments_check1 CHECK (((state = 'Applied'::text) = (applied_at IS NOT NULL))),
    CONSTRAINT coverage_slot_assignments_check2 CHECK (((state = 'Refused'::text) = (reason IS NOT NULL))),
    CONSTRAINT coverage_slot_assignments_check3 CHECK (((state = ANY (ARRAY['Applied'::text, 'Refused'::text])) = (consent_event IS NOT NULL))),
    CONSTRAINT coverage_slot_assignments_check4 CHECK (((applied_at IS NULL) OR ((created_at <= applied_at) AND (applied_at < expires_at)))),
    CONSTRAINT coverage_slot_assignments_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT coverage_slot_assignments_state_check CHECK ((state = ANY (ARRAY['AwaitingConsent'::text, 'Applied'::text, 'Refused'::text, 'Expired'::text])))
);

ALTER TABLE ONLY public.coverage_slot_assignments FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_approvals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_approvals (
    transfer_id uuid NOT NULL,
    source_account_id uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_id uuid NOT NULL,
    source_term_id uuid NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    operation_key uuid NOT NULL,
    hash text NOT NULL,
    reason text NOT NULL,
    review_digest text NOT NULL,
    consents uuid[] NOT NULL,
    cutover_at timestamp with time zone NOT NULL,
    paid_through_at timestamp with time zone NOT NULL,
    prepare_event uuid NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    CONSTRAINT coverage_transfer_approvals_check CHECK ((destination_account_id <> source_account_id)),
    CONSTRAINT coverage_transfer_approvals_check1 CHECK ((paid_through_at >= cutover_at)),
    CONSTRAINT coverage_transfer_approvals_check2 CHECK ((decided_at < cutover_at)),
    CONSTRAINT coverage_transfer_approvals_consents_check CHECK ((cardinality(consents) = 3)),
    CONSTRAINT coverage_transfer_approvals_hash_check CHECK (((length(hash) = 64) AND (hash !~ '[^a-f0-9]'::text))),
    CONSTRAINT coverage_transfer_approvals_reason_check CHECK (((length(btrim(reason)) >= 1) AND (length(btrim(reason)) <= 500)))
);

ALTER TABLE ONLY public.coverage_transfer_approvals FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_bindings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_bindings (
    transfer_id uuid NOT NULL,
    predecessor_id uuid NOT NULL,
    source_account_id uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    source_term_id uuid NOT NULL,
    slot_origin_shop_id uuid NOT NULL,
    member_allowance integer NOT NULL,
    owner_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_revision bigint NOT NULL,
    cutover_at timestamp with time zone NOT NULL,
    paid_through_at timestamp with time zone NOT NULL,
    CONSTRAINT coverage_transfer_bindings_check CHECK (((cutover_at <= paid_through_at) AND (source_account_id <> destination_account_id))),
    CONSTRAINT coverage_transfer_bindings_member_allowance_check CHECK ((member_allowance > 0))
);

ALTER TABLE ONLY public.coverage_transfer_bindings FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_consents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_consents (
    event_id uuid NOT NULL,
    transfer_id uuid NOT NULL,
    source_account_id uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    purpose text NOT NULL,
    generation integer NOT NULL,
    kind text NOT NULL,
    account_id uuid NOT NULL,
    account_name character varying(120),
    actor_id uuid NOT NULL,
    member_id uuid NOT NULL,
    authority_revision bigint NOT NULL,
    proposal_revision bigint NOT NULL,
    terms_digest text NOT NULL,
    operation_key uuid,
    request_hash text,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT coverage_transfer_consents_authority_revision_check CHECK ((authority_revision >= 0)),
    CONSTRAINT coverage_transfer_consents_check CHECK ((((purpose = 'ShopOwner'::text) = (operation_key IS NULL)) AND ((operation_key IS NULL) = (request_hash IS NULL)))),
    CONSTRAINT coverage_transfer_consents_check1 CHECK ((account_id =
CASE purpose
    WHEN 'DestinationBillingOwner'::text THEN destination_account_id
    ELSE source_account_id
END)),
    CONSTRAINT coverage_transfer_consents_generation_check CHECK ((generation > 0)),
    CONSTRAINT coverage_transfer_consents_kind_check CHECK ((kind = ANY (ARRAY['Accepted'::text, 'Revoked'::text]))),
    CONSTRAINT coverage_transfer_consents_proposal_revision_check CHECK ((proposal_revision > 0)),
    CONSTRAINT coverage_transfer_consents_purpose_check CHECK ((purpose = ANY (ARRAY['SourceBillingOwner'::text, 'DestinationBillingOwner'::text, 'ShopOwner'::text]))),
    CONSTRAINT coverage_transfer_consents_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT coverage_transfer_consents_request_hash_check CHECK (((request_hash IS NULL) OR (request_hash ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT coverage_transfer_consents_terms_digest_check CHECK ((terms_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.coverage_transfer_consents FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_evidence; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_evidence (
    transfer_id uuid NOT NULL,
    side text NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    mode text NOT NULL,
    capacity integer NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    paid_through_at timestamp with time zone,
    access_ends_at timestamp with time zone,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT coverage_transfer_evidence_access_ends_at_check CHECK (((access_ends_at IS NULL) OR isfinite(access_ends_at))),
    CONSTRAINT coverage_transfer_evidence_capacity_check CHECK ((capacity > 0)),
    CONSTRAINT coverage_transfer_evidence_ends_at_check CHECK (isfinite(ends_at)),
    CONSTRAINT coverage_transfer_evidence_paid_through_at_check CHECK (((paid_through_at IS NULL) OR isfinite(paid_through_at))),
    CONSTRAINT coverage_transfer_evidence_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT coverage_transfer_evidence_side_check CHECK ((side = ANY (ARRAY['Source'::text, 'Destination'::text]))),
    CONSTRAINT coverage_transfer_evidence_starts_at_check CHECK (isfinite(starts_at))
);

ALTER TABLE ONLY public.coverage_transfer_evidence FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_progress; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_progress (
    transfer_id uuid NOT NULL,
    source_account_id uuid NOT NULL,
    sequence integer NOT NULL,
    state text NOT NULL,
    event_id uuid NOT NULL,
    reason text,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT coverage_transfer_progress_sequence_check CHECK ((sequence > 0)),
    CONSTRAINT coverage_transfer_progress_state_check CHECK ((state = ANY (ARRAY['Scheduled'::text, 'Consuming'::text, 'PendingActivation'::text, 'Applied'::text, 'NeedsReview'::text])))
);

ALTER TABLE ONLY public.coverage_transfer_progress FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_proposals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_proposals (
    transfer_id uuid NOT NULL,
    proposal_revision bigint NOT NULL,
    source_account_id uuid NOT NULL,
    source_account_name character varying(120) NOT NULL,
    destination_account_id uuid NOT NULL,
    source_term_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    source_affiliation_id uuid NOT NULL,
    treatment text NOT NULL,
    cutover_at timestamp with time zone,
    source_paid_through_at timestamp with time zone NOT NULL,
    observed_permit_count integer NOT NULL,
    observed_permit_deadline timestamp with time zone,
    last_contact_at timestamp with time zone,
    observed_at timestamp with time zone NOT NULL,
    permit_digest text NOT NULL,
    evidence_digest text NOT NULL,
    terms_digest text NOT NULL,
    request_reason character varying(500) NOT NULL,
    requester_id uuid NOT NULL,
    requester_member_id uuid NOT NULL,
    requester_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    state text NOT NULL,
    retired_reason text,
    retired_at timestamp with time zone,
    funding_digest text,
    CONSTRAINT coverage_transfer_proposals_check CHECK ((source_account_id <> destination_account_id)),
    CONSTRAINT coverage_transfer_proposals_check1 CHECK (((treatment = 'ReviewedAllocation'::text) = (cutover_at IS NOT NULL))),
    CONSTRAINT coverage_transfer_proposals_check2 CHECK (((cutover_at IS NULL) OR ((created_at < cutover_at) AND (cutover_at < source_paid_through_at)))),
    CONSTRAINT coverage_transfer_proposals_check3 CHECK (((created_at < expires_at) AND (expires_at <= source_paid_through_at))),
    CONSTRAINT coverage_transfer_proposals_check4 CHECK (((cutover_at IS NULL) OR (expires_at <= cutover_at))),
    CONSTRAINT coverage_transfer_proposals_check5 CHECK ((((state = 'Retired'::text) = (retired_reason IS NOT NULL)) AND ((state = 'Retired'::text) = (retired_at IS NOT NULL)))),
    CONSTRAINT coverage_transfer_proposals_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT coverage_transfer_proposals_cutover_at_check CHECK (((cutover_at IS NULL) OR isfinite(cutover_at))),
    CONSTRAINT coverage_transfer_proposals_evidence_digest_check CHECK ((evidence_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_transfer_proposals_expires_at_check CHECK (isfinite(expires_at)),
    CONSTRAINT coverage_transfer_proposals_funding_digest_check CHECK (((length(funding_digest) = 64) AND (funding_digest !~ '[^a-f0-9]'::text))),
    CONSTRAINT coverage_transfer_proposals_last_contact_at_check CHECK (((last_contact_at IS NULL) OR isfinite(last_contact_at))),
    CONSTRAINT coverage_transfer_proposals_observed_at_check CHECK (isfinite(observed_at)),
    CONSTRAINT coverage_transfer_proposals_observed_permit_count_check CHECK ((observed_permit_count >= 0)),
    CONSTRAINT coverage_transfer_proposals_observed_permit_deadline_check CHECK (((observed_permit_deadline IS NULL) OR isfinite(observed_permit_deadline))),
    CONSTRAINT coverage_transfer_proposals_permit_digest_check CHECK ((permit_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_transfer_proposals_proposal_revision_check CHECK ((proposal_revision > 0)),
    CONSTRAINT coverage_transfer_proposals_request_hash_check CHECK ((request_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_transfer_proposals_request_reason_check CHECK (((length(btrim((request_reason)::text)) >= 1) AND (length(btrim((request_reason)::text)) <= 500) AND ((request_reason)::text !~ '[[:cntrl:]]'::text))),
    CONSTRAINT coverage_transfer_proposals_requester_revision_check CHECK ((requester_revision > 0)),
    CONSTRAINT coverage_transfer_proposals_retired_at_check CHECK (((retired_at IS NULL) OR isfinite(retired_at))),
    CONSTRAINT coverage_transfer_proposals_retired_reason_check CHECK (((retired_reason IS NULL) OR ((length(retired_reason) >= 1) AND (length(retired_reason) <= 60)))),
    CONSTRAINT coverage_transfer_proposals_shop_name_check CHECK (((length(shop_name) >= 1) AND (length(shop_name) <= 250))),
    CONSTRAINT coverage_transfer_proposals_source_account_name_check CHECK ((length((source_account_name)::text) >= 1)),
    CONSTRAINT coverage_transfer_proposals_source_paid_through_at_check CHECK (isfinite(source_paid_through_at)),
    CONSTRAINT coverage_transfer_proposals_state_check CHECK ((state = ANY (ARRAY['Open'::text, 'Retired'::text]))),
    CONSTRAINT coverage_transfer_proposals_terms_digest_check CHECK ((terms_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_transfer_proposals_treatment_check CHECK ((treatment = ANY (ARRAY['RetainUntilPaidThrough'::text, 'ReviewedAllocation'::text])))
);

ALTER TABLE ONLY public.coverage_transfer_proposals FORCE ROW LEVEL SECURITY;


--
-- Name: shop_affiliations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_affiliations (
    request_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    term_id uuid,
    event_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    shop_name text NOT NULL,
    owner_name text NOT NULL,
    accepted_at timestamp with time zone NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    coverage_ends_at timestamp with time zone,
    CONSTRAINT shop_affiliations_authority_version_check CHECK ((authority_version >= 0)),
    CONSTRAINT shop_affiliations_check CHECK ((applied_at >= accepted_at)),
    CONSTRAINT shop_affiliations_check1 CHECK ((((term_id IS NULL) AND (coverage_ends_at IS NULL)) OR ((term_id IS NOT NULL) AND (coverage_ends_at > applied_at))))
);

ALTER TABLE ONLY public.shop_affiliations FORCE ROW LEVEL SECURITY;


--
-- Name: current_shop_affiliations; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.current_shop_affiliations WITH (security_invoker='true') AS
 SELECT a.request_id,
    a.account_id,
    a.shop_id,
    a.term_id,
    a.event_id,
    a.actor_id,
    a.membership_id,
    a.authority_version,
    a.shop_name,
    a.owner_name,
    a.accepted_at,
    a.applied_at,
    a.coverage_ends_at
   FROM public.shop_affiliations a
  WHERE (NOT (EXISTS ( SELECT
           FROM public.coverage_transfer_bindings b
          WHERE (b.predecessor_id = a.request_id))))
UNION ALL
 SELECT b.transfer_id AS request_id,
    b.destination_account_id AS account_id,
    b.shop_id,
    NULL::uuid AS term_id,
    b.transfer_id AS event_id,
    b.owner_id AS actor_id,
    b.membership_id,
    b.authority_revision AS authority_version,
    b.shop_name,
    ''::text AS owner_name,
    b.cutover_at AS accepted_at,
    b.cutover_at AS applied_at,
    NULL::timestamp with time zone AS coverage_ends_at
   FROM public.coverage_transfer_bindings b
  WHERE (((b.destination_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) OR (b.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid) OR (CURRENT_USER = pg_get_userbyid(( SELECT pg_class.relowner
           FROM pg_class
          WHERE (pg_class.oid = ('public.shop_affiliations'::regclass)::oid))))) AND (NOT (EXISTS ( SELECT
           FROM public.coverage_transfer_bindings next
          WHERE (next.predecessor_id = b.transfer_id)))));


--
-- Name: development_gateway_controls; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.development_gateway_controls (
    provider_order_reference character varying(120) NOT NULL,
    outcome text NOT NULL,
    capture_at timestamp with time zone,
    CONSTRAINT development_gateway_controls_capture_at_check CHECK (isfinite(capture_at)),
    CONSTRAINT development_gateway_controls_check CHECK (((outcome = 'Delayed'::text) = (capture_at IS NOT NULL))),
    CONSTRAINT development_gateway_controls_outcome_check CHECK ((outcome = ANY (ARRAY['Timeout'::text, 'Delayed'::text, 'Failed'::text])))
);

ALTER TABLE ONLY public.development_gateway_controls FORCE ROW LEVEL SECURITY;


--
-- Name: development_gateway_payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.development_gateway_payments (
    provider_payment_reference character varying(120) NOT NULL,
    provider_order_reference character varying(120) NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    state text NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    CONSTRAINT development_gateway_payments_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT development_gateway_payments_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT development_gateway_payments_state_check CHECK ((state = 'Captured'::text))
);

ALTER TABLE ONLY public.development_gateway_payments FORCE ROW LEVEL SECURITY;


--
-- Name: development_gateway_refunds; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.development_gateway_refunds (
    reference text NOT NULL,
    receipt text NOT NULL,
    payment_reference text NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    provider_account_id text NOT NULL,
    environment text NOT NULL,
    configuration_version text NOT NULL,
    CONSTRAINT development_gateway_refunds_amount_paise_check CHECK ((amount_paise > 0))
);

ALTER TABLE ONLY public.development_gateway_refunds FORCE ROW LEVEL SECURITY;


--
-- Name: development_gateway_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.development_gateway_sessions (
    provider_order_reference character varying(120) NOT NULL,
    account_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    order_id uuid,
    provider_account_id character varying(80) DEFAULT 'local-simulator'::character varying NOT NULL,
    environment character varying(20) DEFAULT 'Development'::character varying NOT NULL,
    configuration_version character varying(80) DEFAULT 'development-simulator/1'::character varying NOT NULL,
    CONSTRAINT development_gateway_sessions_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT development_gateway_sessions_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT development_gateway_sessions_environment_check CHECK (((environment)::text = 'Development'::text))
);

ALTER TABLE ONLY public.development_gateway_sessions FORCE ROW LEVEL SECURITY;


--
-- Name: development_refund_controls; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.development_refund_controls (
    receipt text NOT NULL,
    provider_account_id text NOT NULL,
    environment text NOT NULL,
    configuration_version text NOT NULL,
    outcome text NOT NULL,
    available_at timestamp with time zone,
    CONSTRAINT development_refund_controls_outcome_check CHECK ((outcome = ANY (ARRAY['Timeout'::text, 'Delayed'::text, 'Failed'::text, 'Pending'::text, 'Processed'::text, 'Mismatch'::text])))
);

ALTER TABLE ONLY public.development_refund_controls FORCE ROW LEVEL SECURITY;


--
-- Name: entitlement_account_heads; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_account_heads (
    account_id uuid NOT NULL,
    revision bigint NOT NULL,
    upstream_verified_at timestamp with time zone NOT NULL,
    restriction text NOT NULL,
    released_revision bigint,
    previous_release_revision bigint,
    CONSTRAINT entitlement_account_heads_check CHECK (((released_revision IS NULL) OR ((restriction = 'Unrestricted'::text) AND (released_revision > 0) AND (released_revision < revision)))),
    CONSTRAINT entitlement_account_heads_check1 CHECK (((previous_release_revision IS NULL) OR ((restriction = ANY (ARRAY['Trading'::text, 'Security'::text])) AND (previous_release_revision > 0) AND (previous_release_revision < revision)))),
    CONSTRAINT entitlement_account_heads_restriction_check CHECK ((restriction = ANY (ARRAY['Unrestricted'::text, 'Trading'::text, 'Security'::text]))),
    CONSTRAINT entitlement_account_heads_revision_check CHECK ((revision > 0)),
    CONSTRAINT entitlement_account_heads_upstream_verified_at_check CHECK (isfinite(upstream_verified_at))
);

ALTER TABLE ONLY public.entitlement_account_heads FORCE ROW LEVEL SECURITY;


--
-- Name: entitlement_messages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_messages (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    kind text NOT NULL,
    payload bytea NOT NULL,
    digest text NOT NULL,
    CONSTRAINT entitlement_messages_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT entitlement_messages_kind_check CHECK ((kind = ANY (ARRAY['Snapshot'::text, 'Acknowledgement'::text, 'Reconciliation'::text, 'CapacityReserve'::text, 'CapacityConsume'::text, 'ReductionManifest'::text, 'ReductionReserve'::text, 'ReductionConsume'::text, 'CoverageExceptionPrepare'::text, 'CoverageExceptionConsume'::text, 'CoverageExceptionAbort'::text, 'TransferPrepare'::text, 'TransferConsume'::text, 'TransferAbort'::text]))),
    CONSTRAINT entitlement_messages_payload_check CHECK (((octet_length(payload) >= 1) AND (octet_length(payload) <= 8192)))
);

ALTER TABLE ONLY public.entitlement_messages FORCE ROW LEVEL SECURITY;


--
-- Name: entitlement_outbox; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_outbox (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    status text DEFAULT 'Pending'::text NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    lease_token uuid,
    lease_expires_at timestamp with time zone,
    attempts integer DEFAULT 0 NOT NULL,
    last_failure text,
    CONSTRAINT entitlement_outbox_attempts_check CHECK (((attempts >= 0) AND (attempts <= 12))),
    CONSTRAINT entitlement_outbox_check CHECK (((status = 'Leased'::text) = ((lease_token IS NOT NULL) AND (lease_expires_at IS NOT NULL)))),
    CONSTRAINT entitlement_outbox_check1 CHECK (((lease_token IS NULL) = (lease_expires_at IS NULL))),
    CONSTRAINT entitlement_outbox_last_failure_check CHECK ((last_failure = ANY (ARRAY['Unavailable'::text, 'Rejected'::text, 'InvalidResponse'::text, 'AcknowledgementMissing'::text, 'LeaseExpired'::text]))),
    CONSTRAINT entitlement_outbox_lease_expires_at_check CHECK (isfinite(lease_expires_at)),
    CONSTRAINT entitlement_outbox_next_attempt_at_check CHECK (isfinite(next_attempt_at)),
    CONSTRAINT entitlement_outbox_status_check CHECK ((status = ANY (ARRAY['Pending'::text, 'Leased'::text, 'AwaitingAcknowledgement'::text, 'Delivered'::text, 'Applied'::text, 'NeedsReview'::text, 'DeadLetter'::text])))
);

ALTER TABLE ONLY public.entitlement_outbox FORCE ROW LEVEL SECURITY;


--
-- Name: entitlement_request_uses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_request_uses (
    replay_id uuid NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT entitlement_request_uses_expires_at_check CHECK (isfinite(expires_at))
);

ALTER TABLE ONLY public.entitlement_request_uses FORCE ROW LEVEL SECURITY;


--
-- Name: entitlement_shop_heads; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_shop_heads (
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    revision bigint NOT NULL,
    next_verification_at timestamp with time zone NOT NULL,
    CONSTRAINT entitlement_shop_heads_next_verification_at_check CHECK (isfinite(next_verification_at)),
    CONSTRAINT entitlement_shop_heads_revision_check CHECK ((revision > 0))
);

ALTER TABLE ONLY public.entitlement_shop_heads FORCE ROW LEVEL SECURITY;


--
-- Name: gateway_inbox_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gateway_inbox_events (
    inbox_id uuid NOT NULL,
    provider_id character varying(80) NOT NULL,
    provider_account_id character varying(80) NOT NULL,
    environment text NOT NULL,
    configuration_version character varying(80) NOT NULL,
    provider_event_id character varying(120) NOT NULL,
    kind text NOT NULL,
    provider_order_reference character varying(120),
    provider_payment_reference character varying(120),
    body_sha256 bytea NOT NULL,
    protected_evidence bytea NOT NULL,
    received_at timestamp with time zone NOT NULL,
    CONSTRAINT gateway_inbox_events_body_sha256_check CHECK ((octet_length(body_sha256) = 32)),
    CONSTRAINT gateway_inbox_events_environment_check CHECK ((environment = ANY (ARRAY['Development'::text, 'Test'::text, 'Live'::text]))),
    CONSTRAINT gateway_inbox_events_kind_check CHECK ((kind = ANY (ARRAY['PaymentCaptured'::text, 'PaymentAuthorized'::text, 'PaymentFailed'::text, 'Other'::text, 'RefundChanged'::text]))),
    CONSTRAINT gateway_inbox_events_protected_evidence_check CHECK (((octet_length(protected_evidence) >= 1) AND (octet_length(protected_evidence) <= 4096))),
    CONSTRAINT gateway_inbox_events_provider_event_id_check CHECK ((length((provider_event_id)::text) > 0)),
    CONSTRAINT gateway_inbox_events_received_at_check CHECK (isfinite(received_at))
);

ALTER TABLE ONLY public.gateway_inbox_events FORCE ROW LEVEL SECURITY;


--
-- Name: gateway_inbox_work; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.gateway_inbox_work (
    inbox_id uuid NOT NULL,
    status text DEFAULT 'Pending'::text NOT NULL,
    outcome text,
    last_failure text,
    attempts integer DEFAULT 0 NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    lease_token uuid,
    lease_expires_at timestamp with time zone,
    account_id uuid,
    attempt_id uuid,
    finished_at timestamp with time zone,
    CONSTRAINT gateway_inbox_work_attempts_check CHECK (((attempts >= 0) AND (attempts <= 12))),
    CONSTRAINT gateway_inbox_work_check CHECK (((status = 'Leased'::text) = (lease_token IS NOT NULL))),
    CONSTRAINT gateway_inbox_work_check1 CHECK (((lease_token IS NULL) = (lease_expires_at IS NULL))),
    CONSTRAINT gateway_inbox_work_check2 CHECK (((status = ANY (ARRAY['Processed'::text, 'DeadLetter'::text])) = (finished_at IS NOT NULL))),
    CONSTRAINT gateway_inbox_work_check3 CHECK (((status = 'Processed'::text) = (outcome IS NOT NULL))),
    CONSTRAINT gateway_inbox_work_check4 CHECK (((status <> 'DeadLetter'::text) OR (last_failure IS NOT NULL))),
    CONSTRAINT gateway_inbox_work_finished_at_check CHECK (isfinite(finished_at)),
    CONSTRAINT gateway_inbox_work_last_failure_check CHECK ((last_failure = ANY (ARRAY['UnknownOrder'::text, 'AmbiguousOrder'::text, 'AdapterUnavailable'::text, 'CaptureUnproven'::text, 'DifferentPayment'::text, 'Busy'::text, 'Unavailable'::text, 'LeaseExpired'::text]))),
    CONSTRAINT gateway_inbox_work_lease_expires_at_check CHECK (isfinite(lease_expires_at)),
    CONSTRAINT gateway_inbox_work_next_attempt_at_check CHECK (isfinite(next_attempt_at)),
    CONSTRAINT gateway_inbox_work_outcome_check CHECK ((outcome = ANY (ARRAY['Fulfilled'::text, 'VerifiedNeedsReview'::text, 'AlreadyVerified'::text, 'NoAction'::text]))),
    CONSTRAINT gateway_inbox_work_status_check CHECK ((status = ANY (ARRAY['Pending'::text, 'Leased'::text, 'Processed'::text, 'DeadLetter'::text])))
);

ALTER TABLE ONLY public.gateway_inbox_work FORCE ROW LEVEL SECURITY;


--
-- Name: initial_admin_bootstrap; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.initial_admin_bootstrap (
    id integer NOT NULL,
    proof_digest character varying(64),
    verified_email character varying(254),
    verification_reference character varying(128),
    admin_origin character varying(255),
    prepared_at timestamp with time zone,
    expires_at timestamp with time zone,
    consumed_at timestamp with time zone,
    owner_id uuid,
    CONSTRAINT ck_bootstrap_preparation CHECK ((((num_nonnulls(proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at) = 0) OR ((num_nonnulls(proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at) = 6) AND ((proof_digest)::text ~ '^[0-9A-F]{64}$'::text) AND (prepared_at < expires_at))) AND (((owner_id IS NULL) AND (consumed_at IS NULL)) OR ((owner_id IS NOT NULL) AND (consumed_at IS NOT NULL) AND (prepared_at IS NOT NULL) AND (prepared_at <= consumed_at) AND (consumed_at < expires_at))))),
    CONSTRAINT ck_bootstrap_singleton CHECK ((id = 1))
);


--
-- Name: manual_payment_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.manual_payment_receipts (
    receipt_id uuid NOT NULL,
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL,
    verified_at timestamp with time zone NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    receiving_account_id character varying(80) NOT NULL,
    configuration_version character varying(80) NOT NULL,
    evidence_policy_version character varying(80) NOT NULL,
    environment text NOT NULL,
    reference_digest text NOT NULL,
    protected_payload text NOT NULL,
    quote_revision integer NOT NULL,
    snapshot_hash text NOT NULL,
    review_reason text,
    term_id uuid,
    CONSTRAINT manual_payment_receipts_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT manual_payment_receipts_check CHECK ((isfinite(verified_at) AND (received_at <= verified_at))),
    CONSTRAINT manual_payment_receipts_check1 CHECK (((term_id IS NULL) <> (review_reason IS NULL))),
    CONSTRAINT manual_payment_receipts_configuration_version_check CHECK ((length((configuration_version)::text) > 0)),
    CONSTRAINT manual_payment_receipts_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT manual_payment_receipts_environment_check CHECK ((environment = ANY (ARRAY['Development'::text, 'Test'::text, 'Live'::text]))),
    CONSTRAINT manual_payment_receipts_evidence_policy_version_check CHECK ((length((evidence_policy_version)::text) > 0)),
    CONSTRAINT manual_payment_receipts_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT manual_payment_receipts_protected_payload_check CHECK (((length(protected_payload) >= 1) AND (length(protected_payload) <= 16384))),
    CONSTRAINT manual_payment_receipts_quote_revision_check CHECK ((quote_revision > 0)),
    CONSTRAINT manual_payment_receipts_received_at_check CHECK (isfinite(received_at)),
    CONSTRAINT manual_payment_receipts_receiving_account_id_check CHECK ((length((receiving_account_id)::text) > 0)),
    CONSTRAINT manual_payment_receipts_reference_digest_check CHECK ((reference_digest ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT manual_payment_receipts_review_reason_check CHECK ((review_reason = ANY (ARRAY['DuplicateFunds'::text, 'CoverageChanged'::text, 'TermUnavailable'::text, 'RestorationPending'::text, 'CapacityPending'::text, 'ReductionPending'::text]))),
    CONSTRAINT manual_payment_receipts_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text))
);

ALTER TABLE ONLY public.manual_payment_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: merchant_billing_delegation_uses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merchant_billing_delegation_uses (
    issuer character varying(64) NOT NULL,
    replay_id uuid NOT NULL,
    account_id uuid,
    actor_id uuid NOT NULL,
    action character varying(64) NOT NULL,
    request_id uuid NOT NULL,
    key_id character varying(128) NOT NULL,
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_merchant_billing_delegation_use_ids CHECK (((replay_id <> '00000000-0000-0000-0000-000000000000'::uuid) AND ((account_id IS NULL) OR (account_id <> '00000000-0000-0000-0000-000000000000'::uuid)) AND (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid) AND (request_id <> '00000000-0000-0000-0000-000000000000'::uuid) AND (request_id <> replay_id) AND ((key_id)::text ~ '^[A-Za-z0-9_-]{1,128}$'::text))),
    CONSTRAINT ck_merchant_billing_delegation_use_window CHECK (((issued_at <= consumed_at) AND (consumed_at < expires_at) AND ((expires_at - issued_at) <= '00:01:00'::interval)))
);

ALTER TABLE ONLY public.merchant_billing_delegation_uses FORCE ROW LEVEL SECURITY;


--
-- Name: offline_device_observations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.offline_device_observations (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    device_ref text NOT NULL,
    observation text NOT NULL,
    observed_at timestamp with time zone NOT NULL,
    permit_id uuid,
    not_before timestamp with time zone,
    valid_until timestamp with time zone,
    permit_limit text,
    account_revision bigint,
    shop_revision bigint,
    authority_verified_at timestamp with time zone,
    CONSTRAINT offline_device_observations_account_revision_check CHECK ((account_revision > 0)),
    CONSTRAINT offline_device_observations_authority_verified_at_check CHECK (isfinite(authority_verified_at)),
    CONSTRAINT offline_device_observations_check CHECK (
CASE
    WHEN (observation = 'PermitIssued'::text) THEN ((permit_id IS NOT NULL) AND (not_before IS NOT NULL) AND (valid_until IS NOT NULL) AND (permit_limit IS NOT NULL) AND (account_revision IS NOT NULL) AND (shop_revision IS NOT NULL) AND (authority_verified_at IS NOT NULL) AND (not_before < valid_until) AND (observed_at = not_before))
    ELSE (num_nonnulls(permit_id, not_before, valid_until, permit_limit, account_revision, shop_revision, authority_verified_at) = 0)
END),
    CONSTRAINT offline_device_observations_device_ref_check CHECK ((device_ref ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT offline_device_observations_not_before_check CHECK (isfinite(not_before)),
    CONSTRAINT offline_device_observations_observation_check CHECK ((observation = ANY (ARRAY['PermitIssued'::text, 'OfflineSync'::text]))),
    CONSTRAINT offline_device_observations_observed_at_check CHECK (isfinite(observed_at)),
    CONSTRAINT offline_device_observations_permit_limit_check CHECK ((permit_limit = ANY (ARRAY['LicenceEnd'::text, 'VerificationFreshness'::text, 'MerchantLimit'::text]))),
    CONSTRAINT offline_device_observations_shop_revision_check CHECK ((shop_revision > 0)),
    CONSTRAINT offline_device_observations_valid_until_check CHECK (isfinite(valid_until))
);

ALTER TABLE ONLY public.offline_device_observations FORCE ROW LEVEL SECURITY;


--
-- Name: operational_closure_consent_notices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_closure_consent_notices (
    proposal_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL
);

ALTER TABLE ONLY public.operational_closure_consent_notices FORCE ROW LEVEL SECURITY;


--
-- Name: operational_closure_proposals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_closure_proposals (
    id uuid NOT NULL,
    ordinal bigint NOT NULL,
    account_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    billing_revision integer NOT NULL,
    financial_treatment text NOT NULL,
    scope_digest text NOT NULL,
    scope jsonb NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    operation_key uuid,
    CONSTRAINT operational_closure_proposals_billing_revision_check CHECK ((billing_revision > 0)),
    CONSTRAINT operational_closure_proposals_check CHECK ((expires_at > recorded_at)),
    CONSTRAINT operational_closure_proposals_financial_treatment_check CHECK ((financial_treatment = 'NoFinancialChange'::text)),
    CONSTRAINT operational_closure_proposals_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT operational_closure_proposals_scope_check CHECK ((jsonb_typeof(scope) = 'array'::text)),
    CONSTRAINT operational_closure_proposals_scope_digest_check CHECK ((scope_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.operational_closure_proposals FORCE ROW LEVEL SECURITY;


--
-- Name: operational_closure_proposals_ordinal_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.operational_closure_proposals ALTER COLUMN ordinal ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.operational_closure_proposals_ordinal_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: operational_closures; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_closures (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    shop_id uuid,
    operation_key uuid NOT NULL,
    request_digest text NOT NULL,
    financial_treatment text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT operational_closures_financial_treatment_check CHECK ((financial_treatment = 'NoFinancialChange'::text)),
    CONSTRAINT operational_closures_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT operational_closures_request_digest_check CHECK ((request_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.operational_closures FORCE ROW LEVEL SECURITY;


--
-- Name: operational_restriction_deliveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_restriction_deliveries (
    restriction_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    event_id uuid NOT NULL
);

ALTER TABLE ONLY public.operational_restriction_deliveries FORCE ROW LEVEL SECURITY;


--
-- Name: operational_restriction_release_deliveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operational_restriction_release_deliveries (
    release_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    event_id uuid NOT NULL
);

ALTER TABLE ONLY public.operational_restriction_release_deliveries FORCE ROW LEVEL SECURITY;


--
-- Name: paid_fresh_starts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.paid_fresh_starts (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    term_id uuid NOT NULL,
    kind text NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    ready_at timestamp with time zone NOT NULL,
    account_revision bigint NOT NULL,
    released_revision bigint,
    upstream_verified_at timestamp with time zone NOT NULL,
    CONSTRAINT paid_fresh_starts_account_revision_check CHECK ((account_revision > 0)),
    CONSTRAINT paid_fresh_starts_check CHECK ((isfinite(ready_at) AND (ready_at >= effective_at))),
    CONSTRAINT paid_fresh_starts_check1 CHECK (((released_revision > 0) AND (released_revision < account_revision))),
    CONSTRAINT paid_fresh_starts_effective_at_check CHECK (isfinite(effective_at)),
    CONSTRAINT paid_fresh_starts_kind_check CHECK ((kind = ANY (ARRAY['FirstPurchase'::text, 'TrialConversion'::text, 'RestoredAccess'::text]))),
    CONSTRAINT paid_fresh_starts_upstream_verified_at_check CHECK (isfinite(upstream_verified_at))
);

ALTER TABLE ONLY public.paid_fresh_starts FORCE ROW LEVEL SECURITY;


--
-- Name: paid_slot_handoffs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.paid_slot_handoffs (
    handoff_id uuid NOT NULL,
    exception_id uuid NOT NULL,
    approval_id uuid NOT NULL,
    generation bigint NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    slot_origin_shop_id uuid NOT NULL,
    slot_generation integer NOT NULL,
    outgoing_shop_id uuid NOT NULL,
    outgoing_affiliation_id uuid NOT NULL,
    incoming_shop_id uuid NOT NULL,
    incoming_affiliation_id uuid NOT NULL,
    previous_starts_at timestamp with time zone NOT NULL,
    cutover_at timestamp with time zone NOT NULL,
    original_ends_at timestamp with time zone NOT NULL,
    courtesy_grant_id uuid,
    courtesy_term_id uuid,
    courtesy_starts_at timestamp with time zone,
    consume_receipt_event uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT paid_slot_handoffs_check CHECK ((outgoing_shop_id <> incoming_shop_id)),
    CONSTRAINT paid_slot_handoffs_check1 CHECK (((previous_starts_at < cutover_at) AND (cutover_at < original_ends_at))),
    CONSTRAINT paid_slot_handoffs_check2 CHECK ((((courtesy_grant_id IS NULL) = (courtesy_term_id IS NULL)) AND ((courtesy_grant_id IS NULL) = (courtesy_starts_at IS NULL)))),
    CONSTRAINT paid_slot_handoffs_check3 CHECK (((courtesy_starts_at IS NULL) OR ((courtesy_starts_at >= recorded_at) AND (courtesy_starts_at < cutover_at)))),
    CONSTRAINT paid_slot_handoffs_courtesy_starts_at_check CHECK (((courtesy_starts_at IS NULL) OR isfinite(courtesy_starts_at))),
    CONSTRAINT paid_slot_handoffs_cutover_at_check CHECK (isfinite(cutover_at)),
    CONSTRAINT paid_slot_handoffs_generation_check CHECK ((generation > 0)),
    CONSTRAINT paid_slot_handoffs_original_ends_at_check CHECK (isfinite(original_ends_at)),
    CONSTRAINT paid_slot_handoffs_previous_starts_at_check CHECK (isfinite(previous_starts_at)),
    CONSTRAINT paid_slot_handoffs_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT paid_slot_handoffs_slot_generation_check CHECK ((slot_generation > 0))
);

ALTER TABLE ONLY public.paid_slot_handoffs FORCE ROW LEVEL SECURITY;


--
-- Name: payment_attempt_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_attempt_sessions (
    attempt_id uuid NOT NULL,
    account_id uuid NOT NULL,
    provider_order_reference character varying(120) NOT NULL,
    launcher_flow character varying(40) NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT payment_attempt_sessions_launcher_flow_check CHECK ((length((launcher_flow)::text) > 0)),
    CONSTRAINT payment_attempt_sessions_provider_order_reference_check CHECK ((length((provider_order_reference)::text) > 0)),
    CONSTRAINT payment_attempt_sessions_recorded_at_check CHECK (isfinite(recorded_at))
);

ALTER TABLE ONLY public.payment_attempt_sessions FORCE ROW LEVEL SECURITY;


--
-- Name: payment_attempts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_attempts (
    attempt_id uuid NOT NULL,
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    actor_id uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    provider_id character varying(40) NOT NULL,
    provider_account_id character varying(80) NOT NULL,
    environment character varying(20) NOT NULL,
    configuration_version character varying(80) NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    snapshot_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    selection_revision bigint,
    CONSTRAINT payment_attempts_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT payment_attempts_configuration_version_check CHECK ((length((configuration_version)::text) > 0)),
    CONSTRAINT payment_attempts_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT payment_attempts_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT payment_attempts_environment_check CHECK (((environment)::text = ANY (ARRAY[('Development'::character varying)::text, ('Test'::character varying)::text, ('Live'::character varying)::text]))),
    CONSTRAINT payment_attempts_provider_account_id_check CHECK ((length((provider_account_id)::text) > 0)),
    CONSTRAINT payment_attempts_provider_id_check CHECK ((length((provider_id)::text) > 0)),
    CONSTRAINT payment_attempts_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT payment_attempts_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text))
);

ALTER TABLE ONLY public.payment_attempts FORCE ROW LEVEL SECURITY;


--
-- Name: payment_confirmations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_confirmations (
    confirmation_id uuid NOT NULL,
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    attempt_id uuid,
    manual_receipt_id uuid,
    issued_at timestamp with time zone NOT NULL,
    facts jsonb NOT NULL,
    CONSTRAINT payment_confirmations_check CHECK (((attempt_id IS NULL) <> (manual_receipt_id IS NULL))),
    CONSTRAINT payment_confirmations_facts_check CHECK (((jsonb_typeof(facts) = 'object'::text) AND ((facts ->> 'policy'::text) = 'payment-confirmation/1'::text))),
    CONSTRAINT payment_confirmations_issued_at_check CHECK (isfinite(issued_at))
);

ALTER TABLE ONLY public.payment_confirmations FORCE ROW LEVEL SECURITY;


--
-- Name: payment_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_events (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    order_id uuid NOT NULL,
    kind text NOT NULL,
    actor_id uuid,
    recorded_at timestamp with time zone NOT NULL,
    sequence bigint NOT NULL,
    CONSTRAINT payment_events_kind_check CHECK ((kind = ANY (ARRAY['AttemptStarted'::text, 'SessionRecorded'::text, 'VerificationRequested'::text, 'OrderFulfilled'::text, 'PaidNeedsReview'::text, 'CheckoutFailed'::text, 'ResumeConfirmed'::text]))),
    CONSTRAINT payment_events_recorded_at_check CHECK (isfinite(recorded_at))
);

ALTER TABLE ONLY public.payment_events FORCE ROW LEVEL SECURITY;


--
-- Name: payment_events_sequence_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.payment_events ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.payment_events_sequence_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: payment_fulfilment_deliveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_fulfilment_deliveries (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_revision bigint NOT NULL,
    CONSTRAINT payment_fulfilment_deliveries_shop_revision_check CHECK ((shop_revision > 0))
);

ALTER TABLE ONLY public.payment_fulfilment_deliveries FORCE ROW LEVEL SECURITY;


--
-- Name: payment_fulfilment_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_fulfilment_shops (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    term_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    coverage_from timestamp with time zone,
    slot_assignment_id uuid,
    replacement_id uuid
);

ALTER TABLE ONLY public.payment_fulfilment_shops FORCE ROW LEVEL SECURITY;


--
-- Name: payment_provider_bindings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_provider_bindings (
    pin jsonb NOT NULL,
    profile_id character varying(80) NOT NULL,
    credential_reference character varying(512),
    webhook_reference character varying(512),
    registered_at timestamp with time zone NOT NULL,
    provenance text DEFAULT 'PlatformOwner'::text NOT NULL,
    CONSTRAINT payment_provider_bindings_provenance_check CHECK ((provenance = ANY (ARRAY['PlatformOwner'::text, 'DeploymentConfiguration'::text]))),
    CONSTRAINT payment_provider_bindings_registered_at_check CHECK (isfinite(registered_at))
);

ALTER TABLE ONLY public.payment_provider_bindings FORCE ROW LEVEL SECURITY;


--
-- Name: payment_provider_routing; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_provider_routing (
    id integer NOT NULL,
    revision bigint NOT NULL,
    profile_id character varying(80),
    pin jsonb,
    CONSTRAINT payment_provider_routing_check CHECK ((((revision = 0) AND (profile_id IS NULL) AND (pin IS NULL)) OR ((revision > 0) AND (profile_id IS NOT NULL) AND (pin IS NOT NULL)))),
    CONSTRAINT payment_provider_routing_id_check CHECK ((id = 1)),
    CONSTRAINT payment_provider_routing_revision_check CHECK ((revision >= 0))
);

ALTER TABLE ONLY public.payment_provider_routing FORCE ROW LEVEL SECURITY;


--
-- Name: payment_provider_selections; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_provider_selections (
    revision bigint NOT NULL,
    profile_id character varying(80) NOT NULL,
    pin jsonb NOT NULL,
    previous_pin jsonb,
    provenance text NOT NULL,
    admin_id uuid,
    session_id uuid,
    mfa_verified_at timestamp with time zone,
    operation_key uuid,
    request_hash text,
    reason character varying(255) NOT NULL,
    selected_at timestamp with time zone NOT NULL,
    receipt jsonb,
    CONSTRAINT payment_provider_selections_check CHECK ((((provenance = 'DeploymentConfiguration'::text) AND (revision = 1) AND (admin_id IS NULL) AND (session_id IS NULL) AND (operation_key IS NULL) AND (request_hash IS NULL) AND (receipt IS NULL)) OR ((provenance = 'PlatformOwner'::text) AND (admin_id IS NOT NULL) AND (session_id IS NOT NULL) AND (mfa_verified_at IS NOT NULL) AND (operation_key IS NOT NULL) AND (request_hash ~ '^[a-f0-9]{64}$'::text) AND (receipt IS NOT NULL) AND (selected_at >= mfa_verified_at) AND (selected_at < (mfa_verified_at + '00:05:00'::interval))))),
    CONSTRAINT payment_provider_selections_provenance_check CHECK ((provenance = ANY (ARRAY['PlatformOwner'::text, 'DeploymentConfiguration'::text]))),
    CONSTRAINT payment_provider_selections_reason_check CHECK (((length(TRIM(BOTH FROM reason)) >= 3) AND (length(TRIM(BOTH FROM reason)) <= 255))),
    CONSTRAINT payment_provider_selections_revision_check CHECK ((revision > 0)),
    CONSTRAINT payment_provider_selections_selected_at_check CHECK (isfinite(selected_at))
);

ALTER TABLE ONLY public.payment_provider_selections FORCE ROW LEVEL SECURITY;


--
-- Name: payment_restoration_holds; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_restoration_holds (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    attempt_id uuid,
    manual_receipt_id uuid,
    effective_at timestamp with time zone NOT NULL,
    held_at timestamp with time zone NOT NULL,
    account_revision bigint,
    restriction text,
    upstream_verified_at timestamp with time zone,
    CONSTRAINT payment_restoration_holds_account_revision_check CHECK ((account_revision > 0)),
    CONSTRAINT payment_restoration_holds_check CHECK ((isfinite(held_at) AND (held_at >= effective_at))),
    CONSTRAINT payment_restoration_holds_check1 CHECK (((attempt_id IS NULL) <> (manual_receipt_id IS NULL))),
    CONSTRAINT payment_restoration_holds_check2 CHECK ((((account_revision IS NULL) AND (restriction IS NULL) AND (upstream_verified_at IS NULL)) OR ((account_revision IS NOT NULL) AND (restriction IS NOT NULL) AND (upstream_verified_at IS NOT NULL)))),
    CONSTRAINT payment_restoration_holds_effective_at_check CHECK (isfinite(effective_at)),
    CONSTRAINT payment_restoration_holds_restriction_check CHECK ((restriction = ANY (ARRAY['Unrestricted'::text, 'Trading'::text, 'Security'::text, 'Closed'::text]))),
    CONSTRAINT payment_restoration_holds_upstream_verified_at_check CHECK (isfinite(upstream_verified_at))
);

ALTER TABLE ONLY public.payment_restoration_holds FORCE ROW LEVEL SECURITY;


--
-- Name: payment_review_decisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_review_decisions (
    account_id uuid NOT NULL,
    case_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    action text NOT NULL,
    disposition text NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    protected_payload text NOT NULL,
    term_id uuid,
    accepted_source_hash text,
    reviewed_source_hash text,
    recovery_policy text,
    CONSTRAINT payment_review_decisions_check CHECK ((((action = 'RecordNoAccessDecision'::text) AND (disposition = 'NoAccessDecisionRecorded'::text)) OR ((action = 'ReferForRefundReview'::text) AND (disposition = 'RefundReviewPending'::text)) OR ((action = 'RecoverAcceptedTerms'::text) AND (disposition = 'AcceptedTermsRecovered'::text)))),
    CONSTRAINT payment_review_decisions_check1 CHECK (((action = 'RecoverAcceptedTerms'::text) = (term_id IS NOT NULL))),
    CONSTRAINT payment_review_decisions_check2 CHECK ((((term_id IS NULL) AND (accepted_source_hash IS NULL) AND (reviewed_source_hash IS NULL) AND (recovery_policy IS NULL)) OR ((term_id IS NOT NULL) AND (accepted_source_hash IS NOT NULL) AND (reviewed_source_hash IS NOT NULL) AND (recovery_policy IS NOT NULL) AND (recovery_policy = 'accepted-source-equivalence/1'::text)))),
    CONSTRAINT payment_review_decisions_decided_at_check CHECK (isfinite(decided_at)),
    CONSTRAINT payment_review_decisions_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT payment_review_decisions_protected_payload_check CHECK (((length(protected_payload) >= 1) AND (length(protected_payload) <= 8192)))
);

ALTER TABLE ONLY public.payment_review_decisions FORCE ROW LEVEL SECURITY;


--
-- Name: payment_routing_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_routing_events (
    sequence bigint NOT NULL,
    account_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    kind text NOT NULL,
    reason text,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT payment_routing_events_check CHECK (((kind = 'Restored'::text) = (reason IS NULL))),
    CONSTRAINT payment_routing_events_kind_check CHECK ((kind = ANY (ARRAY['Unavailable'::text, 'Restored'::text]))),
    CONSTRAINT payment_routing_events_reason_check CHECK ((reason = ANY (ARRAY['MissingProfile'::text, 'InvalidProfile'::text, 'UnknownProvider'::text, 'CredentialUnavailable'::text, 'SimulatorForbidden'::text, 'ConfigurationReused'::text]))),
    CONSTRAINT payment_routing_events_recorded_at_check CHECK (isfinite(recorded_at))
);

ALTER TABLE ONLY public.payment_routing_events FORCE ROW LEVEL SECURITY;


--
-- Name: payment_routing_events_sequence_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.payment_routing_events ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.payment_routing_events_sequence_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: payment_trial_conversions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_trial_conversions (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    trial_term_id uuid NOT NULL,
    paid_starts_at timestamp with time zone NOT NULL,
    CONSTRAINT payment_trial_conversions_paid_starts_at_check CHECK (isfinite(paid_starts_at))
);

ALTER TABLE ONLY public.payment_trial_conversions FORCE ROW LEVEL SECURITY;


--
-- Name: pending_payment_attempts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.pending_payment_attempts (
    attempt_id uuid NOT NULL,
    account_id uuid NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    attempts integer DEFAULT 0 NOT NULL,
    completed boolean DEFAULT false NOT NULL,
    lease_token uuid,
    lease_expires_at timestamp with time zone,
    CONSTRAINT pending_payment_attempts_attempts_check CHECK (((attempts >= 0) AND (attempts <= 12))),
    CONSTRAINT pending_payment_attempts_check CHECK (((lease_token IS NULL) = (lease_expires_at IS NULL))),
    CONSTRAINT pending_payment_attempts_check1 CHECK (((NOT completed) OR (lease_token IS NULL))),
    CONSTRAINT pending_payment_attempts_lease_expires_at_check CHECK (isfinite(lease_expires_at)),
    CONSTRAINT pending_payment_attempts_next_attempt_at_check CHECK (isfinite(next_attempt_at))
);

ALTER TABLE ONLY public.pending_payment_attempts FORCE ROW LEVEL SECURITY;


--
-- Name: plan_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.plan_versions (
    id uuid NOT NULL,
    plan_id uuid NOT NULL,
    version_number integer NOT NULL,
    status integer NOT NULL,
    revision integer NOT NULL,
    name character varying(80) NOT NULL,
    currency character varying(3) NOT NULL,
    monthly_price_paise bigint,
    annual_price_paise bigint,
    included_shops integer,
    members_per_shop integer,
    extra_shops_offered boolean,
    extra_shop_monthly_price_paise bigint,
    extra_shop_annual_price_paise bigint,
    customer_terms_version character varying(40),
    refund_policy_version character varying(40),
    negotiation_reason character varying(500),
    created_at timestamp with time zone NOT NULL,
    created_by_admin_id uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by_admin_id uuid NOT NULL,
    source integer DEFAULT 0 NOT NULL,
    source_expected_version integer,
    source_version_id uuid,
    CONSTRAINT ck_plan_versions_amounts CHECK (((monthly_price_paise >= 0) AND (monthly_price_paise <= '1000000000000'::bigint) AND ((annual_price_paise >= 0) AND (annual_price_paise <= '1000000000000'::bigint)) AND ((extra_shop_monthly_price_paise >= 0) AND (extra_shop_monthly_price_paise <= '1000000000000'::bigint)) AND ((extra_shop_annual_price_paise >= 0) AND (extra_shop_annual_price_paise <= '1000000000000'::bigint)))),
    CONSTRAINT ck_plan_versions_capacities CHECK (((included_shops >= 1) AND (included_shops <= 100000) AND ((members_per_shop >= 1) AND (members_per_shop <= 100000)))),
    CONSTRAINT ck_plan_versions_currency CHECK (((currency)::text = 'INR'::text)),
    CONSTRAINT ck_plan_versions_extra_shop_offer CHECK (((extra_shops_offered IS TRUE) OR ((extra_shop_monthly_price_paise IS NULL) AND (extra_shop_annual_price_paise IS NULL)))),
    CONSTRAINT ck_plan_versions_name CHECK ((((name)::text = btrim((name)::text)) AND ((char_length((name)::text) >= 1) AND (char_length((name)::text) <= 80)))),
    CONSTRAINT ck_plan_versions_numbers CHECK (((version_number >= 1) AND (revision >= 1))),
    CONSTRAINT ck_plan_versions_policy_versions CHECK ((((customer_terms_version)::text ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'::text) AND ((refund_policy_version)::text ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'::text))),
    CONSTRAINT ck_plan_versions_reason CHECK ((((negotiation_reason)::text = btrim((negotiation_reason)::text)) AND ((char_length((negotiation_reason)::text) >= 1) AND (char_length((negotiation_reason)::text) <= 500)))),
    CONSTRAINT ck_plan_versions_source CHECK (((source = ANY (ARRAY[0, 1, 2])) AND ((source_version_id IS NULL) = (source_expected_version IS NULL)) AND ((source_expected_version IS NULL) OR (source_expected_version > 0)))),
    CONSTRAINT ck_plan_versions_status CHECK ((status = ANY (ARRAY[1, 2, 3])))
);

ALTER TABLE ONLY public.plan_versions FORCE ROW LEVEL SECURITY;


--
-- Name: plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.plans (
    id uuid NOT NULL,
    kind integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    created_by_admin_id uuid NOT NULL,
    CONSTRAINT ck_plans_id CHECK ((id <> '00000000-0000-0000-0000-000000000000'::uuid)),
    CONSTRAINT ck_plans_kind CHECK ((kind = ANY (ARRAY[1, 2, 3])))
);

ALTER TABLE ONLY public.plans FORCE ROW LEVEL SECURITY;


--
-- Name: provider_audit_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.provider_audit_events (
    id uuid NOT NULL,
    action integer NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    owner_id uuid,
    invitation_id uuid,
    actor_kind integer,
    after jsonb,
    before jsonb,
    correlation_id uuid,
    reason integer,
    CONSTRAINT ck_bootstrap_audit_action CHECK ((((action >= 1) AND (action <= 25)) OR ((action >= 101) AND (action <= 107)))),
    CONSTRAINT ck_provider_audit_metadata CHECK ((((num_nonnulls(actor_kind, reason, correlation_id) = 0) AND ((action >= 1) AND (action <= 16)) AND (before IS NULL) AND (after IS NULL)) OR ((num_nonnulls(actor_kind, reason, correlation_id) = 3) AND ((actor_kind >= 1) AND (actor_kind <= 5)) AND ((reason >= 1) AND (reason <= 36)))))
);


--
-- Name: published_catalogue_offers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.published_catalogue_offers (
    version_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    version_number integer NOT NULL,
    development_fixture boolean NOT NULL,
    snapshot text NOT NULL,
    CONSTRAINT ck_published_catalogue_offer CHECK (((version_number > 0) AND ((octet_length(snapshot) >= 2) AND (octet_length(snapshot) <= 2400))))
);

ALTER TABLE ONLY public.published_catalogue_offers FORCE ROW LEVEL SECURITY;


--
-- Name: refund_approvals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_approvals (
    account_id uuid NOT NULL,
    approval_id uuid NOT NULL,
    reservation_id uuid NOT NULL,
    refund_request_id uuid NOT NULL,
    order_id uuid NOT NULL,
    confirmation_id uuid NOT NULL,
    attempt_id uuid,
    manual_receipt_id uuid,
    operation_key uuid NOT NULL,
    payload_hash text NOT NULL,
    impact_digest text NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    mfa_verified_at timestamp with time zone NOT NULL,
    authorized_at timestamp with time zone NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    provider_id text NOT NULL,
    provider_account_id text NOT NULL,
    environment text NOT NULL,
    configuration_version text NOT NULL,
    refund_policy_version text NOT NULL,
    payment_amount_paise bigint NOT NULL,
    requested_amount_paise bigint NOT NULL,
    amount_paise bigint NOT NULL,
    remaining_before_paise bigint NOT NULL,
    remaining_after_paise bigint NOT NULL,
    receipt_revision bigint NOT NULL,
    currency text NOT NULL,
    reason text NOT NULL,
    CONSTRAINT refund_approvals_check CHECK ((isfinite(authorized_at) AND (authorized_at >= mfa_verified_at) AND (authorized_at < (mfa_verified_at + '00:05:00'::interval)))),
    CONSTRAINT refund_approvals_check1 CHECK (((requested_amount_paise >= 1) AND (requested_amount_paise <= payment_amount_paise))),
    CONSTRAINT refund_approvals_check2 CHECK (((amount_paise >= 1) AND (amount_paise <= requested_amount_paise))),
    CONSTRAINT refund_approvals_check3 CHECK (((remaining_before_paise >= amount_paise) AND (remaining_before_paise <= payment_amount_paise))),
    CONSTRAINT refund_approvals_check4 CHECK ((remaining_after_paise = (remaining_before_paise - amount_paise))),
    CONSTRAINT refund_approvals_check5 CHECK (((attempt_id IS NULL) <> (manual_receipt_id IS NULL))),
    CONSTRAINT refund_approvals_configuration_version_check CHECK (((length(configuration_version) >= 1) AND (length(configuration_version) <= 200))),
    CONSTRAINT refund_approvals_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT refund_approvals_decided_at_check CHECK (isfinite(decided_at)),
    CONSTRAINT refund_approvals_environment_check CHECK (((length(environment) >= 1) AND (length(environment) <= 40))),
    CONSTRAINT refund_approvals_impact_digest_check CHECK ((impact_digest ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT refund_approvals_mfa_verified_at_check CHECK (isfinite(mfa_verified_at)),
    CONSTRAINT refund_approvals_payload_hash_check CHECK ((payload_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT refund_approvals_payment_amount_paise_check CHECK (((payment_amount_paise >= 1) AND (payment_amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT refund_approvals_provider_account_id_check CHECK (((length(provider_account_id) >= 1) AND (length(provider_account_id) <= 200))),
    CONSTRAINT refund_approvals_provider_id_check CHECK (((length(provider_id) >= 1) AND (length(provider_id) <= 80))),
    CONSTRAINT refund_approvals_reason_check CHECK (((length(btrim(reason)) >= 1) AND (length(btrim(reason)) <= 500) AND (reason = btrim(reason)) AND (reason !~ '[[:cntrl:]]'::text))),
    CONSTRAINT refund_approvals_receipt_revision_check CHECK ((receipt_revision > 0)),
    CONSTRAINT refund_approvals_refund_policy_version_check CHECK (((length(refund_policy_version) >= 1) AND (length(refund_policy_version) <= 80)))
);

ALTER TABLE ONLY public.refund_approvals FORCE ROW LEVEL SECURITY;


--
-- Name: refund_bindings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_bindings (
    reservation_id uuid NOT NULL,
    provider_id text NOT NULL,
    provider_account_id text NOT NULL,
    environment text NOT NULL,
    reference text NOT NULL
);

ALTER TABLE ONLY public.refund_bindings FORCE ROW LEVEL SECURITY;


--
-- Name: refund_observations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_observations (
    observation_id bigint NOT NULL,
    reservation_id uuid NOT NULL,
    observed_at timestamp with time zone NOT NULL,
    outcome text NOT NULL,
    reason text NOT NULL,
    refund_reference text
);

ALTER TABLE ONLY public.refund_observations FORCE ROW LEVEL SECURITY;


--
-- Name: refund_observations_observation_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.refund_observations ALTER COLUMN observation_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.refund_observations_observation_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: refund_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_requests (
    account_id uuid NOT NULL,
    refund_request_id uuid NOT NULL,
    order_id uuid NOT NULL,
    confirmation_id uuid NOT NULL,
    attempt_id uuid,
    manual_receipt_id uuid,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    provider_id text NOT NULL,
    provider_account_id text NOT NULL,
    environment text NOT NULL,
    configuration_version text NOT NULL,
    payment_amount_paise bigint NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    reason text NOT NULL,
    requested_at timestamp with time zone NOT NULL,
    CONSTRAINT refund_requests_check CHECK (((amount_paise >= 1) AND (amount_paise <= payment_amount_paise))),
    CONSTRAINT refund_requests_check1 CHECK (((attempt_id IS NULL) <> (manual_receipt_id IS NULL))),
    CONSTRAINT refund_requests_configuration_version_check CHECK (((length(configuration_version) >= 1) AND (length(configuration_version) <= 200))),
    CONSTRAINT refund_requests_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT refund_requests_environment_check CHECK (((length(environment) >= 1) AND (length(environment) <= 40))),
    CONSTRAINT refund_requests_payment_amount_paise_check CHECK (((payment_amount_paise >= 1) AND (payment_amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT refund_requests_provider_account_id_check CHECK (((length(provider_account_id) >= 1) AND (length(provider_account_id) <= 200))),
    CONSTRAINT refund_requests_provider_id_check CHECK (((length(provider_id) >= 1) AND (length(provider_id) <= 80))),
    CONSTRAINT refund_requests_reason_check CHECK (((length(btrim(reason)) >= 1) AND (length(btrim(reason)) <= 500) AND (reason = btrim(reason)) AND (reason !~ '[[:cntrl:]]'::text))),
    CONSTRAINT refund_requests_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT refund_requests_requested_at_check CHECK (isfinite(requested_at))
);

ALTER TABLE ONLY public.refund_requests FORCE ROW LEVEL SECURITY;


--
-- Name: refund_sends; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_sends (
    reservation_id uuid NOT NULL,
    started_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY public.refund_sends FORCE ROW LEVEL SECURITY;


--
-- Name: refund_work; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_work (
    reservation_id uuid NOT NULL,
    token uuid,
    due_at timestamp with time zone NOT NULL,
    lease_until timestamp with time zone,
    state text DEFAULT 'Reserved'::text NOT NULL,
    checks smallint DEFAULT 0 NOT NULL,
    wake_pending boolean DEFAULT false NOT NULL,
    refund_reference text,
    candidate_reference text,
    last_check timestamp with time zone,
    reason text,
    CONSTRAINT refund_work_checks_check CHECK (((checks >= 0) AND (checks <= 12))),
    CONSTRAINT refund_work_state_check CHECK ((state = ANY (ARRAY['Reserved'::text, 'Unknown'::text, 'Pending'::text, 'Confirmed'::text, 'Failed'::text])))
);

ALTER TABLE ONLY public.refund_work FORCE ROW LEVEL SECURITY;


--
-- Name: scheduled_capacity_reduction_resolutions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scheduled_capacity_reduction_resolutions (
    account_id uuid NOT NULL,
    resolution_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    generation bigint NOT NULL,
    observation_digest text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT scheduled_capacity_reduction_resolutio_observation_digest_check CHECK ((observation_digest ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reduction_resolutions_generation_check CHECK ((generation > 0)),
    CONSTRAINT scheduled_capacity_reduction_resolutions_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT scheduled_capacity_reduction_resolutions_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text))
);

ALTER TABLE ONLY public.scheduled_capacity_reduction_resolutions FORCE ROW LEVEL SECURITY;


--
-- Name: scheduled_capacity_reduction_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scheduled_capacity_reduction_shops (
    account_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_name text NOT NULL,
    affiliation_request_id uuid NOT NULL,
    retained boolean NOT NULL,
    active_members integer NOT NULL,
    seat_hash text NOT NULL,
    replay_id uuid NOT NULL,
    CONSTRAINT scheduled_capacity_reduction_shops_active_members_check CHECK ((active_members >= 0)),
    CONSTRAINT scheduled_capacity_reduction_shops_seat_hash_check CHECK ((seat_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reduction_shops_shop_name_check CHECK (((length(shop_name) >= 1) AND (length(shop_name) <= 250)))
);

ALTER TABLE ONLY public.scheduled_capacity_reduction_shops FORCE ROW LEVEL SECURITY;


--
-- Name: scheduled_capacity_reductions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scheduled_capacity_reductions (
    account_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    preview_id uuid NOT NULL,
    term_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    replay_id uuid NOT NULL,
    state text NOT NULL,
    reason text,
    conflicts jsonb,
    target_plan_version_id uuid NOT NULL,
    prior_capacity integer NOT NULL,
    target_capacity integer NOT NULL,
    prior_member_allowance integer NOT NULL,
    target_member_allowance integer NOT NULL,
    target_mode text NOT NULL,
    source_shop_count integer NOT NULL,
    source_shop_hash text NOT NULL,
    retained_shop_count integer NOT NULL,
    retained_shop_hash text NOT NULL,
    source_hash text NOT NULL,
    observation_digest text NOT NULL,
    amount_paise bigint NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    renewal_ends_at timestamp with time zone NOT NULL,
    expected_source_revision bigint NOT NULL,
    resolution_generation bigint DEFAULT 0 NOT NULL,
    order_id uuid,
    attempt_id uuid,
    receipt_id uuid,
    payment_effective_at timestamp with time zone,
    grace_days_included integer,
    funded_at timestamp with time zone,
    reserve_event uuid,
    consume_event uuid,
    reserve_expires_at timestamp with time zone,
    successor_term_id uuid,
    applied_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    snapshot_hash text NOT NULL,
    snapshot jsonb NOT NULL,
    CONSTRAINT scheduled_capacity_reductions_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT scheduled_capacity_reductions_applied_at_check CHECK (isfinite(applied_at)),
    CONSTRAINT scheduled_capacity_reductions_check CHECK (((target_capacity >= 1) AND (target_capacity <= prior_capacity))),
    CONSTRAINT scheduled_capacity_reductions_check1 CHECK (((target_member_allowance >= 1) AND (target_member_allowance <= prior_member_allowance))),
    CONSTRAINT scheduled_capacity_reductions_check2 CHECK (((target_capacity < prior_capacity) OR (target_member_allowance < prior_member_allowance))),
    CONSTRAINT scheduled_capacity_reductions_check3 CHECK (((source_shop_count >= 1) AND (source_shop_count <= prior_capacity))),
    CONSTRAINT scheduled_capacity_reductions_check4 CHECK (((retained_shop_count >= 1) AND (retained_shop_count <= target_capacity) AND (retained_shop_count <= source_shop_count))),
    CONSTRAINT scheduled_capacity_reductions_check5 CHECK ((isfinite(renewal_ends_at) AND (renewal_ends_at > effective_at))),
    CONSTRAINT scheduled_capacity_reductions_check6 CHECK (((order_id IS NULL) OR (num_nonnulls(attempt_id, receipt_id) = 1))),
    CONSTRAINT scheduled_capacity_reductions_check7 CHECK (((state = 'Scheduled'::text) = (order_id IS NULL))),
    CONSTRAINT scheduled_capacity_reductions_check8 CHECK (((state = ANY (ARRAY['NeedsResolution'::text, 'NeedsReview'::text])) = (reason IS NOT NULL))),
    CONSTRAINT scheduled_capacity_reductions_check9 CHECK (((state = 'Applied'::text) = (applied_at IS NOT NULL))),
    CONSTRAINT scheduled_capacity_reductions_conflicts_check CHECK (((conflicts IS NULL) OR (jsonb_typeof(conflicts) = 'array'::text))),
    CONSTRAINT scheduled_capacity_reductions_created_at_check CHECK (isfinite(created_at)),
    CONSTRAINT scheduled_capacity_reductions_effective_at_check CHECK (isfinite(effective_at)),
    CONSTRAINT scheduled_capacity_reductions_expected_source_revision_check CHECK ((expected_source_revision >= 0)),
    CONSTRAINT scheduled_capacity_reductions_funded_at_check CHECK (isfinite(funded_at)),
    CONSTRAINT scheduled_capacity_reductions_grace_days_included_check CHECK (((grace_days_included >= 0) AND (grace_days_included <= 7))),
    CONSTRAINT scheduled_capacity_reductions_observation_digest_check CHECK ((observation_digest ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reductions_payment_effective_at_check CHECK (isfinite(payment_effective_at)),
    CONSTRAINT scheduled_capacity_reductions_prior_capacity_check CHECK (((prior_capacity >= 1) AND (prior_capacity <= 100000))),
    CONSTRAINT scheduled_capacity_reductions_prior_member_allowance_check CHECK (((prior_member_allowance >= 1) AND (prior_member_allowance <= 100000))),
    CONSTRAINT scheduled_capacity_reductions_reason_check CHECK (((length(reason) >= 1) AND (length(reason) <= 80))),
    CONSTRAINT scheduled_capacity_reductions_request_hash_check CHECK ((request_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reductions_reserve_expires_at_check CHECK (isfinite(reserve_expires_at)),
    CONSTRAINT scheduled_capacity_reductions_resolution_generation_check CHECK ((resolution_generation >= 0)),
    CONSTRAINT scheduled_capacity_reductions_retained_shop_hash_check CHECK ((retained_shop_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reductions_snapshot_check CHECK ((jsonb_typeof(snapshot) = 'object'::text)),
    CONSTRAINT scheduled_capacity_reductions_snapshot_hash_check CHECK ((snapshot_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reductions_source_hash_check CHECK ((source_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reductions_source_shop_hash_check CHECK ((source_shop_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT scheduled_capacity_reductions_state_check CHECK ((state = ANY (ARRAY['Scheduled'::text, 'Funded'::text, 'Applying'::text, 'Applied'::text, 'NeedsResolution'::text, 'NeedsReview'::text]))),
    CONSTRAINT scheduled_capacity_reductions_target_mode_check CHECK ((target_mode = ANY (ARRAY['Independent'::text, 'Grouped'::text])))
);

ALTER TABLE ONLY public.scheduled_capacity_reductions FORCE ROW LEVEL SECURITY;


--
-- Name: scheduled_subscription_change_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scheduled_subscription_change_shops (
    account_id uuid NOT NULL,
    change_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    request_id uuid NOT NULL,
    shop_name text NOT NULL,
    CONSTRAINT scheduled_subscription_change_shops_shop_name_check CHECK (((length(shop_name) >= 1) AND (length(shop_name) <= 250)))
);

ALTER TABLE ONLY public.scheduled_subscription_change_shops FORCE ROW LEVEL SECURITY;


--
-- Name: scheduled_subscription_changes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scheduled_subscription_changes (
    change_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    revision bigint NOT NULL,
    kind text NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    policy_version text NOT NULL,
    shop_count integer NOT NULL,
    shop_hash text NOT NULL,
    withdraws_change_id uuid,
    CONSTRAINT scheduled_subscription_changes_check CHECK ((isfinite(effective_at) AND (effective_at > recorded_at))),
    CONSTRAINT scheduled_subscription_changes_kind_check CHECK ((kind = ANY (ARRAY['ScheduleCancellation'::text, 'WithdrawCancellation'::text]))),
    CONSTRAINT scheduled_subscription_changes_policy_version_check CHECK ((policy_version = 'term-boundary/1'::text)),
    CONSTRAINT scheduled_subscription_changes_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT scheduled_subscription_changes_revision_check CHECK ((revision >= 1)),
    CONSTRAINT scheduled_subscription_changes_revision_parity CHECK (((kind = 'ScheduleCancellation'::text) = ((revision % (2)::bigint) = 1))),
    CONSTRAINT scheduled_subscription_changes_shop_count_check CHECK ((shop_count > 0)),
    CONSTRAINT scheduled_subscription_changes_shop_hash_check CHECK ((shop_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT scheduled_subscription_changes_withdrawal_link CHECK (((kind = 'WithdrawCancellation'::text) = (withdraws_change_id IS NOT NULL)))
);

ALTER TABLE ONLY public.scheduled_subscription_changes FORCE ROW LEVEL SECURITY;


--
-- Name: service_restoration_adjustments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.service_restoration_adjustments (
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    grant_id uuid NOT NULL,
    paid_term_id uuid NOT NULL
);

ALTER TABLE ONLY public.service_restoration_adjustments FORCE ROW LEVEL SECURITY;


--
-- Name: shop_affiliation_delivery_uses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_affiliation_delivery_uses (
    replay_id uuid NOT NULL,
    account_id uuid NOT NULL,
    expires_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY public.shop_affiliation_delivery_uses FORCE ROW LEVEL SECURITY;


--
-- Name: shop_affiliation_inbox; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_affiliation_inbox (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    request_id uuid NOT NULL,
    digest text NOT NULL,
    status text NOT NULL,
    CONSTRAINT shop_affiliation_inbox_digest_check CHECK ((length(digest) = 64)),
    CONSTRAINT shop_affiliation_inbox_status_check CHECK ((status = ANY (ARRAY['Applied'::text, 'NeedsReview'::text])))
);

ALTER TABLE ONLY public.shop_affiliation_inbox FORCE ROW LEVEL SECURITY;


--
-- Name: shop_affiliation_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_affiliation_requests (
    request_id uuid NOT NULL,
    account_id uuid NOT NULL,
    account_name character varying(120) NOT NULL,
    shop_id uuid NOT NULL,
    requester_id uuid NOT NULL,
    requester_revision integer NOT NULL,
    operation_key uuid NOT NULL,
    term_id uuid,
    created_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    reviewed_refund_paid_through timestamp with time zone,
    CONSTRAINT shop_affiliation_requests_check CHECK ((expires_at > created_at)),
    CONSTRAINT shop_affiliation_requests_requester_revision_check CHECK ((requester_revision > 0))
);

ALTER TABLE ONLY public.shop_affiliation_requests FORCE ROW LEVEL SECURITY;


--
-- Name: shop_trial_delivery_uses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_trial_delivery_uses (
    replay_id uuid NOT NULL,
    event_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    digest text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT shop_trial_delivery_uses_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.shop_trial_delivery_uses FORCE ROW LEVEL SECURITY;


--
-- Name: shop_trial_inbox; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_trial_inbox (
    event_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    account_id uuid,
    term_id uuid,
    replay_id uuid NOT NULL,
    digest text NOT NULL,
    status text NOT NULL,
    reason character varying(40),
    CONSTRAINT shop_trial_inbox_check CHECK ((((status = 'Applied'::text) AND (account_id IS NOT NULL) AND (term_id IS NOT NULL) AND (reason IS NULL)) OR ((status = 'NeedsReview'::text) AND (term_id IS NULL) AND (reason IS NOT NULL))))
);

ALTER TABLE ONLY public.shop_trial_inbox FORCE ROW LEVEL SECURITY;


--
-- Name: subscription_cancellation_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscription_cancellation_receipts (
    change_id uuid NOT NULL,
    account_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_revision integer NOT NULL,
    request_id uuid NOT NULL,
    payload_hash text NOT NULL,
    source_hash text NOT NULL,
    CONSTRAINT subscription_cancellation_receipts_membership_revision_check CHECK ((membership_revision > 0)),
    CONSTRAINT subscription_cancellation_receipts_operation_key_check CHECK ((operation_key <> '00000000-0000-0000-0000-000000000000'::uuid)),
    CONSTRAINT subscription_cancellation_receipts_payload_hash_check CHECK ((payload_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT subscription_cancellation_receipts_source_hash_check CHECK ((source_hash ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.subscription_cancellation_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: subscription_coverage_terms; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscription_coverage_terms (
    term_id uuid NOT NULL,
    account_id uuid NOT NULL,
    mode text NOT NULL,
    capacity integer NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    CONSTRAINT subscription_coverage_terms_check CHECK ((ends_at > starts_at)),
    CONSTRAINT subscription_coverage_terms_check1 CHECK ((((mode = 'Independent'::text) AND (capacity = 1)) OR ((mode = 'Grouped'::text) AND (capacity > 1))))
);

ALTER TABLE ONLY public.subscription_coverage_terms FORCE ROW LEVEL SECURITY;


--
-- Name: subscription_term_facts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscription_term_facts (
    term_id uuid NOT NULL,
    account_id uuid NOT NULL,
    basis text NOT NULL,
    billing_period text NOT NULL,
    anchor_local timestamp without time zone NOT NULL,
    time_zone text NOT NULL,
    period_ordinal integer NOT NULL,
    calendar_version character varying(40) NOT NULL,
    access_policy_version character varying(40) NOT NULL,
    member_allowance integer NOT NULL,
    price_paise bigint,
    currency text,
    plan_version_id uuid,
    source_reference character varying(80) NOT NULL,
    development_fixture boolean NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT subscription_term_facts_access_policy_version_check CHECK ((length((access_policy_version)::text) > 0)),
    CONSTRAINT subscription_term_facts_anchor_local_check CHECK ((anchor_local = date_trunc('second'::text, anchor_local))),
    CONSTRAINT subscription_term_facts_basis_check CHECK ((basis = ANY (ARRAY['Paid'::text, 'Trial'::text, 'Complimentary'::text, 'Temporary'::text, 'Transition'::text]))),
    CONSTRAINT subscription_term_facts_billing_period_check CHECK ((billing_period = ANY (ARRAY['Month'::text, 'Year'::text]))),
    CONSTRAINT subscription_term_facts_calendar_version_check CHECK ((length((calendar_version)::text) > 0)),
    CONSTRAINT subscription_term_facts_check CHECK (((price_paise IS NULL) = (currency IS NULL))),
    CONSTRAINT subscription_term_facts_check1 CHECK (((basis <> 'Paid'::text) OR (price_paise IS NOT NULL))),
    CONSTRAINT subscription_term_facts_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT subscription_term_facts_member_allowance_check CHECK (((member_allowance >= 1) AND (member_allowance <= 100000))),
    CONSTRAINT subscription_term_facts_period_ordinal_check CHECK (((period_ordinal >= 0) AND (period_ordinal <= 1200))),
    CONSTRAINT subscription_term_facts_price_paise_check CHECK (((price_paise >= 0) AND (price_paise <= '1000000000000'::bigint))),
    CONSTRAINT subscription_term_facts_source_reference_check CHECK ((length((source_reference)::text) > 0)),
    CONSTRAINT subscription_term_facts_time_zone_check CHECK ((time_zone = 'Asia/Kolkata'::text))
);

ALTER TABLE ONLY public.subscription_term_facts FORCE ROW LEVEL SECURITY;


--
-- Name: trial_extension_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trial_extension_receipts (
    extension_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    admin_id uuid NOT NULL,
    session_id uuid NOT NULL,
    shop_name text NOT NULL,
    reason character varying(500) NOT NULL,
    review_reference character varying(80) NOT NULL,
    payload_digest text NOT NULL,
    source_hash text NOT NULL,
    expected_version bigint NOT NULL,
    account_trial_cap_reached boolean NOT NULL,
    CONSTRAINT trial_extension_receipts_expected_version_check CHECK ((expected_version >= 0)),
    CONSTRAINT trial_extension_receipts_payload_digest_check CHECK ((payload_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT trial_extension_receipts_reason_check CHECK (((length(btrim((reason)::text)) > 0) AND ((reason)::text !~ '[[:cntrl:]]'::text))),
    CONSTRAINT trial_extension_receipts_review_reference_check CHECK (((length(btrim((review_reference)::text)) > 0) AND ((review_reference)::text !~ '[[:cntrl:]]'::text))),
    CONSTRAINT trial_extension_receipts_source_hash_check CHECK ((source_hash ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.trial_extension_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: trial_extensions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trial_extensions (
    extension_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    revision bigint NOT NULL,
    previous_ends_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    policy_version text NOT NULL,
    CONSTRAINT trial_extensions_check CHECK ((isfinite(ends_at) AND (ends_at > previous_ends_at) AND (ends_at > recorded_at) AND (ends_at < '9999-01-01 00:00:00+00'::timestamp with time zone))),
    CONSTRAINT trial_extensions_policy_version_check CHECK ((policy_version = 'trial-extension/1'::text)),
    CONSTRAINT trial_extensions_previous_ends_at_check CHECK (isfinite(previous_ends_at)),
    CONSTRAINT trial_extensions_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT trial_extensions_revision_check CHECK ((revision > 0))
);

ALTER TABLE ONLY public.trial_extensions FORCE ROW LEVEL SECURITY;


--
-- Name: trial_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trial_history (
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    event_id uuid NOT NULL,
    operation_key uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    policy_id uuid NOT NULL,
    policy_revision integer NOT NULL,
    terms_version text NOT NULL,
    member_cap integer NOT NULL,
    account_trial_cap integer NOT NULL,
    active_members integer NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    consent_digest text NOT NULL,
    development_fixture boolean NOT NULL,
    computation_version text NOT NULL,
    CONSTRAINT trial_history_account_trial_cap_check CHECK (((account_trial_cap >= 1) AND (account_trial_cap <= 100000))),
    CONSTRAINT trial_history_authority_version_check CHECK ((authority_version >= 0)),
    CONSTRAINT trial_history_check CHECK (((active_members >= 1) AND (active_members <= member_cap))),
    CONSTRAINT trial_history_check1 CHECK (((ends_at - starts_at) = '336:00:00'::interval)),
    CONSTRAINT trial_history_check2 CHECK (((applied_at >= starts_at) AND (applied_at < ends_at))),
    CONSTRAINT trial_history_computation_version_check CHECK ((computation_version = 'shop-trial/1'::text)),
    CONSTRAINT trial_history_consent_digest_check CHECK ((consent_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT trial_history_member_cap_check CHECK (((member_cap >= 1) AND (member_cap <= 100000))),
    CONSTRAINT trial_history_policy_revision_check CHECK ((policy_revision > 0)),
    CONSTRAINT trial_history_terms_version_check CHECK ((terms_version ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'::text))
);

ALTER TABLE ONLY public.trial_history FORCE ROW LEVEL SECURITY;


--
-- Name: trial_policy_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.trial_policy_versions (
    id uuid NOT NULL,
    version_number integer NOT NULL,
    status integer NOT NULL,
    revision integer NOT NULL,
    duration_days integer NOT NULL,
    active_member_cap integer,
    max_concurrent_trial_shops integer,
    trial_terms_version character varying(40),
    created_at timestamp with time zone NOT NULL,
    created_by_admin_id uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by_admin_id uuid NOT NULL,
    source integer DEFAULT 0 NOT NULL,
    source_expected_version integer,
    source_version_id uuid,
    CONSTRAINT ck_trial_policy_versions_caps CHECK (((active_member_cap >= 1) AND (active_member_cap <= 100000) AND ((max_concurrent_trial_shops >= 1) AND (max_concurrent_trial_shops <= 100000)))),
    CONSTRAINT ck_trial_policy_versions_duration CHECK ((duration_days = 14)),
    CONSTRAINT ck_trial_policy_versions_numbers CHECK (((version_number >= 1) AND (revision >= 1))),
    CONSTRAINT ck_trial_policy_versions_source CHECK (((source = ANY (ARRAY[0, 1, 2])) AND ((source_version_id IS NULL) = (source_expected_version IS NULL)) AND ((source_expected_version IS NULL) OR (source_expected_version > 0)))),
    CONSTRAINT ck_trial_policy_versions_status CHECK ((status = ANY (ARRAY[1, 2, 3]))),
    CONSTRAINT ck_trial_policy_versions_terms CHECK (((trial_terms_version)::text ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'::text))
);

ALTER TABLE ONLY public.trial_policy_versions FORCE ROW LEVEL SECURITY;


--
-- Name: unmatched_payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.unmatched_payments (
    case_id uuid NOT NULL,
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    attempt_id uuid,
    manual_receipt_id uuid,
    inbox_id uuid,
    classification text NOT NULL,
    expected_amount_paise bigint NOT NULL,
    expected_currency text NOT NULL,
    observed_amount_paise bigint NOT NULL,
    observed_currency text NOT NULL,
    provider_id text NOT NULL,
    receiving_account_id text NOT NULL,
    environment text NOT NULL,
    configuration_version text NOT NULL,
    provider_order_reference text,
    provider_payment_reference text,
    observed_at timestamp with time zone NOT NULL,
    captured_at timestamp with time zone,
    created_at timestamp with time zone,
    CONSTRAINT unmatched_payments_check CHECK ((isfinite(captured_at) AND (captured_at <= observed_at))),
    CONSTRAINT unmatched_payments_check1 CHECK ((isfinite(created_at) AND (created_at <= observed_at))),
    CONSTRAINT unmatched_payments_check2 CHECK ((num_nonnulls(attempt_id, manual_receipt_id) = 1)),
    CONSTRAINT unmatched_payments_check3 CHECK (((attempt_id IS NULL) = (provider_payment_reference IS NULL))),
    CONSTRAINT unmatched_payments_classification_check CHECK ((classification = ANY (ARRAY['ReceivingPinMismatch'::text, 'CurrencyMismatch'::text, 'Underpayment'::text, 'Overpayment'::text, 'DuplicateFunds'::text, 'CoverageChanged'::text, 'TermUnavailable'::text, 'CaptureTimeUnproven'::text, 'RestorationPending'::text, 'ReservationExpired'::text, 'ReservationChanged'::text, 'SourceChanged'::text, 'ReservationBusy'::text]))),
    CONSTRAINT unmatched_payments_expected_amount_paise_check CHECK ((expected_amount_paise > 0)),
    CONSTRAINT unmatched_payments_expected_currency_check CHECK ((length(expected_currency) = 3)),
    CONSTRAINT unmatched_payments_observed_amount_paise_check CHECK ((observed_amount_paise > 0)),
    CONSTRAINT unmatched_payments_observed_at_check CHECK (isfinite(observed_at)),
    CONSTRAINT unmatched_payments_observed_currency_check CHECK ((length(observed_currency) = 3))
);

ALTER TABLE ONLY public.unmatched_payments FORCE ROW LEVEL SECURITY;


--
-- Name: verified_payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.verified_payments (
    attempt_id uuid NOT NULL,
    account_id uuid NOT NULL,
    order_id uuid NOT NULL,
    provider_id character varying(40) NOT NULL,
    provider_account_id character varying(80) NOT NULL,
    environment character varying(20) NOT NULL,
    configuration_version character varying(80) NOT NULL,
    provider_payment_reference character varying(120) NOT NULL,
    amount_paise bigint NOT NULL,
    currency text NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    verified_at timestamp with time zone NOT NULL,
    capture_time_basis text DEFAULT 'ProviderCapture'::text NOT NULL,
    CONSTRAINT verified_payments_amount_paise_check CHECK (((amount_paise >= 1) AND (amount_paise <= '1000000000000'::bigint))),
    CONSTRAINT verified_payments_capture_time_basis_check CHECK ((capture_time_basis = ANY (ARRAY['ProviderCapture'::text, 'ObservedAtVerification'::text]))),
    CONSTRAINT verified_payments_captured_at_check CHECK (isfinite(captured_at)),
    CONSTRAINT verified_payments_configuration_version_check CHECK ((length((configuration_version)::text) > 0)),
    CONSTRAINT verified_payments_currency_check CHECK ((currency = 'INR'::text)),
    CONSTRAINT verified_payments_environment_check CHECK (((environment)::text = ANY (ARRAY[('Development'::character varying)::text, ('Test'::character varying)::text, ('Live'::character varying)::text]))),
    CONSTRAINT verified_payments_provider_account_id_check CHECK ((length((provider_account_id)::text) > 0)),
    CONSTRAINT verified_payments_provider_id_check CHECK ((length((provider_id)::text) > 0)),
    CONSTRAINT verified_payments_provider_payment_reference_check CHECK ((length((provider_payment_reference)::text) > 0)),
    CONSTRAINT verified_payments_verified_at_check CHECK (isfinite(verified_at))
);

ALTER TABLE ONLY public.verified_payments FORCE ROW LEVEL SECURITY;


--
-- Name: withdrawn_catalogue_offers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.withdrawn_catalogue_offers (
    version_id uuid NOT NULL
);

ALTER TABLE ONLY public.withdrawn_catalogue_offers FORCE ROW LEVEL SECURITY;


--
-- Data for Name: admin_identities; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_invitations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_recoveries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_recovery_codes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_refresh_credentials; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_sessions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_sign_in_buckets; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: admin_totp; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_account_invitations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_account_members; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_accounts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_document_versions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_invitation_audit; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_invitation_issue_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_member_removals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_ownership_acceptances; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: billing_ownership_proposals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: bootstrap_enrollment_credentials; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: bootstrap_rate_buckets; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: capacity_change_quote_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: capacity_change_quotes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: capacity_increases; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: capacity_reduction_preview_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: capacity_reduction_previews; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: catalogue_draft_changes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: catalogue_preparations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: catalogue_publications; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: catalogue_retirements; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: checkout_order_acceptances; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: checkout_order_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: checkout_orders; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: checkout_trial_sources; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: complimentary_grant_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: complimentary_grant_scopes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: complimentary_grant_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: complimentary_grants; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_replacement_exception_approvals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_replacement_exceptions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_replacements; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_slot_assignment_deliveries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_slot_assignments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_approvals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_bindings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_consents; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_evidence; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_progress; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_proposals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: development_gateway_controls; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: development_gateway_payments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: development_gateway_refunds; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: development_gateway_sessions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: development_refund_controls; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_account_heads; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_merchant_inbox; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_messages; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_outbox; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_request_uses; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_shop_heads; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_snapshots; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: gateway_inbox_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: gateway_inbox_work; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: initial_admin_bootstrap; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.initial_admin_bootstrap (id, proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at, consumed_at, owner_id) VALUES (1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);


--
-- Data for Name: manual_payment_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: merchant_billing_delegation_uses; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: offline_device_observations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_closure_consent_notices; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_closure_proposals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_closure_reopenings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_closure_targets; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_closures; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_restriction_deliveries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_restriction_release_deliveries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_restriction_releases; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operational_restrictions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: paid_fresh_starts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: paid_slot_handoffs; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_attempt_sessions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_attempts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_confirmations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_fulfilment_deliveries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_fulfilment_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_fulfilments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_provider_bindings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_provider_routing; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.payment_provider_routing (id, revision, profile_id, pin) VALUES (1, 0, NULL, NULL);


--
-- Data for Name: payment_provider_selections; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_restoration_holds; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_review_decisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_routing_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_trial_conversions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: pending_payment_attempts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: plan_versions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: plans; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: provider_audit_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: published_catalogue_offers; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_access_decisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_applications; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_approvals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_attempts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_bindings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_observations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_requests; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_sends; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refund_work; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: scheduled_capacity_reduction_resolutions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: scheduled_capacity_reduction_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: scheduled_capacity_reductions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: scheduled_subscription_change_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: scheduled_subscription_changes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: service_restoration_adjustments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_affiliation_delivery_uses; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_affiliation_inbox; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_affiliation_requests; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_affiliations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_trial_delivery_uses; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_trial_inbox; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: subscription_cancellation_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: subscription_coverage_terms; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: subscription_term_facts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: trial_extension_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: trial_extensions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: trial_history; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: trial_policy_versions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: unmatched_payments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: verified_payments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: withdrawn_catalogue_offers; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Name: operational_closure_proposals_ordinal_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.operational_closure_proposals_ordinal_seq', 1, false);


--
-- Name: payment_events_sequence_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.payment_events_sequence_seq', 1, false);


--
-- Name: payment_routing_events_sequence_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.payment_routing_events_sequence_seq', 1, false);


--
-- Name: refund_observations_observation_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.refund_observations_observation_id_seq', 1, false);


--
-- Name: billing_account_invitations billing_account_invitations_accepted_member_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_account_invitations
    ADD CONSTRAINT billing_account_invitations_accepted_member_id_key UNIQUE (accepted_member_id);


--
-- Name: billing_account_invitations billing_account_invitations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_account_invitations
    ADD CONSTRAINT billing_account_invitations_pkey PRIMARY KEY (id);


--
-- Name: billing_document_versions billing_document_versions_account_id_document_id_version_nu_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_document_versions
    ADD CONSTRAINT billing_document_versions_account_id_document_id_version_nu_key UNIQUE (account_id, document_id, version_number);


--
-- Name: billing_document_versions billing_document_versions_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_document_versions
    ADD CONSTRAINT billing_document_versions_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: billing_document_versions billing_document_versions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_document_versions
    ADD CONSTRAINT billing_document_versions_pkey PRIMARY KEY (account_id, version_id);


--
-- Name: billing_invitation_audit billing_invitation_audit_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_invitation_audit
    ADD CONSTRAINT billing_invitation_audit_pkey PRIMARY KEY (invitation_id, invitation_revision);


--
-- Name: billing_invitation_issue_receipts billing_invitation_issue_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_invitation_issue_receipts
    ADD CONSTRAINT billing_invitation_issue_receipts_pkey PRIMARY KEY (account_id, actor_id, operation_key);


--
-- Name: billing_member_removals billing_member_removals_member_id_before_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_member_removals
    ADD CONSTRAINT billing_member_removals_member_id_before_revision_key UNIQUE (member_id, before_revision);


--
-- Name: billing_member_removals billing_member_removals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_member_removals
    ADD CONSTRAINT billing_member_removals_pkey PRIMARY KEY (account_id, actor_id, operation_key);


--
-- Name: billing_ownership_acceptances billing_ownership_acceptances_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_acceptances
    ADD CONSTRAINT billing_ownership_acceptances_pkey PRIMARY KEY (transfer_id);


--
-- Name: billing_ownership_acceptances billing_ownership_acceptances_recipient_actor_id_operation__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_acceptances
    ADD CONSTRAINT billing_ownership_acceptances_recipient_actor_id_operation__key UNIQUE (recipient_actor_id, operation_key);


--
-- Name: billing_ownership_proposals billing_ownership_proposals_owner_actor_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_proposals
    ADD CONSTRAINT billing_ownership_proposals_owner_actor_id_operation_key_key UNIQUE (owner_actor_id, operation_key);


--
-- Name: billing_ownership_proposals billing_ownership_proposals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_proposals
    ADD CONSTRAINT billing_ownership_proposals_pkey PRIMARY KEY (id);


--
-- Name: capacity_change_quote_shops capacity_change_quote_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quote_shops
    ADD CONSTRAINT capacity_change_quote_shops_pkey PRIMARY KEY (account_id, quote_id, shop_id);


--
-- Name: capacity_change_quotes capacity_change_quotes_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quotes
    ADD CONSTRAINT capacity_change_quotes_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: capacity_change_quotes capacity_change_quotes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quotes
    ADD CONSTRAINT capacity_change_quotes_pkey PRIMARY KEY (account_id, quote_id);


--
-- Name: capacity_change_quotes capacity_change_quotes_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quotes
    ADD CONSTRAINT capacity_change_quotes_replay_id_key UNIQUE (replay_id);


--
-- Name: capacity_increases capacity_increases_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_attempt_id_key UNIQUE (attempt_id);


--
-- Name: capacity_increases capacity_increases_consume_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_consume_event_key UNIQUE (consume_event);


--
-- Name: capacity_increases capacity_increases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_pkey PRIMARY KEY (account_id, order_id);


--
-- Name: capacity_increases capacity_increases_receipt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_receipt_id_key UNIQUE (receipt_id);


--
-- Name: capacity_increases capacity_increases_reserve_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_reserve_event_key UNIQUE (reserve_event);


--
-- Name: capacity_reduction_preview_shops capacity_reduction_preview_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_preview_shops
    ADD CONSTRAINT capacity_reduction_preview_shops_pkey PRIMARY KEY (account_id, preview_id, shop_id);


--
-- Name: capacity_reduction_previews capacity_reduction_previews_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_previews
    ADD CONSTRAINT capacity_reduction_previews_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: capacity_reduction_previews capacity_reduction_previews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_previews
    ADD CONSTRAINT capacity_reduction_previews_pkey PRIMARY KEY (account_id, preview_id);


--
-- Name: capacity_reduction_previews capacity_reduction_previews_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_previews
    ADD CONSTRAINT capacity_reduction_previews_replay_id_key UNIQUE (replay_id);


--
-- Name: checkout_order_acceptances checkout_order_acceptances_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_acceptances
    ADD CONSTRAINT checkout_order_acceptances_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: checkout_order_acceptances checkout_order_acceptances_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_acceptances
    ADD CONSTRAINT checkout_order_acceptances_pkey PRIMARY KEY (account_id, order_id);


--
-- Name: checkout_order_acceptances checkout_order_acceptances_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_acceptances
    ADD CONSTRAINT checkout_order_acceptances_replay_id_key UNIQUE (replay_id);


--
-- Name: checkout_order_shops checkout_order_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_shops
    ADD CONSTRAINT checkout_order_shops_pkey PRIMARY KEY (account_id, order_id, shop_id);


--
-- Name: checkout_orders checkout_orders_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_orders
    ADD CONSTRAINT checkout_orders_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: checkout_orders checkout_orders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_orders
    ADD CONSTRAINT checkout_orders_pkey PRIMARY KEY (account_id, order_id);


--
-- Name: checkout_orders checkout_orders_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_orders
    ADD CONSTRAINT checkout_orders_replay_id_key UNIQUE (replay_id);


--
-- Name: checkout_trial_sources checkout_trial_sources_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_trial_sources
    ADD CONSTRAINT checkout_trial_sources_pkey PRIMARY KEY (account_id, order_id, shop_id);


--
-- Name: complimentary_grant_receipts complimentary_grant_receipts_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_receipts
    ADD CONSTRAINT complimentary_grant_receipts_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: complimentary_grant_receipts complimentary_grant_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_receipts
    ADD CONSTRAINT complimentary_grant_receipts_pkey PRIMARY KEY (grant_id);


--
-- Name: complimentary_grant_scopes complimentary_grant_scopes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_scopes
    ADD CONSTRAINT complimentary_grant_scopes_pkey PRIMARY KEY (account_id);


--
-- Name: complimentary_grant_shops complimentary_grant_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_shops
    ADD CONSTRAINT complimentary_grant_shops_pkey PRIMARY KEY (grant_id, shop_id);


--
-- Name: complimentary_grants complimentary_grants_account_id_granted_version_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grants
    ADD CONSTRAINT complimentary_grants_account_id_granted_version_key UNIQUE (account_id, granted_version);


--
-- Name: complimentary_grants complimentary_grants_grant_id_account_id_term_id_starts_at__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grants
    ADD CONSTRAINT complimentary_grants_grant_id_account_id_term_id_starts_at__key UNIQUE (grant_id, account_id, term_id, starts_at, ends_at);


--
-- Name: complimentary_grants complimentary_grants_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grants
    ADD CONSTRAINT complimentary_grants_pkey PRIMARY KEY (grant_id);


--
-- Name: complimentary_grants complimentary_grants_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grants
    ADD CONSTRAINT complimentary_grants_term_id_key UNIQUE (term_id);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptio_account_id_requester_id_opera_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptio_account_id_requester_id_opera_key UNIQUE (account_id, requester_id, operation_key);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_app_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_app_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_appr_exception_id_generation_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_appr_exception_id_generation_key UNIQUE (exception_id, generation);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approv_consume_receipt_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approv_consume_receipt_event_key UNIQUE (consume_receipt_event);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_abort_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_abort_event_key UNIQUE (abort_event);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_consume_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_consume_event_key UNIQUE (consume_event);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_courtesy_grant_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_courtesy_grant_id_key UNIQUE (courtesy_grant_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_courtesy_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_courtesy_term_id_key UNIQUE (courtesy_term_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_pkey PRIMARY KEY (approval_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_prepare_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_prepare_event_key UNIQUE (prepare_event);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_incoming_consent_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_incoming_consent_event_key UNIQUE (incoming_consent_event);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_incoming_revoke_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_incoming_revoke_event_key UNIQUE (incoming_revoke_event);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_outgoing_consent_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_outgoing_consent_event_key UNIQUE (outgoing_consent_event);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_outgoing_revoke_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_outgoing_revoke_event_key UNIQUE (outgoing_revoke_event);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_pkey PRIMARY KEY (exception_id);


--
-- Name: coverage_replacements coverage_replacements_account_id_requester_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacements
    ADD CONSTRAINT coverage_replacements_account_id_requester_id_operation_key_key UNIQUE (account_id, requester_id, operation_key);


--
-- Name: coverage_replacements coverage_replacements_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacements
    ADD CONSTRAINT coverage_replacements_pkey PRIMARY KEY (replacement_id);


--
-- Name: coverage_slot_assignment_deliveries coverage_slot_assignment_deliveries_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignment_deliveries
    ADD CONSTRAINT coverage_slot_assignment_deliveries_event_id_key UNIQUE (event_id);


--
-- Name: coverage_slot_assignment_deliveries coverage_slot_assignment_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignment_deliveries
    ADD CONSTRAINT coverage_slot_assignment_deliveries_pkey PRIMARY KEY (assignment_id);


--
-- Name: coverage_slot_assignments coverage_slot_assignments_account_id_requester_id_operation_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignments
    ADD CONSTRAINT coverage_slot_assignments_account_id_requester_id_operation_key UNIQUE (account_id, requester_id, operation_key);


--
-- Name: coverage_slot_assignments coverage_slot_assignments_consent_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignments
    ADD CONSTRAINT coverage_slot_assignments_consent_event_key UNIQUE (consent_event);


--
-- Name: coverage_slot_assignments coverage_slot_assignments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignments
    ADD CONSTRAINT coverage_slot_assignments_pkey PRIMARY KEY (assignment_id);


--
-- Name: subscription_coverage_terms coverage_term_interval_identity; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_coverage_terms
    ADD CONSTRAINT coverage_term_interval_identity UNIQUE (account_id, term_id, starts_at, ends_at);


--
-- Name: coverage_transfer_approvals coverage_transfer_approvals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_approvals
    ADD CONSTRAINT coverage_transfer_approvals_pkey PRIMARY KEY (transfer_id);


--
-- Name: coverage_transfer_approvals coverage_transfer_approvals_prepare_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_approvals
    ADD CONSTRAINT coverage_transfer_approvals_prepare_event_key UNIQUE (prepare_event);


--
-- Name: coverage_transfer_approvals coverage_transfer_approvals_source_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_approvals
    ADD CONSTRAINT coverage_transfer_approvals_source_account_id_operation_key_key UNIQUE (source_account_id, operation_key);


--
-- Name: coverage_transfer_approvals coverage_transfer_approvals_transfer_id_source_account_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_approvals
    ADD CONSTRAINT coverage_transfer_approvals_transfer_id_source_account_id_key UNIQUE (transfer_id, source_account_id);


--
-- Name: coverage_transfer_bindings coverage_transfer_bindings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_bindings
    ADD CONSTRAINT coverage_transfer_bindings_pkey PRIMARY KEY (transfer_id);


--
-- Name: coverage_transfer_bindings coverage_transfer_bindings_predecessor_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_bindings
    ADD CONSTRAINT coverage_transfer_bindings_predecessor_id_key UNIQUE (predecessor_id);


--
-- Name: coverage_transfer_bindings coverage_transfer_bindings_source_term_id_slot_origin_shop__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_bindings
    ADD CONSTRAINT coverage_transfer_bindings_source_term_id_slot_origin_shop__key UNIQUE (source_term_id, slot_origin_shop_id);


--
-- Name: coverage_transfer_consents coverage_transfer_consents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consents
    ADD CONSTRAINT coverage_transfer_consents_pkey PRIMARY KEY (event_id);


--
-- Name: coverage_transfer_consents coverage_transfer_consents_transfer_id_purpose_generation_k_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consents
    ADD CONSTRAINT coverage_transfer_consents_transfer_id_purpose_generation_k_key UNIQUE (transfer_id, purpose, generation, kind);


--
-- Name: coverage_transfer_evidence coverage_transfer_evidence_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_evidence
    ADD CONSTRAINT coverage_transfer_evidence_pkey PRIMARY KEY (transfer_id, side, term_id);


--
-- Name: coverage_transfer_progress coverage_transfer_progress_event_id_state_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_progress
    ADD CONSTRAINT coverage_transfer_progress_event_id_state_key UNIQUE (event_id, state);


--
-- Name: coverage_transfer_progress coverage_transfer_progress_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_progress
    ADD CONSTRAINT coverage_transfer_progress_pkey PRIMARY KEY (transfer_id, sequence);


--
-- Name: coverage_transfer_proposals coverage_transfer_proposals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_proposals
    ADD CONSTRAINT coverage_transfer_proposals_pkey PRIMARY KEY (transfer_id);


--
-- Name: coverage_transfer_proposals coverage_transfer_proposals_source_account_id_requester_id__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_proposals
    ADD CONSTRAINT coverage_transfer_proposals_source_account_id_requester_id__key UNIQUE (source_account_id, requester_id, operation_key);


--
-- Name: development_gateway_controls development_gateway_controls_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_controls
    ADD CONSTRAINT development_gateway_controls_pkey PRIMARY KEY (provider_order_reference);


--
-- Name: development_gateway_payments development_gateway_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_payments
    ADD CONSTRAINT development_gateway_payments_pkey PRIMARY KEY (provider_payment_reference);


--
-- Name: development_gateway_payments development_gateway_payments_provider_order_reference_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_payments
    ADD CONSTRAINT development_gateway_payments_provider_order_reference_key UNIQUE (provider_order_reference);


--
-- Name: development_gateway_refunds development_gateway_refunds_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_refunds
    ADD CONSTRAINT development_gateway_refunds_pkey PRIMARY KEY (reference);


--
-- Name: development_gateway_refunds development_gateway_refunds_receipt_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_refunds
    ADD CONSTRAINT development_gateway_refunds_receipt_key UNIQUE (receipt);


--
-- Name: development_gateway_sessions development_gateway_sessions_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_sessions
    ADD CONSTRAINT development_gateway_sessions_attempt_id_key UNIQUE (attempt_id);


--
-- Name: development_gateway_sessions development_gateway_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_sessions
    ADD CONSTRAINT development_gateway_sessions_pkey PRIMARY KEY (provider_order_reference);


--
-- Name: development_refund_controls development_refund_controls_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_refund_controls
    ADD CONSTRAINT development_refund_controls_pkey PRIMARY KEY (receipt);


--
-- Name: entitlement_account_heads entitlement_account_heads_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_account_heads
    ADD CONSTRAINT entitlement_account_heads_pkey PRIMARY KEY (account_id);


--
-- Name: entitlement_merchant_inbox entitlement_merchant_inbox_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_merchant_inbox
    ADD CONSTRAINT entitlement_merchant_inbox_pkey PRIMARY KEY (event_id, kind);


--
-- Name: entitlement_messages entitlement_messages_event_id_account_id_shop_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_messages
    ADD CONSTRAINT entitlement_messages_event_id_account_id_shop_id_key UNIQUE (event_id, account_id, shop_id);


--
-- Name: entitlement_messages entitlement_messages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_messages
    ADD CONSTRAINT entitlement_messages_pkey PRIMARY KEY (event_id);


--
-- Name: entitlement_outbox entitlement_outbox_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_outbox
    ADD CONSTRAINT entitlement_outbox_pkey PRIMARY KEY (event_id);


--
-- Name: entitlement_request_uses entitlement_request_uses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_request_uses
    ADD CONSTRAINT entitlement_request_uses_pkey PRIMARY KEY (replay_id);


--
-- Name: entitlement_shop_heads entitlement_shop_heads_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_shop_heads
    ADD CONSTRAINT entitlement_shop_heads_pkey PRIMARY KEY (shop_id);


--
-- Name: entitlement_snapshots entitlement_snapshots_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_snapshots
    ADD CONSTRAINT entitlement_snapshots_pkey PRIMARY KEY (event_id);


--
-- Name: entitlement_snapshots entitlement_snapshots_shop_id_shop_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_snapshots
    ADD CONSTRAINT entitlement_snapshots_shop_id_shop_revision_key UNIQUE (shop_id, shop_revision);


--
-- Name: gateway_inbox_events gateway_inbox_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gateway_inbox_events
    ADD CONSTRAINT gateway_inbox_events_pkey PRIMARY KEY (inbox_id);


--
-- Name: gateway_inbox_events gateway_inbox_events_provider_id_provider_account_id_enviro_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gateway_inbox_events
    ADD CONSTRAINT gateway_inbox_events_provider_id_provider_account_id_enviro_key UNIQUE (provider_id, provider_account_id, environment, provider_event_id);


--
-- Name: gateway_inbox_work gateway_inbox_work_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gateway_inbox_work
    ADD CONSTRAINT gateway_inbox_work_pkey PRIMARY KEY (inbox_id);


--
-- Name: manual_payment_receipts manual_payment_receipts_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.manual_payment_receipts
    ADD CONSTRAINT manual_payment_receipts_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: manual_payment_receipts manual_payment_receipts_account_id_order_id_receipt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.manual_payment_receipts
    ADD CONSTRAINT manual_payment_receipts_account_id_order_id_receipt_id_key UNIQUE (account_id, order_id, receipt_id);


--
-- Name: manual_payment_receipts manual_payment_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.manual_payment_receipts
    ADD CONSTRAINT manual_payment_receipts_pkey PRIMARY KEY (receipt_id);


--
-- Name: manual_payment_receipts manual_receiving_reference; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.manual_payment_receipts
    ADD CONSTRAINT manual_receiving_reference UNIQUE (receiving_account_id, environment, reference_digest);


--
-- Name: offline_device_observations offline_device_observations_permit_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_device_observations
    ADD CONSTRAINT offline_device_observations_permit_id_key UNIQUE (permit_id);


--
-- Name: offline_device_observations offline_device_observations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_device_observations
    ADD CONSTRAINT offline_device_observations_pkey PRIMARY KEY (event_id);


--
-- Name: operational_closure_consent_notices operational_closure_consent_n_proposal_id_shop_id_actor_id__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_consent_notices
    ADD CONSTRAINT operational_closure_consent_n_proposal_id_shop_id_actor_id__key UNIQUE NULLS NOT DISTINCT (proposal_id, shop_id, actor_id, operation_key);


--
-- Name: operational_closure_proposals operational_closure_proposals_id_account_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_proposals
    ADD CONSTRAINT operational_closure_proposals_id_account_id_key UNIQUE (id, account_id);


--
-- Name: operational_closure_proposals operational_closure_proposals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_proposals
    ADD CONSTRAINT operational_closure_proposals_pkey PRIMARY KEY (id);


--
-- Name: operational_closure_reopenings operational_closure_reopening_account_id_shop_id_owner_cons_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopening_account_id_shop_id_owner_cons_key UNIQUE (account_id, shop_id, owner_consent_reference);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_closure_id_shop_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_closure_id_shop_id_key UNIQUE (closure_id, shop_id);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_event_id_key UNIQUE (event_id);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_pkey PRIMARY KEY (id);


--
-- Name: operational_closure_targets operational_closure_targets_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_targets
    ADD CONSTRAINT operational_closure_targets_event_id_key UNIQUE (event_id);


--
-- Name: operational_closure_targets operational_closure_targets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_targets
    ADD CONSTRAINT operational_closure_targets_pkey PRIMARY KEY (closure_id, shop_id);


--
-- Name: operational_closures operational_closures_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closures
    ADD CONSTRAINT operational_closures_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: operational_closures operational_closures_id_account_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closures
    ADD CONSTRAINT operational_closures_id_account_id_key UNIQUE (id, account_id);


--
-- Name: operational_closures operational_closures_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closures
    ADD CONSTRAINT operational_closures_pkey PRIMARY KEY (id);


--
-- Name: operational_restriction_deliveries operational_restriction_deliveries_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_deliveries
    ADD CONSTRAINT operational_restriction_deliveries_event_id_key UNIQUE (event_id);


--
-- Name: operational_restriction_deliveries operational_restriction_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_deliveries
    ADD CONSTRAINT operational_restriction_deliveries_pkey PRIMARY KEY (restriction_id, shop_id);


--
-- Name: operational_restriction_release_deliveries operational_restriction_release_deliveries_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_release_deliveries
    ADD CONSTRAINT operational_restriction_release_deliveries_event_id_key UNIQUE (event_id);


--
-- Name: operational_restriction_release_deliveries operational_restriction_release_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_release_deliveries
    ADD CONSTRAINT operational_restriction_release_deliveries_pkey PRIMARY KEY (release_id, shop_id);


--
-- Name: operational_restriction_releases operational_restriction_releases_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_releases
    ADD CONSTRAINT operational_restriction_releases_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: operational_restriction_releases operational_restriction_releases_id_account_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_releases
    ADD CONSTRAINT operational_restriction_releases_id_account_id_key UNIQUE (id, account_id);


--
-- Name: operational_restriction_releases operational_restriction_releases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_releases
    ADD CONSTRAINT operational_restriction_releases_pkey PRIMARY KEY (id);


--
-- Name: operational_restriction_releases operational_restriction_releases_restriction_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_releases
    ADD CONSTRAINT operational_restriction_releases_restriction_id_key UNIQUE (restriction_id);


--
-- Name: operational_restrictions operational_restrictions_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restrictions
    ADD CONSTRAINT operational_restrictions_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: operational_restrictions operational_restrictions_account_id_shop_id_version_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restrictions
    ADD CONSTRAINT operational_restrictions_account_id_shop_id_version_key UNIQUE NULLS NOT DISTINCT (account_id, shop_id, version);


--
-- Name: operational_restrictions operational_restrictions_id_account_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restrictions
    ADD CONSTRAINT operational_restrictions_id_account_id_key UNIQUE (id, account_id);


--
-- Name: operational_restrictions operational_restrictions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restrictions
    ADD CONSTRAINT operational_restrictions_pkey PRIMARY KEY (id);


--
-- Name: paid_fresh_starts paid_fresh_starts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_fresh_starts
    ADD CONSTRAINT paid_fresh_starts_pkey PRIMARY KEY (account_id, order_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_account_id_term_id_outgoing_shop_id_prev_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_account_id_term_id_outgoing_shop_id_prev_key UNIQUE (account_id, term_id, outgoing_shop_id, previous_starts_at);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_account_id_term_id_slot_origin_shop_id_s_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_account_id_term_id_slot_origin_shop_id_s_key UNIQUE (account_id, term_id, slot_origin_shop_id, slot_generation);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_approval_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_approval_id_key UNIQUE (approval_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_consume_receipt_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_consume_receipt_event_key UNIQUE (consume_receipt_event);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_courtesy_grant_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_courtesy_grant_id_key UNIQUE (courtesy_grant_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_courtesy_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_courtesy_term_id_key UNIQUE (courtesy_term_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_exception_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_exception_id_key UNIQUE (exception_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_pkey PRIMARY KEY (handoff_id);


--
-- Name: payment_attempt_sessions payment_attempt_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempt_sessions
    ADD CONSTRAINT payment_attempt_sessions_pkey PRIMARY KEY (attempt_id);


--
-- Name: payment_attempts payment_attempts_account_id_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempts
    ADD CONSTRAINT payment_attempts_account_id_attempt_id_key UNIQUE (account_id, attempt_id);


--
-- Name: payment_attempts payment_attempts_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempts
    ADD CONSTRAINT payment_attempts_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: payment_attempts payment_attempts_account_id_order_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempts
    ADD CONSTRAINT payment_attempts_account_id_order_id_key UNIQUE (account_id, order_id);


--
-- Name: payment_attempts payment_attempts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempts
    ADD CONSTRAINT payment_attempts_pkey PRIMARY KEY (attempt_id);


--
-- Name: payment_attempts payment_attempts_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempts
    ADD CONSTRAINT payment_attempts_replay_id_key UNIQUE (replay_id);


--
-- Name: payment_confirmations payment_confirmations_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_confirmations
    ADD CONSTRAINT payment_confirmations_attempt_id_key UNIQUE (attempt_id);


--
-- Name: payment_confirmations payment_confirmations_manual_receipt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_confirmations
    ADD CONSTRAINT payment_confirmations_manual_receipt_id_key UNIQUE (manual_receipt_id);


--
-- Name: payment_confirmations payment_confirmations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_confirmations
    ADD CONSTRAINT payment_confirmations_pkey PRIMARY KEY (confirmation_id);


--
-- Name: payment_events payment_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_events
    ADD CONSTRAINT payment_events_pkey PRIMARY KEY (event_id);


--
-- Name: payment_events payment_events_sequence_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_events
    ADD CONSTRAINT payment_events_sequence_key UNIQUE (sequence);


--
-- Name: payment_fulfilment_deliveries payment_fulfilment_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_deliveries
    ADD CONSTRAINT payment_fulfilment_deliveries_pkey PRIMARY KEY (account_id, order_id, shop_id);


--
-- Name: payment_fulfilments payment_fulfilment_one_successor; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilment_one_successor UNIQUE (account_id, predecessor_term_id);


--
-- Name: payment_fulfilment_shops payment_fulfilment_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_shops
    ADD CONSTRAINT payment_fulfilment_shops_pkey PRIMARY KEY (account_id, order_id, shop_id);


--
-- Name: payment_fulfilment_shops payment_fulfilment_shops_term_shop; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_shops
    ADD CONSTRAINT payment_fulfilment_shops_term_shop UNIQUE (term_id, shop_id);


--
-- Name: payment_fulfilments payment_fulfilments_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_attempt_id_key UNIQUE (attempt_id);


--
-- Name: payment_fulfilments payment_fulfilments_manual_receipt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_manual_receipt_id_key UNIQUE (manual_receipt_id);


--
-- Name: payment_fulfilments payment_fulfilments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_pkey PRIMARY KEY (account_id, order_id);


--
-- Name: payment_fulfilments payment_fulfilments_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_term_id_key UNIQUE (term_id);


--
-- Name: payment_provider_bindings payment_provider_bindings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_provider_bindings
    ADD CONSTRAINT payment_provider_bindings_pkey PRIMARY KEY (pin);


--
-- Name: payment_provider_routing payment_provider_routing_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_provider_routing
    ADD CONSTRAINT payment_provider_routing_pkey PRIMARY KEY (id);


--
-- Name: payment_provider_selections payment_provider_selections_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_provider_selections
    ADD CONSTRAINT payment_provider_selections_operation_key_key UNIQUE (operation_key);


--
-- Name: payment_provider_selections payment_provider_selections_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_provider_selections
    ADD CONSTRAINT payment_provider_selections_pkey PRIMARY KEY (revision);


--
-- Name: payment_restoration_holds payment_restoration_holds_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_restoration_holds
    ADD CONSTRAINT payment_restoration_holds_attempt_id_key UNIQUE (attempt_id);


--
-- Name: payment_restoration_holds payment_restoration_holds_manual_receipt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_restoration_holds
    ADD CONSTRAINT payment_restoration_holds_manual_receipt_id_key UNIQUE (manual_receipt_id);


--
-- Name: payment_restoration_holds payment_restoration_holds_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_restoration_holds
    ADD CONSTRAINT payment_restoration_holds_pkey PRIMARY KEY (account_id, order_id);


--
-- Name: payment_review_decisions payment_review_decisions_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_review_decisions
    ADD CONSTRAINT payment_review_decisions_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: payment_review_decisions payment_review_decisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_review_decisions
    ADD CONSTRAINT payment_review_decisions_pkey PRIMARY KEY (account_id, case_id);


--
-- Name: payment_routing_events payment_routing_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_routing_events
    ADD CONSTRAINT payment_routing_events_pkey PRIMARY KEY (sequence);


--
-- Name: payment_trial_conversions payment_trial_conversions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_trial_conversions
    ADD CONSTRAINT payment_trial_conversions_pkey PRIMARY KEY (account_id, order_id, shop_id);


--
-- Name: payment_trial_conversions payment_trial_conversions_shop_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_trial_conversions
    ADD CONSTRAINT payment_trial_conversions_shop_id_key UNIQUE (shop_id);


--
-- Name: pending_payment_attempts pending_payment_attempts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pending_payment_attempts
    ADD CONSTRAINT pending_payment_attempts_pkey PRIMARY KEY (attempt_id);


--
-- Name: admin_identities pk_admin_identities; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_identities
    ADD CONSTRAINT pk_admin_identities PRIMARY KEY (id);


--
-- Name: admin_invitations pk_admin_invitations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_invitations
    ADD CONSTRAINT pk_admin_invitations PRIMARY KEY (id);


--
-- Name: admin_recoveries pk_admin_recoveries; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_recoveries
    ADD CONSTRAINT pk_admin_recoveries PRIMARY KEY (id);


--
-- Name: admin_recovery_codes pk_admin_recovery_codes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_recovery_codes
    ADD CONSTRAINT pk_admin_recovery_codes PRIMARY KEY (admin_id, digest);


--
-- Name: admin_refresh_credentials pk_admin_refresh_credentials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_refresh_credentials
    ADD CONSTRAINT pk_admin_refresh_credentials PRIMARY KEY (id);


--
-- Name: admin_sessions pk_admin_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_sessions
    ADD CONSTRAINT pk_admin_sessions PRIMARY KEY (id);


--
-- Name: admin_sign_in_buckets pk_admin_sign_in_buckets; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_sign_in_buckets
    ADD CONSTRAINT pk_admin_sign_in_buckets PRIMARY KEY (subject);


--
-- Name: admin_totp pk_admin_totp; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_totp
    ADD CONSTRAINT pk_admin_totp PRIMARY KEY (admin_id);


--
-- Name: billing_account_members pk_billing_account_members; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_account_members
    ADD CONSTRAINT pk_billing_account_members PRIMARY KEY (id);


--
-- Name: billing_accounts pk_billing_accounts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_accounts
    ADD CONSTRAINT pk_billing_accounts PRIMARY KEY (id);


--
-- Name: bootstrap_enrollment_credentials pk_bootstrap_enrollment_credentials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bootstrap_enrollment_credentials
    ADD CONSTRAINT pk_bootstrap_enrollment_credentials PRIMARY KEY (id);


--
-- Name: bootstrap_rate_buckets pk_bootstrap_rate_buckets; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bootstrap_rate_buckets
    ADD CONSTRAINT pk_bootstrap_rate_buckets PRIMARY KEY (id);


--
-- Name: catalogue_draft_changes pk_catalogue_draft_changes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_draft_changes
    ADD CONSTRAINT pk_catalogue_draft_changes PRIMARY KEY (id);


--
-- Name: catalogue_preparations pk_catalogue_preparations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_preparations
    ADD CONSTRAINT pk_catalogue_preparations PRIMARY KEY (admin_id, operation_key);


--
-- Name: catalogue_publications pk_catalogue_publications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_publications
    ADD CONSTRAINT pk_catalogue_publications PRIMARY KEY (version_id);


--
-- Name: catalogue_retirements pk_catalogue_retirements; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_retirements
    ADD CONSTRAINT pk_catalogue_retirements PRIMARY KEY (version_id);


--
-- Name: initial_admin_bootstrap pk_initial_admin_bootstrap; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.initial_admin_bootstrap
    ADD CONSTRAINT pk_initial_admin_bootstrap PRIMARY KEY (id);


--
-- Name: merchant_billing_delegation_uses pk_merchant_billing_delegation_uses; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_billing_delegation_uses
    ADD CONSTRAINT pk_merchant_billing_delegation_uses PRIMARY KEY (issuer, replay_id);


--
-- Name: plan_versions pk_plan_versions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plan_versions
    ADD CONSTRAINT pk_plan_versions PRIMARY KEY (id);


--
-- Name: plans pk_plans; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plans
    ADD CONSTRAINT pk_plans PRIMARY KEY (id);


--
-- Name: provider_audit_events pk_provider_audit_events; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.provider_audit_events
    ADD CONSTRAINT pk_provider_audit_events PRIMARY KEY (id);


--
-- Name: published_catalogue_offers pk_published_catalogue_offers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.published_catalogue_offers
    ADD CONSTRAINT pk_published_catalogue_offers PRIMARY KEY (version_id);


--
-- Name: trial_policy_versions pk_trial_policy_versions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_policy_versions
    ADD CONSTRAINT pk_trial_policy_versions PRIMARY KEY (id);


--
-- Name: withdrawn_catalogue_offers pk_withdrawn_catalogue_offers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.withdrawn_catalogue_offers
    ADD CONSTRAINT pk_withdrawn_catalogue_offers PRIMARY KEY (version_id);


--
-- Name: refund_access_decisions refund_access_decisions_account_id_approval_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_account_id_approval_id_key UNIQUE (account_id, approval_id);


--
-- Name: refund_access_decisions refund_access_decisions_account_id_refund_request_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_account_id_refund_request_id_key UNIQUE (account_id, refund_request_id);


--
-- Name: refund_access_decisions refund_access_decisions_account_id_term_id_term_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_account_id_term_id_term_revision_key UNIQUE (account_id, term_id, term_revision);


--
-- Name: refund_access_decisions refund_access_decisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_pkey PRIMARY KEY (account_id, decision_id);


--
-- Name: refund_applications refund_applications_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_applications
    ADD CONSTRAINT refund_applications_pkey PRIMARY KEY (account_id, decision_id);


--
-- Name: refund_approvals refund_approvals_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: refund_approvals refund_approvals_account_id_refund_request_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_account_id_refund_request_id_key UNIQUE (account_id, refund_request_id);


--
-- Name: refund_approvals refund_approvals_confirmation_id_receipt_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_confirmation_id_receipt_revision_key UNIQUE (confirmation_id, receipt_revision);


--
-- Name: refund_approvals refund_approvals_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_pkey PRIMARY KEY (account_id, approval_id);


--
-- Name: refund_approvals refund_approvals_reservation_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_reservation_id_key UNIQUE (reservation_id);


--
-- Name: refund_attempts refund_attempts_account_id_decision_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_attempts
    ADD CONSTRAINT refund_attempts_account_id_decision_id_key UNIQUE (account_id, decision_id);


--
-- Name: refund_attempts refund_attempts_account_id_refund_request_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_attempts
    ADD CONSTRAINT refund_attempts_account_id_refund_request_id_key UNIQUE (account_id, refund_request_id);


--
-- Name: refund_attempts refund_attempts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_attempts
    ADD CONSTRAINT refund_attempts_pkey PRIMARY KEY (reservation_id);


--
-- Name: refund_bindings refund_bindings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_bindings
    ADD CONSTRAINT refund_bindings_pkey PRIMARY KEY (reservation_id);


--
-- Name: refund_bindings refund_bindings_provider_id_provider_account_id_environment_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_bindings
    ADD CONSTRAINT refund_bindings_provider_id_provider_account_id_environment_key UNIQUE (provider_id, provider_account_id, environment, reference);


--
-- Name: refund_observations refund_observations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_observations
    ADD CONSTRAINT refund_observations_pkey PRIMARY KEY (observation_id);


--
-- Name: refund_requests refund_requests_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_requests
    ADD CONSTRAINT refund_requests_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: refund_requests refund_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_requests
    ADD CONSTRAINT refund_requests_pkey PRIMARY KEY (account_id, refund_request_id);


--
-- Name: refund_requests refund_requests_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_requests
    ADD CONSTRAINT refund_requests_replay_id_key UNIQUE (replay_id);


--
-- Name: refund_sends refund_sends_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_sends
    ADD CONSTRAINT refund_sends_pkey PRIMARY KEY (reservation_id);


--
-- Name: refund_work refund_work_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_work
    ADD CONSTRAINT refund_work_pkey PRIMARY KEY (reservation_id);


--
-- Name: refund_work refund_work_token_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_work
    ADD CONSTRAINT refund_work_token_key UNIQUE (token);


--
-- Name: scheduled_capacity_reduction_resolutions scheduled_capacity_reduction__account_id_schedule_id_genera_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_resolutions
    ADD CONSTRAINT scheduled_capacity_reduction__account_id_schedule_id_genera_key UNIQUE (account_id, schedule_id, generation);


--
-- Name: scheduled_capacity_reduction_resolutions scheduled_capacity_reduction_resol_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_resolutions
    ADD CONSTRAINT scheduled_capacity_reduction_resol_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: scheduled_capacity_reduction_resolutions scheduled_capacity_reduction_resolutions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_resolutions
    ADD CONSTRAINT scheduled_capacity_reduction_resolutions_pkey PRIMARY KEY (account_id, resolution_id);


--
-- Name: scheduled_capacity_reduction_resolutions scheduled_capacity_reduction_resolutions_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_resolutions
    ADD CONSTRAINT scheduled_capacity_reduction_resolutions_replay_id_key UNIQUE (replay_id);


--
-- Name: scheduled_capacity_reduction_shops scheduled_capacity_reduction_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_shops
    ADD CONSTRAINT scheduled_capacity_reduction_shops_pkey PRIMARY KEY (account_id, schedule_id, shop_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_attempt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_attempt_id_key UNIQUE (attempt_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_consume_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_consume_event_key UNIQUE (consume_event);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_pkey PRIMARY KEY (account_id, schedule_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_receipt_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_receipt_id_key UNIQUE (receipt_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_replay_id_key UNIQUE (replay_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_reserve_event_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_reserve_event_key UNIQUE (reserve_event);


--
-- Name: scheduled_subscription_change_shops scheduled_subscription_change_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_change_shops
    ADD CONSTRAINT scheduled_subscription_change_shops_pkey PRIMARY KEY (change_id, shop_id);


--
-- Name: scheduled_subscription_changes scheduled_subscription_changes_account_id_change_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_changes
    ADD CONSTRAINT scheduled_subscription_changes_account_id_change_id_key UNIQUE (account_id, change_id);


--
-- Name: scheduled_subscription_changes scheduled_subscription_changes_account_id_term_id_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_changes
    ADD CONSTRAINT scheduled_subscription_changes_account_id_term_id_revision_key UNIQUE (account_id, term_id, revision);


--
-- Name: scheduled_subscription_changes scheduled_subscription_changes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_changes
    ADD CONSTRAINT scheduled_subscription_changes_pkey PRIMARY KEY (change_id);


--
-- Name: scheduled_subscription_changes scheduled_subscription_changes_withdraws_once; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_changes
    ADD CONSTRAINT scheduled_subscription_changes_withdraws_once UNIQUE (withdraws_change_id);


--
-- Name: service_restoration_adjustments service_restoration_adjustments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_restoration_adjustments
    ADD CONSTRAINT service_restoration_adjustments_pkey PRIMARY KEY (grant_id);


--
-- Name: shop_affiliation_delivery_uses shop_affiliation_delivery_uses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_delivery_uses
    ADD CONSTRAINT shop_affiliation_delivery_uses_pkey PRIMARY KEY (replay_id);


--
-- Name: shop_affiliation_inbox shop_affiliation_inbox_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_inbox
    ADD CONSTRAINT shop_affiliation_inbox_pkey PRIMARY KEY (event_id);


--
-- Name: shop_affiliation_inbox shop_affiliation_inbox_request_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_inbox
    ADD CONSTRAINT shop_affiliation_inbox_request_id_key UNIQUE (request_id);


--
-- Name: shop_affiliation_requests shop_affiliation_requests_account_id_requester_id_operation_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_requests
    ADD CONSTRAINT shop_affiliation_requests_account_id_requester_id_operation_key UNIQUE (account_id, requester_id, operation_key);


--
-- Name: shop_affiliation_requests shop_affiliation_requests_account_id_shop_id_request_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_requests
    ADD CONSTRAINT shop_affiliation_requests_account_id_shop_id_request_id_key UNIQUE (account_id, shop_id, request_id);


--
-- Name: shop_affiliation_requests shop_affiliation_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_requests
    ADD CONSTRAINT shop_affiliation_requests_pkey PRIMARY KEY (request_id);


--
-- Name: shop_affiliations shop_affiliations_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliations
    ADD CONSTRAINT shop_affiliations_event_id_key UNIQUE (event_id);


--
-- Name: shop_affiliations shop_affiliations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliations
    ADD CONSTRAINT shop_affiliations_pkey PRIMARY KEY (request_id);


--
-- Name: shop_affiliations shop_affiliations_shop_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliations
    ADD CONSTRAINT shop_affiliations_shop_id_key UNIQUE (shop_id);


--
-- Name: shop_trial_delivery_uses shop_trial_delivery_uses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_delivery_uses
    ADD CONSTRAINT shop_trial_delivery_uses_pkey PRIMARY KEY (replay_id);


--
-- Name: shop_trial_delivery_uses shop_trial_delivery_uses_replay_id_event_id_shop_id_digest_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_delivery_uses
    ADD CONSTRAINT shop_trial_delivery_uses_replay_id_event_id_shop_id_digest_key UNIQUE (replay_id, event_id, shop_id, digest);


--
-- Name: shop_trial_inbox shop_trial_inbox_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT shop_trial_inbox_pkey PRIMARY KEY (event_id);


--
-- Name: shop_trial_inbox shop_trial_inbox_replay_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT shop_trial_inbox_replay_id_key UNIQUE (replay_id);


--
-- Name: shop_trial_inbox shop_trial_inbox_shop_id_actor_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT shop_trial_inbox_shop_id_actor_id_operation_key_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: shop_trial_inbox shop_trial_inbox_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT shop_trial_inbox_term_id_key UNIQUE (term_id);


--
-- Name: subscription_cancellation_receipts subscription_cancellation_receipts_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_cancellation_receipts
    ADD CONSTRAINT subscription_cancellation_receipts_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: subscription_cancellation_receipts subscription_cancellation_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_cancellation_receipts
    ADD CONSTRAINT subscription_cancellation_receipts_pkey PRIMARY KEY (change_id);


--
-- Name: subscription_coverage_terms subscription_coverage_terms_account_id_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_coverage_terms
    ADD CONSTRAINT subscription_coverage_terms_account_id_term_id_key UNIQUE (account_id, term_id);


--
-- Name: subscription_coverage_terms subscription_coverage_terms_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_coverage_terms
    ADD CONSTRAINT subscription_coverage_terms_pkey PRIMARY KEY (term_id);


--
-- Name: subscription_term_facts subscription_term_facts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_term_facts
    ADD CONSTRAINT subscription_term_facts_pkey PRIMARY KEY (term_id);


--
-- Name: trial_extension_receipts trial_extension_receipts_account_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extension_receipts
    ADD CONSTRAINT trial_extension_receipts_account_id_operation_key_key UNIQUE (account_id, operation_key);


--
-- Name: trial_extension_receipts trial_extension_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extension_receipts
    ADD CONSTRAINT trial_extension_receipts_pkey PRIMARY KEY (extension_id);


--
-- Name: trial_extensions trial_extensions_extension_id_account_id_shop_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extensions
    ADD CONSTRAINT trial_extensions_extension_id_account_id_shop_id_key UNIQUE (extension_id, account_id, shop_id);


--
-- Name: trial_extensions trial_extensions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extensions
    ADD CONSTRAINT trial_extensions_pkey PRIMARY KEY (extension_id);


--
-- Name: trial_extensions trial_extensions_shop_id_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extensions
    ADD CONSTRAINT trial_extensions_shop_id_revision_key UNIQUE (shop_id, revision);


--
-- Name: trial_history trial_history_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_history
    ADD CONSTRAINT trial_history_event_id_key UNIQUE (event_id);


--
-- Name: trial_history trial_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_history
    ADD CONSTRAINT trial_history_pkey PRIMARY KEY (shop_id);


--
-- Name: trial_history trial_history_term_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_history
    ADD CONSTRAINT trial_history_term_id_key UNIQUE (term_id);


--
-- Name: unmatched_payments unmatched_payments_account_id_case_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.unmatched_payments
    ADD CONSTRAINT unmatched_payments_account_id_case_id_key UNIQUE (account_id, case_id);


--
-- Name: unmatched_payments unmatched_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.unmatched_payments
    ADD CONSTRAINT unmatched_payments_pkey PRIMARY KEY (case_id);


--
-- Name: verified_payments verified_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.verified_payments
    ADD CONSTRAINT verified_payments_pkey PRIMARY KEY (attempt_id);


--
-- Name: verified_payments verified_payments_provider_id_provider_account_id_environme_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.verified_payments
    ADD CONSTRAINT verified_payments_provider_id_provider_account_id_environme_key UNIQUE (provider_id, provider_account_id, environment, provider_payment_reference);


--
-- Name: account_closure_once; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX account_closure_once ON public.operational_closures USING btree (account_id) WHERE (shop_id IS NULL);


--
-- Name: account_closure_proposals; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX account_closure_proposals ON public.operational_closure_proposals USING btree (account_id, ordinal DESC);


--
-- Name: billing_document_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX billing_document_order ON public.billing_document_versions USING btree (account_id, order_id, uploaded_at, version_id);


--
-- Name: billing_document_order_lineage; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX billing_document_order_lineage ON public.billing_document_versions USING btree (account_id, order_id, kind) WHERE (version_number = 1);


--
-- Name: billing_document_order_version; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX billing_document_order_version ON public.billing_document_versions USING btree (account_id, order_id, version_number DESC, version_id);


--
-- Name: billing_document_supersedes_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX billing_document_supersedes_unique ON public.billing_document_versions USING btree (account_id, supersedes_version_id) WHERE (supersedes_version_id IS NOT NULL);


--
-- Name: billing_invitation_recipient; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX billing_invitation_recipient ON public.billing_account_invitations USING btree (invited_actor_id, id);


--
-- Name: billing_invitation_target; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX billing_invitation_target ON public.billing_account_invitations USING btree (account_id, invited_actor_id, expires_at);


--
-- Name: capacity_delivery_retry; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX capacity_delivery_retry ON public.entitlement_outbox USING btree (next_attempt_at, event_id) WHERE (status = 'DeadLetter'::text);


--
-- Name: capacity_increase_source; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX capacity_increase_source ON public.capacity_increases USING btree (account_id, term_id, target_revision) WHERE (state = ANY (ARRAY['Pending'::text, 'Applied'::text]));


--
-- Name: checkout_order_shops_covered; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX checkout_order_shops_covered ON public.checkout_order_shops USING btree (account_id, shop_id, order_id);


--
-- Name: closure_proposal_account_operation; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX closure_proposal_account_operation ON public.operational_closure_proposals USING btree (account_id, COALESCE(operation_key, id));


--
-- Name: closure_targets_affiliation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX closure_targets_affiliation ON public.operational_closure_targets USING btree (account_id, shop_id, affiliation_request_id);


--
-- Name: complimentary_grant_shops_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX complimentary_grant_shops_shop ON public.complimentary_grant_shops USING btree (shop_id, starts_at, ends_at);


--
-- Name: complimentary_grant_shops_term; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX complimentary_grant_shops_term ON public.complimentary_grant_shops USING btree (term_id);


--
-- Name: confirmation_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX confirmation_order ON public.payment_confirmations USING btree (account_id, order_id, issued_at, confirmation_id);


--
-- Name: coverage_exception_active_approval; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_exception_active_approval ON public.coverage_replacement_exception_approvals USING btree (exception_id) WHERE (state = ANY (ARRAY['Preparing'::text, 'Consuming'::text, 'Applied'::text, 'Aborting'::text]));


--
-- Name: coverage_exception_inflight_term; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_exception_inflight_term ON public.coverage_replacement_exceptions USING btree (term_id) WHERE (state = ANY (ARRAY['Preparing'::text, 'Consuming'::text, 'Aborting'::text]));


--
-- Name: coverage_exception_open_incoming; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_exception_open_incoming ON public.coverage_replacement_exceptions USING btree (incoming_shop_id) WHERE (state = ANY (ARRAY['AwaitingConsents'::text, 'ReadyForReview'::text, 'Preparing'::text, 'Consuming'::text, 'Aborting'::text]));


--
-- Name: coverage_exception_open_source; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_exception_open_source ON public.coverage_replacement_exceptions USING btree (term_id, outgoing_shop_id) WHERE (state = ANY (ARRAY['AwaitingConsents'::text, 'ReadyForReview'::text, 'Preparing'::text, 'Consuming'::text, 'Aborting'::text]));


--
-- Name: coverage_slot_applied_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_slot_applied_shop ON public.coverage_slot_assignments USING btree (term_id, shop_id) WHERE (state = 'Applied'::text);


--
-- Name: coverage_slot_pending_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_slot_pending_shop ON public.coverage_slot_assignments USING btree (shop_id) WHERE (state = 'AwaitingConsent'::text);


--
-- Name: coverage_transfer_consent_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_transfer_consent_key ON public.coverage_transfer_consents USING btree (account_id, actor_id, operation_key) WHERE (operation_key IS NOT NULL);


--
-- Name: coverage_transfer_destination; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX coverage_transfer_destination ON public.coverage_transfer_proposals USING btree (destination_account_id, created_at);


--
-- Name: coverage_transfer_open_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX coverage_transfer_open_shop ON public.coverage_transfer_proposals USING btree (shop_id) WHERE (state = 'Open'::text);


--
-- Name: entitlement_outbox_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX entitlement_outbox_due ON public.entitlement_outbox USING btree (next_attempt_at, event_id) WHERE (status = ANY (ARRAY['Pending'::text, 'Leased'::text, 'AwaitingAcknowledgement'::text]));


--
-- Name: entitlement_shop_heads_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX entitlement_shop_heads_due ON public.entitlement_shop_heads USING btree (next_verification_at, shop_id);


--
-- Name: entitlement_snapshots_closure_cycle; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX entitlement_snapshots_closure_cycle ON public.entitlement_snapshots USING btree (shop_id, closure_revision, shop_revision DESC) WHERE (closure_revision IS NOT NULL);


--
-- Name: gateway_inbox_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gateway_inbox_due ON public.gateway_inbox_work USING btree (next_attempt_at, inbox_id) WHERE (status = ANY (ARRAY['Pending'::text, 'Leased'::text]));


--
-- Name: gateway_inbox_order_lookup; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX gateway_inbox_order_lookup ON public.payment_attempt_sessions USING btree (provider_order_reference);


--
-- Name: ix_admin_identities_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_identities_email ON public.admin_identities USING btree (email);


--
-- Name: ix_admin_invitations_accepted_admin_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_invitations_accepted_admin_id ON public.admin_invitations USING btree (accepted_admin_id);


--
-- Name: ix_admin_invitations_invited_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_admin_invitations_invited_by ON public.admin_invitations USING btree (invited_by);


--
-- Name: ix_admin_invitations_open_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_invitations_open_email ON public.admin_invitations USING btree (email) WHERE ((accepted_at IS NULL) AND (revoked_at IS NULL));


--
-- Name: ix_admin_invitations_token_digest; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_invitations_token_digest ON public.admin_invitations USING btree (token_digest);


--
-- Name: ix_admin_recoveries_admin_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_admin_recoveries_admin_id ON public.admin_recoveries USING btree (admin_id);


--
-- Name: ix_admin_recoveries_grant_digest; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_recoveries_grant_digest ON public.admin_recoveries USING btree (grant_digest);


--
-- Name: ix_admin_recoveries_reviewer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_admin_recoveries_reviewer_id ON public.admin_recoveries USING btree (reviewer_id);


--
-- Name: ix_admin_refresh_credentials_parent_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_refresh_credentials_parent_id ON public.admin_refresh_credentials USING btree (parent_id);


--
-- Name: ix_admin_refresh_credentials_session_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_admin_refresh_credentials_session_id ON public.admin_refresh_credentials USING btree (session_id);


--
-- Name: ix_admin_refresh_credentials_session_root; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_refresh_credentials_session_root ON public.admin_refresh_credentials USING btree (session_id) WHERE (parent_id IS NULL);


--
-- Name: ix_admin_refresh_credentials_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_admin_refresh_credentials_token_hash ON public.admin_refresh_credentials USING btree (token_hash);


--
-- Name: ix_admin_sessions_admin_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_admin_sessions_admin_id ON public.admin_sessions USING btree (admin_id);


--
-- Name: ix_admin_sign_in_buckets_window_ends_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_admin_sign_in_buckets_window_ends_at ON public.admin_sign_in_buckets USING btree (window_ends_at);


--
-- Name: ix_billing_account_members_actor; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_billing_account_members_actor ON public.billing_account_members USING btree (merchant_user_id, status, created_at);


--
-- Name: ix_billing_accounts_created_by_merchant_user_id_creation_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_billing_accounts_created_by_merchant_user_id_creation_key ON public.billing_accounts USING btree (created_by_merchant_user_id, creation_key);


--
-- Name: ix_billing_ownership_proposal_recipient; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_billing_ownership_proposal_recipient ON public.billing_ownership_proposals USING btree (recipient_actor_id, id) WHERE (recipient_actor_id IS NOT NULL);


--
-- Name: ix_bootstrap_enrollment_credentials_digest; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_bootstrap_enrollment_credentials_digest ON public.bootstrap_enrollment_credentials USING btree (digest);


--
-- Name: ix_bootstrap_enrollment_credentials_owner_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_bootstrap_enrollment_credentials_owner_id ON public.bootstrap_enrollment_credentials USING btree (owner_id) WHERE (revoked_at IS NULL);


--
-- Name: ix_catalogue_draft_changes_actor_admin_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_catalogue_draft_changes_actor_admin_id ON public.catalogue_draft_changes USING btree (actor_admin_id);


--
-- Name: ix_catalogue_draft_changes_target_kind_target_id_revision; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_catalogue_draft_changes_target_kind_target_id_revision ON public.catalogue_draft_changes USING btree (target_kind, target_id, revision);


--
-- Name: ix_catalogue_publications_admin_id_operation_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_catalogue_publications_admin_id_operation_key ON public.catalogue_publications USING btree (admin_id, operation_key);


--
-- Name: ix_catalogue_retirements_admin_id_operation_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_catalogue_retirements_admin_id_operation_key ON public.catalogue_retirements USING btree (admin_id, operation_key);


--
-- Name: ix_initial_admin_bootstrap_owner_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_initial_admin_bootstrap_owner_id ON public.initial_admin_bootstrap USING btree (owner_id);


--
-- Name: ix_merchant_billing_delegation_uses_account_id_actor_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_merchant_billing_delegation_uses_account_id_actor_id ON public.merchant_billing_delegation_uses USING btree (account_id, actor_id);


--
-- Name: ix_plan_versions_plan_id_version_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_plan_versions_plan_id_version_number ON public.plan_versions USING btree (plan_id, version_number);


--
-- Name: ix_plans_created_by_admin_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_plans_created_by_admin_id ON public.plans USING btree (created_by_admin_id);


--
-- Name: ix_provider_audit_events_invitation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_provider_audit_events_invitation_id ON public.provider_audit_events USING btree (invitation_id);


--
-- Name: ix_provider_audit_events_occurred_at_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_provider_audit_events_occurred_at_id ON public.provider_audit_events USING btree (occurred_at DESC, id DESC);


--
-- Name: ix_provider_audit_events_owner_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_provider_audit_events_owner_id ON public.provider_audit_events USING btree (owner_id);


--
-- Name: ix_published_catalogue_offers_development_fixture_plan_id_vers; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_published_catalogue_offers_development_fixture_plan_id_vers ON public.published_catalogue_offers USING btree (development_fixture, plan_id, version_number);


--
-- Name: ix_trial_policy_versions_version_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_trial_policy_versions_version_number ON public.trial_policy_versions USING btree (version_number);


--
-- Name: manual_receipt_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX manual_receipt_order ON public.manual_payment_receipts USING btree (account_id, order_id, verified_at);


--
-- Name: offline_device_observations_device; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX offline_device_observations_device ON public.offline_device_observations USING btree (account_id, shop_id, device_ref, observed_at DESC);


--
-- Name: operational_closure_reopenings_shop_history; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX operational_closure_reopenings_shop_history ON public.operational_closure_reopenings USING btree (account_id, shop_id, affiliation_request_id, version DESC);


--
-- Name: paid_slot_handoffs_incoming; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX paid_slot_handoffs_incoming ON public.paid_slot_handoffs USING btree (account_id, incoming_shop_id, term_id);


--
-- Name: paid_slot_handoffs_outgoing; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX paid_slot_handoffs_outgoing ON public.paid_slot_handoffs USING btree (account_id, outgoing_shop_id, term_id);


--
-- Name: paid_slot_handoffs_term; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX paid_slot_handoffs_term ON public.paid_slot_handoffs USING btree (account_id, term_id, slot_origin_shop_id, slot_generation);


--
-- Name: payment_events_attempt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX payment_events_attempt ON public.payment_events USING btree (attempt_id, kind);


--
-- Name: payment_fulfilment_shops_account_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX payment_fulfilment_shops_account_shop ON public.payment_fulfilment_shops USING btree (account_id, shop_id);


--
-- Name: payment_fulfilment_shops_term; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX payment_fulfilment_shops_term ON public.payment_fulfilment_shops USING btree (term_id);


--
-- Name: pending_payment_attempts_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX pending_payment_attempts_due ON public.pending_payment_attempts USING btree (next_attempt_at, attempt_id) WHERE (NOT completed);


--
-- Name: refund_access_decisions_term; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX refund_access_decisions_term ON public.refund_access_decisions USING btree (account_id, term_id, term_revision DESC) WHERE (treatment <> 'Preserve'::text);


--
-- Name: refund_approvals_receipt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX refund_approvals_receipt ON public.refund_approvals USING btree (confirmation_id);


--
-- Name: refund_attempt_payment; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX refund_attempt_payment ON public.refund_attempts USING btree (provider_id, provider_account_id, environment, payment_reference);


--
-- Name: refund_requests_account_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX refund_requests_account_order ON public.refund_requests USING btree (account_id, requested_at, refund_request_id);


--
-- Name: refund_work_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX refund_work_due ON public.refund_work USING btree (due_at, reservation_id);


--
-- Name: replacement_open_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX replacement_open_shop ON public.coverage_replacements USING btree (incoming_shop_id) WHERE (state = ANY (ARRAY['AwaitingConsent'::text, 'Scheduled'::text]));


--
-- Name: replacement_open_term; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX replacement_open_term ON public.coverage_replacements USING btree (term_id) WHERE (state = ANY (ARRAY['AwaitingConsent'::text, 'Scheduled'::text]));


--
-- Name: routing_event_attempt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX routing_event_attempt ON public.payment_routing_events USING btree (attempt_id, sequence DESC);


--
-- Name: scheduled_reduction_order_unique; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX scheduled_reduction_order_unique ON public.scheduled_capacity_reductions USING btree (account_id, order_id) WHERE (order_id IS NOT NULL);


--
-- Name: scheduled_subscription_changes_by_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX scheduled_subscription_changes_by_shop ON public.scheduled_subscription_change_shops USING btree (shop_id, change_id);


--
-- Name: service_restoration_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX service_restoration_order ON public.service_restoration_adjustments USING btree (account_id, order_id);


--
-- Name: shop_affiliation_requests_account; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX shop_affiliation_requests_account ON public.shop_affiliation_requests USING btree (account_id, created_at DESC);


--
-- Name: shop_affiliations_term; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX shop_affiliations_term ON public.shop_affiliations USING btree (term_id);


--
-- Name: single_active_scheduled_reduction; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX single_active_scheduled_reduction ON public.scheduled_capacity_reductions USING btree (account_id, term_id) WHERE (state = ANY (ARRAY['Scheduled'::text, 'Funded'::text, 'Applying'::text, 'NeedsResolution'::text, 'NeedsReview'::text]));


--
-- Name: subscription_term_facts_account; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX subscription_term_facts_account ON public.subscription_term_facts USING btree (account_id);


--
-- Name: trial_history_account_interval; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX trial_history_account_interval ON public.trial_history USING btree (account_id, ends_at, starts_at);


--
-- Name: unmatched_account_queue; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX unmatched_account_queue ON public.unmatched_payments USING btree (account_id, case_id);


--
-- Name: unmatched_gateway_identity; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX unmatched_gateway_identity ON public.unmatched_payments USING btree (provider_id, receiving_account_id, environment, provider_payment_reference, ((classification = ANY (ARRAY['ReceivingPinMismatch'::text, 'CurrencyMismatch'::text, 'Underpayment'::text, 'Overpayment'::text])))) WHERE (attempt_id IS NOT NULL);


--
-- Name: unmatched_manual_identity; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX unmatched_manual_identity ON public.unmatched_payments USING btree (manual_receipt_id) WHERE (manual_receipt_id IS NOT NULL);


--
-- Name: ux_billing_account_members_active_member; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_billing_account_members_active_member ON public.billing_account_members USING btree (account_id, merchant_user_id) WHERE (status = 1);


--
-- Name: ux_billing_account_members_active_owner; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_billing_account_members_active_owner ON public.billing_account_members USING btree (account_id) WHERE ((role = 1) AND (status = 1));


--
-- Name: ux_plan_versions_open_draft; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_plan_versions_open_draft ON public.plan_versions USING btree (plan_id) WHERE (status = 1);


--
-- Name: ux_plan_versions_published; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_plan_versions_published ON public.plan_versions USING btree (plan_id) WHERE (status = 2);


--
-- Name: ux_trial_policy_versions_open_draft; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_trial_policy_versions_open_draft ON public.trial_policy_versions USING btree (status) WHERE (status = 1);


--
-- Name: ux_trial_policy_versions_published; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_trial_policy_versions_published ON public.trial_policy_versions USING btree (status) WHERE (status = 2);


--
-- Name: admin_invitations admin_invitations_no_truncate; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER admin_invitations_no_truncate BEFORE TRUNCATE ON public.admin_invitations FOR EACH STATEMENT EXECUTE FUNCTION public.preserve_admin_invitation();


--
-- Name: admin_invitations admin_invitations_settled; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER admin_invitations_settled BEFORE DELETE OR UPDATE ON public.admin_invitations FOR EACH ROW EXECUTE FUNCTION public.preserve_admin_invitation();


--
-- Name: admin_recoveries admin_recovery_evidence_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER admin_recovery_evidence_immutable BEFORE DELETE OR UPDATE ON public.admin_recoveries FOR EACH ROW EXECUTE FUNCTION public.protect_admin_recovery_evidence();


--
-- Name: admin_recoveries admin_recovery_evidence_no_truncate; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER admin_recovery_evidence_no_truncate BEFORE TRUNCATE ON public.admin_recoveries FOR EACH STATEMENT EXECUTE FUNCTION public.protect_admin_recovery_evidence();


--
-- Name: billing_account_members billing_account_members_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_account_members_retained BEFORE DELETE OR TRUNCATE ON public.billing_account_members FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_accounts billing_account_owner_required; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER billing_account_owner_required AFTER INSERT ON public.billing_accounts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.check_billing_active_owner();


--
-- Name: billing_accounts billing_accounts_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_accounts_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_accounts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_invitation_audit billing_invitation_audit_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_invitation_audit_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_invitation_audit FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_account_invitations billing_invitation_grant_complete; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER billing_invitation_grant_complete AFTER UPDATE ON public.billing_account_invitations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.check_billing_invitation_grant();


--
-- Name: billing_invitation_issue_receipts billing_invitation_receipt_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_invitation_receipt_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_invitation_issue_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_account_invitations billing_invitation_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_invitation_retained BEFORE DELETE OR TRUNCATE ON public.billing_account_invitations FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_account_invitations billing_invitation_transition; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_invitation_transition BEFORE UPDATE ON public.billing_account_invitations FOR EACH ROW EXECUTE FUNCTION public.guard_billing_invitation_transition();


--
-- Name: billing_account_members billing_member_owner_required; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER billing_member_owner_required AFTER INSERT OR UPDATE ON public.billing_account_members DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.check_billing_active_owner();


--
-- Name: billing_member_removals billing_removal_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_removal_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_member_removals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_ownership_acceptances billing_transfer_acceptance_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_transfer_acceptance_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_ownership_acceptances FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_ownership_proposals billing_transfer_proposal_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER billing_transfer_proposal_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_ownership_proposals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: capacity_change_quotes capacity_quote_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER capacity_quote_guard BEFORE INSERT ON public.capacity_change_quotes FOR EACH ROW EXECUTE FUNCTION public.guard_capacity_quote();


--
-- Name: capacity_change_quote_shops capacity_quote_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER capacity_quote_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.capacity_change_quote_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: capacity_change_quotes capacity_quote_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER capacity_quote_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.capacity_change_quotes FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: capacity_change_quote_shops capacity_quote_shop_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER capacity_quote_shop_guard BEFORE INSERT ON public.capacity_change_quote_shops FOR EACH ROW EXECUTE FUNCTION public.guard_capacity_quote_shop();


--
-- Name: capacity_change_quote_shops capacity_quote_shop_sealed; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER capacity_quote_shop_sealed AFTER INSERT ON public.capacity_change_quote_shops DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_capacity_quote_seal();


--
-- Name: catalogue_draft_changes catalogue_draft_changes_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER catalogue_draft_changes_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.catalogue_draft_changes FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: catalogue_preparations catalogue_preparations_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER catalogue_preparations_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.catalogue_preparations FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: catalogue_publications catalogue_publication_state_required; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER catalogue_publication_state_required AFTER INSERT ON public.catalogue_publications DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_catalogue_publication_state();


--
-- Name: catalogue_publications catalogue_publications_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER catalogue_publications_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.catalogue_publications FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: catalogue_retirements catalogue_retirement_state_required; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER catalogue_retirement_state_required AFTER INSERT ON public.catalogue_retirements DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_catalogue_retirement_state();


--
-- Name: catalogue_retirements catalogue_retirements_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER catalogue_retirements_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.catalogue_retirements FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: billing_document_versions check_billing_document_supersession; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER check_billing_document_supersession AFTER INSERT ON public.billing_document_versions FOR EACH ROW EXECUTE FUNCTION public.guard_billing_document_supersession();


--
-- Name: checkout_order_acceptances checkout_acceptance_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER checkout_acceptance_guard BEFORE INSERT ON public.checkout_order_acceptances FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_acceptance();


--
-- Name: checkout_orders checkout_order_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER checkout_order_guard BEFORE INSERT ON public.checkout_orders FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_order();


--
-- Name: checkout_order_acceptances checkout_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER checkout_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.checkout_order_acceptances FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: checkout_order_shops checkout_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER checkout_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.checkout_order_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: checkout_orders checkout_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER checkout_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.checkout_orders FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: checkout_order_shops checkout_shop_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER checkout_shop_guard BEFORE INSERT ON public.checkout_order_shops FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_shop();


--
-- Name: checkout_order_shops checkout_shop_sealed; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER checkout_shop_sealed AFTER INSERT ON public.checkout_order_shops DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_checkout_shop_seal();


--
-- Name: complimentary_grants complimentary_grant_complete; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER complimentary_grant_complete AFTER INSERT ON public.complimentary_grants DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_complete_complimentary_grant();


--
-- Name: bootstrap_enrollment_credentials enrollment_requires_recovery_proof; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER enrollment_requires_recovery_proof AFTER INSERT ON public.bootstrap_enrollment_credentials DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_recovery_enrollment();


--
-- Name: gateway_inbox_events gateway_event_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gateway_event_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.gateway_inbox_events FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: gateway_inbox_work gateway_work_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER gateway_work_retained BEFORE DELETE OR TRUNCATE ON public.gateway_inbox_work FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: capacity_increases guard_capacity_increase; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_capacity_increase BEFORE INSERT OR UPDATE ON public.capacity_increases FOR EACH ROW EXECUTE FUNCTION public.guard_capacity_increase();


--
-- Name: checkout_trial_sources guard_checkout_trial_source; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_checkout_trial_source BEFORE INSERT ON public.checkout_trial_sources FOR EACH ROW EXECUTE FUNCTION public.guard_checkout_trial_source();


--
-- Name: complimentary_grant_scopes guard_complimentary_grant_scope; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_complimentary_grant_scope BEFORE DELETE OR UPDATE ON public.complimentary_grant_scopes FOR EACH ROW EXECUTE FUNCTION public.guard_complimentary_grant_scope();


--
-- Name: complimentary_grant_shops guard_complimentary_grant_shop; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_complimentary_grant_shop BEFORE INSERT ON public.complimentary_grant_shops FOR EACH ROW EXECUTE FUNCTION public.guard_complimentary_grant_shop();


--
-- Name: payment_confirmations guard_confirmation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_confirmation BEFORE INSERT ON public.payment_confirmations FOR EACH ROW EXECUTE FUNCTION public.guard_payment_confirmation();


--
-- Name: coverage_replacement_exceptions guard_coverage_exception; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_coverage_exception BEFORE UPDATE ON public.coverage_replacement_exceptions FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_exception();


--
-- Name: coverage_replacement_exception_approvals guard_coverage_exception_approval; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_coverage_exception_approval BEFORE UPDATE ON public.coverage_replacement_exception_approvals FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_exception_approval();


--
-- Name: coverage_transfer_proposals guard_coverage_transfer_proposal; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_coverage_transfer_proposal BEFORE UPDATE ON public.coverage_transfer_proposals FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_transfer_proposal();


--
-- Name: manual_payment_receipts guard_manual_payment_receipt; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_manual_payment_receipt BEFORE INSERT ON public.manual_payment_receipts FOR EACH ROW EXECUTE FUNCTION public.guard_manual_payment_receipt();


--
-- Name: paid_fresh_starts guard_paid_fresh_start; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_paid_fresh_start BEFORE INSERT ON public.paid_fresh_starts FOR EACH ROW EXECUTE FUNCTION public.guard_paid_fresh_start();


--
-- Name: paid_slot_handoffs guard_paid_slot_handoff; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_paid_slot_handoff BEFORE INSERT ON public.paid_slot_handoffs FOR EACH ROW EXECUTE FUNCTION public.guard_paid_slot_handoff();


--
-- Name: payment_attempts guard_payment_attempt; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_payment_attempt BEFORE INSERT ON public.payment_attempts FOR EACH ROW EXECUTE FUNCTION public.guard_payment_attempt();


--
-- Name: payment_fulfilments guard_payment_fulfilment_renewal; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_payment_fulfilment_renewal BEFORE INSERT ON public.payment_fulfilments FOR EACH ROW EXECUTE FUNCTION public.guard_payment_fulfilment_renewal();


--
-- Name: payment_fulfilment_shops guard_payment_fulfilment_shop; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_payment_fulfilment_shop BEFORE INSERT ON public.payment_fulfilment_shops FOR EACH ROW EXECUTE FUNCTION public.guard_payment_fulfilment_shop();


--
-- Name: payment_restoration_holds guard_payment_restoration_hold; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_payment_restoration_hold BEFORE INSERT ON public.payment_restoration_holds FOR EACH ROW EXECUTE FUNCTION public.guard_payment_restoration_hold();


--
-- Name: payment_trial_conversions guard_payment_trial_conversion; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_payment_trial_conversion BEFORE INSERT ON public.payment_trial_conversions FOR EACH ROW EXECUTE FUNCTION public.guard_payment_trial_conversion();


--
-- Name: operational_closure_reopenings guard_reopening; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_reopening BEFORE INSERT ON public.operational_closure_reopenings FOR EACH ROW EXECUTE FUNCTION public.guard_closure_reopening();


--
-- Name: coverage_replacements guard_replacement; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_replacement BEFORE UPDATE ON public.coverage_replacements FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_replacement();


--
-- Name: payment_provider_routing guard_selection; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_selection BEFORE UPDATE ON public.payment_provider_routing FOR EACH ROW EXECUTE FUNCTION public.guard_payment_provider_selection();


--
-- Name: service_restoration_adjustments guard_service_restoration_adjustment; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_service_restoration_adjustment BEFORE INSERT ON public.service_restoration_adjustments FOR EACH ROW EXECUTE FUNCTION public.guard_service_restoration_adjustment();


--
-- Name: coverage_slot_assignments guard_slot_assignment; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_slot_assignment BEFORE INSERT OR UPDATE ON public.coverage_slot_assignments FOR EACH ROW EXECUTE FUNCTION public.guard_slot_assignment();


--
-- Name: entitlement_account_heads guard_source_account_revision; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_source_account_revision BEFORE UPDATE ON public.entitlement_account_heads FOR EACH ROW EXECUTE FUNCTION public.guard_entitlement_source_revision();


--
-- Name: entitlement_shop_heads guard_source_shop_revision; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_source_shop_revision BEFORE UPDATE ON public.entitlement_shop_heads FOR EACH ROW EXECUTE FUNCTION public.guard_entitlement_source_revision();


--
-- Name: trial_history guard_trial_against_paid_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_trial_against_paid_coverage BEFORE INSERT ON public.trial_history FOR EACH ROW EXECUTE FUNCTION public.guard_trial_against_paid_coverage();


--
-- Name: unmatched_payments guard_unmatched_payment_source; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_unmatched_payment_source BEFORE INSERT ON public.unmatched_payments FOR EACH ROW EXECUTE FUNCTION public.guard_unmatched_payment_source();


--
-- Name: verified_payments guard_verified_payment; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_verified_payment BEFORE INSERT ON public.verified_payments FOR EACH ROW EXECUTE FUNCTION public.guard_verified_payment();


--
-- Name: initial_admin_bootstrap initial_admin_bootstrap_no_truncate; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER initial_admin_bootstrap_no_truncate BEFORE TRUNCATE ON public.initial_admin_bootstrap FOR EACH STATEMENT EXECUTE FUNCTION public.preserve_initial_admin_bootstrap();


--
-- Name: initial_admin_bootstrap initial_admin_bootstrap_terminal; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER initial_admin_bootstrap_terminal BEFORE DELETE OR UPDATE ON public.initial_admin_bootstrap FOR EACH ROW EXECUTE FUNCTION public.preserve_initial_admin_bootstrap();


--
-- Name: manual_payment_receipts manual_receipt_complete; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER manual_receipt_complete AFTER INSERT ON public.manual_payment_receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_complete_manual_payment_receipt();


--
-- Name: merchant_billing_delegation_uses merchant_billing_delegation_use_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER merchant_billing_delegation_use_immutable BEFORE DELETE OR UPDATE OR TRUNCATE ON public.merchant_billing_delegation_uses FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_delegation_use_mutation();


--
-- Name: payment_fulfilments payment_fulfilment_complete; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER payment_fulfilment_complete AFTER INSERT ON public.payment_fulfilments DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_complete_payment_fulfilment();


--
-- Name: plan_versions plan_retirement_evidence_required; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER plan_retirement_evidence_required AFTER UPDATE ON public.plan_versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (((old.status = 2) AND (new.status = 3))) EXECUTE FUNCTION public.require_plan_retirement_evidence();


--
-- Name: plan_versions plan_successor_source; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER plan_successor_source BEFORE INSERT ON public.plan_versions FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_version_insert();


--
-- Name: plan_versions plan_versions_draft_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER plan_versions_draft_guard BEFORE UPDATE ON public.plan_versions FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_draft_version();


--
-- Name: plan_versions plan_versions_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER plan_versions_retained BEFORE DELETE OR TRUNCATE ON public.plan_versions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: plans plans_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER plans_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.plans FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: refund_access_decisions prepare_approved_refund; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER prepare_approved_refund AFTER INSERT ON public.refund_access_decisions FOR EACH ROW EXECUTE FUNCTION public.prepare_approved_refund();


--
-- Name: provider_audit_events provider_audit_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER provider_audit_immutable BEFORE DELETE OR UPDATE OR TRUNCATE ON public.provider_audit_events FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_provider_audit_mutation();


--
-- Name: plan_versions public_catalogue_offer_required; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER public_catalogue_offer_required AFTER UPDATE ON public.plan_versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_catalogue_offer();


--
-- Name: published_catalogue_offers published_catalogue_offers_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER published_catalogue_offers_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.published_catalogue_offers FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: payment_attempts queue_pending_payment; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER queue_pending_payment AFTER INSERT ON public.payment_attempts FOR EACH ROW EXECUTE FUNCTION public.queue_pending_payment();


--
-- Name: capacity_reduction_previews reduction_preview_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_preview_guard BEFORE INSERT ON public.capacity_reduction_previews FOR EACH ROW EXECUTE FUNCTION public.guard_reduction_preview();


--
-- Name: capacity_reduction_preview_shops reduction_preview_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_preview_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.capacity_reduction_preview_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: capacity_reduction_previews reduction_preview_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_preview_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.capacity_reduction_previews FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: capacity_reduction_preview_shops reduction_preview_shop_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_preview_shop_guard BEFORE INSERT ON public.capacity_reduction_preview_shops FOR EACH ROW EXECUTE FUNCTION public.guard_reduction_preview_shop();


--
-- Name: capacity_reduction_preview_shops reduction_preview_shop_sealed; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER reduction_preview_shop_sealed AFTER INSERT ON public.capacity_reduction_preview_shops DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_reduction_preview_seal();


--
-- Name: scheduled_capacity_reduction_resolutions reduction_resolution_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_resolution_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.scheduled_capacity_reduction_resolutions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_capacity_reductions reduction_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_retained BEFORE DELETE OR TRUNCATE ON public.scheduled_capacity_reductions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_capacity_reduction_shops reduction_shop_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reduction_shop_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.scheduled_capacity_reduction_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_access_decisions refund_access_decision_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_access_decision_guard BEFORE INSERT ON public.refund_access_decisions FOR EACH ROW EXECUTE FUNCTION public.guard_refund_access_decision();


--
-- Name: refund_access_decisions refund_access_decision_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_access_decision_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_access_decisions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_applications refund_application_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_application_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_applications FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_approvals refund_approval_complete; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER refund_approval_complete AFTER INSERT ON public.refund_approvals DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_refund_access_decision();


--
-- Name: refund_approvals refund_approval_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_approval_guard BEFORE INSERT ON public.refund_approvals FOR EACH ROW EXECUTE FUNCTION public.guard_refund_approval();


--
-- Name: refund_approvals refund_approval_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_approval_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_approvals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_attempts refund_attempt_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_attempt_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_attempts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_requests refund_request_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_request_guard BEFORE INSERT ON public.refund_requests FOR EACH ROW EXECUTE FUNCTION public.guard_refund_request();


--
-- Name: refund_requests refund_request_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refund_request_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_requests FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_extensions refuse_converted_trial_extension; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER refuse_converted_trial_extension BEFORE INSERT ON public.trial_extensions FOR EACH ROW EXECUTE FUNCTION public.refuse_converted_trial_extension();


--
-- Name: scheduled_subscription_changes require_cancellation_evidence; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER require_cancellation_evidence AFTER INSERT ON public.scheduled_subscription_changes DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.guard_subscription_cancellation();


--
-- Name: scheduled_subscription_change_shops require_cancellation_shop; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER require_cancellation_shop BEFORE INSERT ON public.scheduled_subscription_change_shops FOR EACH ROW EXECUTE FUNCTION public.guard_subscription_cancellation_shop();


--
-- Name: checkout_orders require_checkout_trial_sources; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER require_checkout_trial_sources BEFORE INSERT ON public.checkout_orders FOR EACH ROW EXECUTE FUNCTION public.require_checkout_trial_sources();


--
-- Name: payment_review_decisions require_payment_review_recovery; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER require_payment_review_recovery AFTER INSERT ON public.payment_review_decisions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_payment_review_recovery();


--
-- Name: complimentary_grant_receipts require_service_restoration_adjustment; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER require_service_restoration_adjustment AFTER INSERT ON public.complimentary_grant_receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN ((new.payment_order_id IS NOT NULL)) EXECUTE FUNCTION public.require_service_restoration_adjustment();


--
-- Name: shop_affiliation_requests reserve_complimentary_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reserve_complimentary_coverage BEFORE INSERT ON public.shop_affiliation_requests FOR EACH ROW EXECUTE FUNCTION public.reserve_complimentary_coverage();


--
-- Name: shop_affiliations reserve_complimentary_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reserve_complimentary_coverage BEFORE INSERT ON public.shop_affiliations FOR EACH ROW EXECUTE FUNCTION public.reserve_complimentary_coverage();


--
-- Name: shop_affiliation_requests reserve_paid_fulfilment_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reserve_paid_fulfilment_coverage BEFORE INSERT ON public.shop_affiliation_requests FOR EACH ROW EXECUTE FUNCTION public.reserve_paid_fulfilment_coverage();


--
-- Name: shop_affiliations reserve_paid_fulfilment_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reserve_paid_fulfilment_coverage BEFORE INSERT ON public.shop_affiliations FOR EACH ROW EXECUTE FUNCTION public.reserve_paid_fulfilment_coverage();


--
-- Name: shop_affiliation_requests reserve_trial_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reserve_trial_coverage BEFORE INSERT ON public.shop_affiliation_requests FOR EACH ROW EXECUTE FUNCTION public.reserve_trial_coverage();


--
-- Name: shop_affiliations reserve_trial_coverage; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER reserve_trial_coverage BEFORE INSERT ON public.shop_affiliations FOR EACH ROW EXECUTE FUNCTION public.reserve_trial_coverage();


--
-- Name: coverage_transfer_approvals retain_approval; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_approval BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_transfer_approvals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: billing_document_versions retain_billing_documents; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_billing_documents BEFORE DELETE OR UPDATE OR TRUNCATE ON public.billing_document_versions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_transfer_bindings retain_binding; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_binding BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_transfer_bindings FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_replacement_exceptions retain_coverage_exception; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_coverage_exception BEFORE DELETE OR TRUNCATE ON public.coverage_replacement_exceptions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_replacement_exception_approvals retain_coverage_exception_approval; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_coverage_exception_approval BEFORE DELETE OR TRUNCATE ON public.coverage_replacement_exception_approvals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_transfer_consents retain_coverage_transfer_consent; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_coverage_transfer_consent BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_transfer_consents FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_transfer_evidence retain_coverage_transfer_evidence; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_coverage_transfer_evidence BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_transfer_evidence FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_transfer_proposals retain_coverage_transfer_proposal; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_coverage_transfer_proposal BEFORE DELETE OR TRUNCATE ON public.coverage_transfer_proposals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: entitlement_messages retain_entitlement_messages_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_entitlement_messages_rows BEFORE DELETE OR UPDATE ON public.entitlement_messages FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_messages retain_entitlement_messages_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_entitlement_messages_table BEFORE TRUNCATE ON public.entitlement_messages FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: capacity_increases retain_increase; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_increase BEFORE DELETE OR TRUNCATE ON public.capacity_increases FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: paid_slot_handoffs retain_paid_slot_handoff; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_paid_slot_handoff BEFORE DELETE OR UPDATE OR TRUNCATE ON public.paid_slot_handoffs FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_transfer_progress retain_progress; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_progress BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_transfer_progress FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_bindings retain_refund_bindings; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_refund_bindings BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_bindings FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_observations retain_refund_observations; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_refund_observations BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_observations FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: refund_sends retain_refund_sends; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_refund_sends BEFORE DELETE OR UPDATE OR TRUNCATE ON public.refund_sends FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_replacements retain_replacement; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_replacement BEFORE DELETE OR TRUNCATE ON public.coverage_replacements FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_provider_routing retain_row; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_row BEFORE DELETE ON public.payment_provider_routing FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: checkout_trial_sources retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.checkout_trial_sources FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grant_receipts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.complimentary_grant_receipts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grant_shops retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.complimentary_grant_shops FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grants retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.complimentary_grants FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: entitlement_merchant_inbox retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.entitlement_merchant_inbox FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_snapshots retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.entitlement_snapshots FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: manual_payment_receipts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.manual_payment_receipts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: offline_device_observations retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.offline_device_observations FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_consent_notices retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_closure_consent_notices FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_proposals retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_closure_proposals FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_reopenings retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_closure_reopenings FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_targets retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_closure_targets FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closures retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_closures FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restriction_deliveries retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_restriction_deliveries FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restriction_release_deliveries retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_restriction_release_deliveries FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restriction_releases retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_restriction_releases FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restrictions retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.operational_restrictions FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: paid_fresh_starts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.paid_fresh_starts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_attempt_sessions retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_attempt_sessions FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_attempts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_attempts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_confirmations retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_confirmations FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_events retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_events FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_fulfilment_deliveries retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_fulfilment_deliveries FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_fulfilment_shops retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_fulfilment_shops FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_fulfilments retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_fulfilments FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_provider_bindings retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_provider_bindings FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_provider_selections retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_provider_selections FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_restoration_holds retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_restoration_holds FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_review_decisions retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_review_decisions FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_routing_events retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_routing_events FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_trial_conversions retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.payment_trial_conversions FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_subscription_change_shops retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.scheduled_subscription_change_shops FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_subscription_changes retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.scheduled_subscription_changes FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: service_restoration_adjustments retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.service_restoration_adjustments FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliation_delivery_uses retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_affiliation_delivery_uses FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliation_inbox retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_affiliation_inbox FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliation_requests retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_affiliation_requests FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliations retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_affiliations FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_trial_delivery_uses retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_trial_delivery_uses FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_trial_inbox retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_trial_inbox FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: subscription_cancellation_receipts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.subscription_cancellation_receipts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: subscription_coverage_terms retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.subscription_coverage_terms FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: subscription_term_facts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.subscription_term_facts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_extension_receipts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.trial_extension_receipts FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_extensions retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.trial_extensions FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_history retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.trial_history FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: unmatched_payments retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.unmatched_payments FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: verified_payments retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.verified_payments FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: development_gateway_refunds retain_simulated_refund; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_simulated_refund BEFORE DELETE OR UPDATE OR TRUNCATE ON public.development_gateway_refunds FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_slot_assignments retain_slot; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_slot BEFORE DELETE OR TRUNCATE ON public.coverage_slot_assignments FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: coverage_slot_assignment_deliveries retain_slot_delivery; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_slot_delivery BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_slot_assignment_deliveries FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: entitlement_account_heads retain_source_account_head; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_source_account_head BEFORE DELETE ON public.entitlement_account_heads FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_shop_heads retain_source_shop_head; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_source_shop_head BEFORE DELETE ON public.entitlement_shop_heads FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_shop_heads retain_source_shop_head_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_source_shop_head_table BEFORE TRUNCATE ON public.entitlement_shop_heads FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: checkout_trial_sources retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.checkout_trial_sources FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grant_receipts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.complimentary_grant_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grant_scopes retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.complimentary_grant_scopes FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grant_shops retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.complimentary_grant_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: complimentary_grants retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.complimentary_grants FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: entitlement_merchant_inbox retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.entitlement_merchant_inbox FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_snapshots retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.entitlement_snapshots FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: manual_payment_receipts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.manual_payment_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: offline_device_observations retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.offline_device_observations FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_consent_notices retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_closure_consent_notices FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_proposals retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_closure_proposals FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_reopenings retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_closure_reopenings FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closure_targets retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_closure_targets FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_closures retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_closures FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restriction_deliveries retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_restriction_deliveries FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restriction_release_deliveries retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_restriction_release_deliveries FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restriction_releases retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_restriction_releases FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: operational_restrictions retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_restrictions FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: paid_fresh_starts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.paid_fresh_starts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_attempt_sessions retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_attempt_sessions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_attempts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_attempts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_confirmations retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_confirmations FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_events retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_events FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_fulfilment_deliveries retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_fulfilment_deliveries FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_fulfilment_shops retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_fulfilment_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_fulfilments retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_fulfilments FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_provider_bindings retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_provider_bindings FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_provider_routing retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_provider_routing FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_provider_selections retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_provider_selections FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_restoration_holds retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_restoration_holds FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_review_decisions retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_review_decisions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_routing_events retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_routing_events FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: payment_trial_conversions retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_trial_conversions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_subscription_change_shops retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.scheduled_subscription_change_shops FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_subscription_changes retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.scheduled_subscription_changes FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: service_restoration_adjustments retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.service_restoration_adjustments FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliation_delivery_uses retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_affiliation_delivery_uses FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliation_inbox retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_affiliation_inbox FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliation_requests retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_affiliation_requests FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_affiliations retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_affiliations FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_trial_delivery_uses retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_trial_delivery_uses FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: shop_trial_inbox retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_trial_inbox FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: subscription_cancellation_receipts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.subscription_cancellation_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: subscription_coverage_terms retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.subscription_coverage_terms FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: subscription_term_facts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.subscription_term_facts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_extension_receipts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.trial_extension_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_extensions retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.trial_extensions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: trial_history retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.trial_history FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: unmatched_payments retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.unmatched_payments FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: verified_payments retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.verified_payments FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();


--
-- Name: scheduled_capacity_reductions scheduled_reduction_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER scheduled_reduction_guard BEFORE INSERT OR UPDATE ON public.scheduled_capacity_reductions FOR EACH ROW EXECUTE FUNCTION public.guard_scheduled_reduction();


--
-- Name: scheduled_capacity_reduction_shops scheduled_reduction_shop_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER scheduled_reduction_shop_guard BEFORE INSERT ON public.scheduled_capacity_reduction_shops FOR EACH ROW EXECUTE FUNCTION public.guard_scheduled_reduction_shop();


--
-- Name: scheduled_capacity_reduction_shops scheduled_reduction_shop_sealed; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER scheduled_reduction_shop_sealed AFTER INSERT ON public.scheduled_capacity_reduction_shops DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_scheduled_reduction_seal();


--
-- Name: operational_restriction_releases sequence_release; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sequence_release BEFORE INSERT ON public.operational_restriction_releases FOR EACH ROW EXECUTE FUNCTION public.guard_suspension_release();


--
-- Name: operational_restrictions sequence_restriction; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sequence_restriction BEFORE INSERT ON public.operational_restrictions FOR EACH ROW EXECUTE FUNCTION public.guard_suspension_apply();


--
-- Name: shop_affiliations shop_affiliation_coverage_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER shop_affiliation_coverage_guard BEFORE INSERT ON public.shop_affiliations FOR EACH ROW EXECUTE FUNCTION public.guard_shop_affiliation_coverage();


--
-- Name: trial_extensions trial_extension_chain; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trial_extension_chain BEFORE INSERT ON public.trial_extensions FOR EACH ROW EXECUTE FUNCTION public.guard_trial_extension();


--
-- Name: trial_extensions trial_extension_receipt; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER trial_extension_receipt AFTER INSERT ON public.trial_extensions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_trial_extension_receipt();


--
-- Name: trial_policy_versions trial_policy_versions_draft_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trial_policy_versions_draft_guard BEFORE UPDATE ON public.trial_policy_versions FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_draft_version();


--
-- Name: trial_policy_versions trial_policy_versions_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trial_policy_versions_retained BEFORE DELETE OR TRUNCATE ON public.trial_policy_versions FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: trial_policy_versions trial_successor_source; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trial_successor_source BEFORE INSERT ON public.trial_policy_versions FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_version_insert();


--
-- Name: withdrawn_catalogue_offers withdrawn_catalogue_offers_retained; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER withdrawn_catalogue_offers_retained BEFORE DELETE OR UPDATE OR TRUNCATE ON public.withdrawn_catalogue_offers FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();


--
-- Name: shop_trial_inbox applied_trial_has_history; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT applied_trial_has_history FOREIGN KEY (term_id) REFERENCES public.trial_history(term_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: billing_account_invitations billing_account_invitations_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_account_invitations
    ADD CONSTRAINT billing_account_invitations_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: billing_document_versions billing_document_versions_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_document_versions
    ADD CONSTRAINT billing_document_versions_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: billing_document_versions billing_document_versions_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_document_versions
    ADD CONSTRAINT billing_document_versions_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_order_acceptances(account_id, order_id);


--
-- Name: billing_document_versions billing_document_versions_account_id_supersedes_version_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_document_versions
    ADD CONSTRAINT billing_document_versions_account_id_supersedes_version_id_fkey FOREIGN KEY (account_id, supersedes_version_id) REFERENCES public.billing_document_versions(account_id, version_id);


--
-- Name: billing_invitation_audit billing_invitation_audit_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_invitation_audit
    ADD CONSTRAINT billing_invitation_audit_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: billing_invitation_audit billing_invitation_audit_invitation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_invitation_audit
    ADD CONSTRAINT billing_invitation_audit_invitation_id_fkey FOREIGN KEY (invitation_id) REFERENCES public.billing_account_invitations(id);


--
-- Name: billing_invitation_issue_receipts billing_invitation_issue_receipts_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_invitation_issue_receipts
    ADD CONSTRAINT billing_invitation_issue_receipts_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: billing_member_removals billing_member_removals_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_member_removals
    ADD CONSTRAINT billing_member_removals_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: billing_ownership_acceptances billing_ownership_acceptances_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_acceptances
    ADD CONSTRAINT billing_ownership_acceptances_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: billing_ownership_acceptances billing_ownership_acceptances_transfer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_acceptances
    ADD CONSTRAINT billing_ownership_acceptances_transfer_id_fkey FOREIGN KEY (transfer_id) REFERENCES public.billing_ownership_proposals(id);


--
-- Name: billing_ownership_proposals billing_ownership_proposals_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_ownership_proposals
    ADD CONSTRAINT billing_ownership_proposals_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: capacity_change_quote_shops capacity_change_quote_shops_account_id_quote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quote_shops
    ADD CONSTRAINT capacity_change_quote_shops_account_id_quote_id_fkey FOREIGN KEY (account_id, quote_id) REFERENCES public.capacity_change_quotes(account_id, quote_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: capacity_change_quote_shops capacity_change_quote_shops_account_id_shop_id_affiliation_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quote_shops
    ADD CONSTRAINT capacity_change_quote_shops_account_id_shop_id_affiliation_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: capacity_change_quotes capacity_change_quotes_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quotes
    ADD CONSTRAINT capacity_change_quotes_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: capacity_change_quotes capacity_change_quotes_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_change_quotes
    ADD CONSTRAINT capacity_change_quotes_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: capacity_increases capacity_increases_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id);


--
-- Name: capacity_increases capacity_increases_account_id_order_id_fkey1; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_account_id_order_id_fkey1 FOREIGN KEY (account_id, order_id) REFERENCES public.capacity_change_quotes(account_id, quote_id);


--
-- Name: capacity_increases capacity_increases_account_id_order_id_receipt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_account_id_order_id_receipt_id_fkey FOREIGN KEY (account_id, order_id, receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id);


--
-- Name: capacity_increases capacity_increases_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: capacity_increases capacity_increases_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_attempt_id_fkey FOREIGN KEY (attempt_id) REFERENCES public.verified_payments(attempt_id);


--
-- Name: capacity_increases capacity_increases_consume_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_consume_event_fkey FOREIGN KEY (consume_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: capacity_increases capacity_increases_reserve_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_increases
    ADD CONSTRAINT capacity_increases_reserve_event_fkey FOREIGN KEY (reserve_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: capacity_reduction_preview_shops capacity_reduction_preview_sh_account_id_shop_id_affiliati_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_preview_shops
    ADD CONSTRAINT capacity_reduction_preview_sh_account_id_shop_id_affiliati_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: capacity_reduction_preview_shops capacity_reduction_preview_shops_account_id_preview_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_preview_shops
    ADD CONSTRAINT capacity_reduction_preview_shops_account_id_preview_id_fkey FOREIGN KEY (account_id, preview_id) REFERENCES public.capacity_reduction_previews(account_id, preview_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: capacity_reduction_previews capacity_reduction_previews_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_previews
    ADD CONSTRAINT capacity_reduction_previews_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: capacity_reduction_previews capacity_reduction_previews_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reduction_previews
    ADD CONSTRAINT capacity_reduction_previews_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: checkout_order_acceptances checkout_order_acceptances_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_acceptances
    ADD CONSTRAINT checkout_order_acceptances_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id);


--
-- Name: checkout_order_shops checkout_order_shops_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_shops
    ADD CONSTRAINT checkout_order_shops_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: checkout_order_shops checkout_order_shops_account_id_shop_id_affiliation_reques_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_order_shops
    ADD CONSTRAINT checkout_order_shops_account_id_shop_id_affiliation_reques_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: checkout_orders checkout_orders_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_orders
    ADD CONSTRAINT checkout_orders_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: checkout_orders checkout_orders_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_orders
    ADD CONSTRAINT checkout_orders_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: checkout_trial_sources checkout_trial_sources_account_id_order_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_trial_sources
    ADD CONSTRAINT checkout_trial_sources_account_id_order_id_shop_id_fkey FOREIGN KEY (account_id, order_id, shop_id) REFERENCES public.checkout_order_shops(account_id, order_id, shop_id);


--
-- Name: checkout_trial_sources checkout_trial_sources_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_trial_sources
    ADD CONSTRAINT checkout_trial_sources_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.trial_history(shop_id);


--
-- Name: complimentary_grant_receipts complimentary_grant_receipts_account_id_payment_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_receipts
    ADD CONSTRAINT complimentary_grant_receipts_account_id_payment_order_id_fkey FOREIGN KEY (account_id, payment_order_id) REFERENCES public.payment_fulfilments(account_id, order_id);


--
-- Name: complimentary_grant_receipts complimentary_grant_receipts_admin_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_receipts
    ADD CONSTRAINT complimentary_grant_receipts_admin_id_fkey FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id);


--
-- Name: complimentary_grant_receipts complimentary_grant_receipts_grant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_receipts
    ADD CONSTRAINT complimentary_grant_receipts_grant_id_fkey FOREIGN KEY (grant_id) REFERENCES public.complimentary_grants(grant_id);


--
-- Name: complimentary_grant_scopes complimentary_grant_scopes_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_scopes
    ADD CONSTRAINT complimentary_grant_scopes_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: complimentary_grant_shops complimentary_grant_shops_grant_id_account_id_term_id_star_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grant_shops
    ADD CONSTRAINT complimentary_grant_shops_grant_id_account_id_term_id_star_fkey FOREIGN KEY (grant_id, account_id, term_id, starts_at, ends_at) REFERENCES public.complimentary_grants(grant_id, account_id, term_id, starts_at, ends_at);


--
-- Name: complimentary_grants complimentary_grants_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grants
    ADD CONSTRAINT complimentary_grants_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: complimentary_grants complimentary_grants_account_id_term_id_starts_at_ends_at_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.complimentary_grants
    ADD CONSTRAINT complimentary_grants_account_id_term_id_starts_at_ends_at_fkey FOREIGN KEY (account_id, term_id, starts_at, ends_at) REFERENCES public.subscription_coverage_terms(account_id, term_id, starts_at, ends_at) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptio_account_id_incoming_shop_id__fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptio_account_id_incoming_shop_id__fkey FOREIGN KEY (account_id, incoming_shop_id, incoming_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptio_account_id_outgoing_shop_id__fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptio_account_id_outgoing_shop_id__fkey FOREIGN KEY (account_id, outgoing_shop_id, outgoing_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_abort_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_abort_event_fkey FOREIGN KEY (abort_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_admin_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_admin_id_fkey FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_consume_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_consume_event_fkey FOREIGN KEY (consume_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_courtesy_grant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_courtesy_grant_id_fkey FOREIGN KEY (courtesy_grant_id) REFERENCES public.complimentary_grants(grant_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_exception_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_exception_id_fkey FOREIGN KEY (exception_id) REFERENCES public.coverage_replacement_exceptions(exception_id);


--
-- Name: coverage_replacement_exception_approvals coverage_replacement_exception_approvals_prepare_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exception_approvals
    ADD CONSTRAINT coverage_replacement_exception_approvals_prepare_event_fkey FOREIGN KEY (prepare_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: coverage_replacement_exceptions coverage_replacement_exceptions_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_exceptions
    ADD CONSTRAINT coverage_replacement_exceptions_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: coverage_replacements coverage_replacements_account_id_incoming_shop_id_incoming_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacements
    ADD CONSTRAINT coverage_replacements_account_id_incoming_shop_id_incoming_fkey FOREIGN KEY (account_id, incoming_shop_id, incoming_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: coverage_replacements coverage_replacements_account_id_outgoing_shop_id_outgoing_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacements
    ADD CONSTRAINT coverage_replacements_account_id_outgoing_shop_id_outgoing_fkey FOREIGN KEY (account_id, outgoing_shop_id, outgoing_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: coverage_replacements coverage_replacements_account_id_successor_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacements
    ADD CONSTRAINT coverage_replacements_account_id_successor_term_id_fkey FOREIGN KEY (account_id, successor_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: coverage_replacements coverage_replacements_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacements
    ADD CONSTRAINT coverage_replacements_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: coverage_slot_assignment_deliveries coverage_slot_assignment_deliveries_assignment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignment_deliveries
    ADD CONSTRAINT coverage_slot_assignment_deliveries_assignment_id_fkey FOREIGN KEY (assignment_id) REFERENCES public.coverage_slot_assignments(assignment_id);


--
-- Name: coverage_slot_assignment_deliveries coverage_slot_assignment_deliveries_event_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignment_deliveries
    ADD CONSTRAINT coverage_slot_assignment_deliveries_event_id_fkey FOREIGN KEY (event_id) REFERENCES public.entitlement_snapshots(event_id);


--
-- Name: coverage_slot_assignments coverage_slot_assignments_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignments
    ADD CONSTRAINT coverage_slot_assignments_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id);


--
-- Name: coverage_slot_assignments coverage_slot_assignments_account_id_shop_id_affiliation_r_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignments
    ADD CONSTRAINT coverage_slot_assignments_account_id_shop_id_affiliation_r_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: coverage_slot_assignments coverage_slot_assignments_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_assignments
    ADD CONSTRAINT coverage_slot_assignments_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: coverage_transfer_approvals coverage_transfer_approvals_source_account_id_source_term__fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_approvals
    ADD CONSTRAINT coverage_transfer_approvals_source_account_id_source_term__fkey FOREIGN KEY (source_account_id, source_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: coverage_transfer_approvals coverage_transfer_approvals_transfer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_approvals
    ADD CONSTRAINT coverage_transfer_approvals_transfer_id_fkey FOREIGN KEY (transfer_id) REFERENCES public.coverage_transfer_proposals(transfer_id);


--
-- Name: coverage_transfer_bindings coverage_transfer_bindings_transfer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_bindings
    ADD CONSTRAINT coverage_transfer_bindings_transfer_id_fkey FOREIGN KEY (transfer_id) REFERENCES public.coverage_transfer_approvals(transfer_id);


--
-- Name: coverage_transfer_consents coverage_transfer_consents_transfer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consents
    ADD CONSTRAINT coverage_transfer_consents_transfer_id_fkey FOREIGN KEY (transfer_id) REFERENCES public.coverage_transfer_proposals(transfer_id);


--
-- Name: coverage_transfer_evidence coverage_transfer_evidence_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_evidence
    ADD CONSTRAINT coverage_transfer_evidence_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: coverage_transfer_evidence coverage_transfer_evidence_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_evidence
    ADD CONSTRAINT coverage_transfer_evidence_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: coverage_transfer_evidence coverage_transfer_evidence_transfer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_evidence
    ADD CONSTRAINT coverage_transfer_evidence_transfer_id_fkey FOREIGN KEY (transfer_id) REFERENCES public.coverage_transfer_proposals(transfer_id);


--
-- Name: coverage_transfer_progress coverage_transfer_progress_transfer_id_source_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_progress
    ADD CONSTRAINT coverage_transfer_progress_transfer_id_source_account_id_fkey FOREIGN KEY (transfer_id, source_account_id) REFERENCES public.coverage_transfer_approvals(transfer_id, source_account_id);


--
-- Name: coverage_transfer_proposals coverage_transfer_proposals_destination_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_proposals
    ADD CONSTRAINT coverage_transfer_proposals_destination_account_id_fkey FOREIGN KEY (destination_account_id) REFERENCES public.billing_accounts(id);


--
-- Name: coverage_transfer_proposals coverage_transfer_proposals_source_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_proposals
    ADD CONSTRAINT coverage_transfer_proposals_source_account_id_fkey FOREIGN KEY (source_account_id) REFERENCES public.billing_accounts(id);


--
-- Name: coverage_transfer_proposals coverage_transfer_proposals_source_account_id_shop_id_sour_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_proposals
    ADD CONSTRAINT coverage_transfer_proposals_source_account_id_shop_id_sour_fkey FOREIGN KEY (source_account_id, shop_id, source_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: coverage_transfer_proposals coverage_transfer_proposals_source_account_id_source_term__fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_proposals
    ADD CONSTRAINT coverage_transfer_proposals_source_account_id_source_term__fkey FOREIGN KEY (source_account_id, source_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: development_gateway_controls development_gateway_controls_provider_order_reference_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_controls
    ADD CONSTRAINT development_gateway_controls_provider_order_reference_fkey FOREIGN KEY (provider_order_reference) REFERENCES public.development_gateway_sessions(provider_order_reference);


--
-- Name: development_gateway_payments development_gateway_payments_provider_order_reference_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.development_gateway_payments
    ADD CONSTRAINT development_gateway_payments_provider_order_reference_fkey FOREIGN KEY (provider_order_reference) REFERENCES public.development_gateway_sessions(provider_order_reference);


--
-- Name: entitlement_account_heads entitlement_account_heads_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_account_heads
    ADD CONSTRAINT entitlement_account_heads_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: entitlement_outbox entitlement_outbox_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_outbox
    ADD CONSTRAINT entitlement_outbox_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id);


--
-- Name: entitlement_shop_heads entitlement_shop_heads_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_shop_heads
    ADD CONSTRAINT entitlement_shop_heads_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.entitlement_account_heads(account_id);


--
-- Name: entitlement_snapshots entitlement_snapshots_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_snapshots
    ADD CONSTRAINT entitlement_snapshots_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id);


--
-- Name: admin_invitations fk_admin_invitations_admin_identities_accepted_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_invitations
    ADD CONSTRAINT fk_admin_invitations_admin_identities_accepted_admin_id FOREIGN KEY (accepted_admin_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: admin_invitations fk_admin_invitations_admin_identities_invited_by; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_invitations
    ADD CONSTRAINT fk_admin_invitations_admin_identities_invited_by FOREIGN KEY (invited_by) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: admin_recoveries fk_admin_recoveries_admin_identities_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_recoveries
    ADD CONSTRAINT fk_admin_recoveries_admin_identities_admin_id FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: admin_recoveries fk_admin_recoveries_admin_identities_reviewer_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_recoveries
    ADD CONSTRAINT fk_admin_recoveries_admin_identities_reviewer_id FOREIGN KEY (reviewer_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: admin_recovery_codes fk_admin_recovery_codes_admin_totp_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_recovery_codes
    ADD CONSTRAINT fk_admin_recovery_codes_admin_totp_admin_id FOREIGN KEY (admin_id) REFERENCES public.admin_totp(admin_id) ON DELETE RESTRICT;


--
-- Name: admin_refresh_credentials fk_admin_refresh_credentials_admin_refresh_credentials_parent_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_refresh_credentials
    ADD CONSTRAINT fk_admin_refresh_credentials_admin_refresh_credentials_parent_ FOREIGN KEY (parent_id) REFERENCES public.admin_refresh_credentials(id) ON DELETE RESTRICT;


--
-- Name: admin_refresh_credentials fk_admin_refresh_credentials_admin_sessions_session_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_refresh_credentials
    ADD CONSTRAINT fk_admin_refresh_credentials_admin_sessions_session_id FOREIGN KEY (session_id) REFERENCES public.admin_sessions(id) ON DELETE RESTRICT;


--
-- Name: admin_sessions fk_admin_sessions_admin_identities_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_sessions
    ADD CONSTRAINT fk_admin_sessions_admin_identities_admin_id FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: admin_totp fk_admin_totp_admin_identities_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_totp
    ADD CONSTRAINT fk_admin_totp_admin_identities_admin_id FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: billing_account_members fk_billing_account_members_billing_accounts_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.billing_account_members
    ADD CONSTRAINT fk_billing_account_members_billing_accounts_account_id FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id) ON DELETE RESTRICT;


--
-- Name: bootstrap_enrollment_credentials fk_bootstrap_enrollment_credentials_admin_identities_owner_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bootstrap_enrollment_credentials
    ADD CONSTRAINT fk_bootstrap_enrollment_credentials_admin_identities_owner_id FOREIGN KEY (owner_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: catalogue_draft_changes fk_catalogue_draft_changes_admin_identities_actor_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_draft_changes
    ADD CONSTRAINT fk_catalogue_draft_changes_admin_identities_actor_admin_id FOREIGN KEY (actor_admin_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: catalogue_publications fk_catalogue_publications_catalogue_preparations_admin_id_oper; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_publications
    ADD CONSTRAINT fk_catalogue_publications_catalogue_preparations_admin_id_oper FOREIGN KEY (admin_id, operation_key) REFERENCES public.catalogue_preparations(admin_id, operation_key) ON DELETE RESTRICT;


--
-- Name: catalogue_retirements fk_catalogue_retirements_catalogue_publications_version_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.catalogue_retirements
    ADD CONSTRAINT fk_catalogue_retirements_catalogue_publications_version_id FOREIGN KEY (version_id) REFERENCES public.catalogue_publications(version_id) ON DELETE RESTRICT;


--
-- Name: initial_admin_bootstrap fk_initial_admin_bootstrap_admin_identity_owner_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.initial_admin_bootstrap
    ADD CONSTRAINT fk_initial_admin_bootstrap_admin_identity_owner_id FOREIGN KEY (owner_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: plan_versions fk_plan_versions_plans_plan_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plan_versions
    ADD CONSTRAINT fk_plan_versions_plans_plan_id FOREIGN KEY (plan_id) REFERENCES public.plans(id) ON DELETE RESTRICT;


--
-- Name: plans fk_plans_admin_identities_created_by_admin_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plans
    ADD CONSTRAINT fk_plans_admin_identities_created_by_admin_id FOREIGN KEY (created_by_admin_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: provider_audit_events fk_provider_audit_events_admin_identities_owner_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.provider_audit_events
    ADD CONSTRAINT fk_provider_audit_events_admin_identities_owner_id FOREIGN KEY (owner_id) REFERENCES public.admin_identities(id) ON DELETE RESTRICT;


--
-- Name: provider_audit_events fk_provider_audit_events_admin_invitation_invitation_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.provider_audit_events
    ADD CONSTRAINT fk_provider_audit_events_admin_invitation_invitation_id FOREIGN KEY (invitation_id) REFERENCES public.admin_invitations(id) ON DELETE RESTRICT;


--
-- Name: published_catalogue_offers fk_published_catalogue_offers_catalogue_publications_version_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.published_catalogue_offers
    ADD CONSTRAINT fk_published_catalogue_offers_catalogue_publications_version_id FOREIGN KEY (version_id) REFERENCES public.catalogue_publications(version_id) ON DELETE RESTRICT;


--
-- Name: withdrawn_catalogue_offers fk_withdrawn_catalogue_offers_catalogue_retirements_version_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.withdrawn_catalogue_offers
    ADD CONSTRAINT fk_withdrawn_catalogue_offers_catalogue_retirements_version_id FOREIGN KEY (version_id) REFERENCES public.catalogue_retirements(version_id) ON DELETE RESTRICT;


--
-- Name: withdrawn_catalogue_offers fk_withdrawn_catalogue_offers_published_catalogue_offers_versi; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.withdrawn_catalogue_offers
    ADD CONSTRAINT fk_withdrawn_catalogue_offers_published_catalogue_offers_versi FOREIGN KEY (version_id) REFERENCES public.published_catalogue_offers(version_id) ON DELETE RESTRICT;


--
-- Name: gateway_inbox_work gateway_inbox_work_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gateway_inbox_work
    ADD CONSTRAINT gateway_inbox_work_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id) MATCH FULL;


--
-- Name: gateway_inbox_work gateway_inbox_work_inbox_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.gateway_inbox_work
    ADD CONSTRAINT gateway_inbox_work_inbox_id_fkey FOREIGN KEY (inbox_id) REFERENCES public.gateway_inbox_events(inbox_id);


--
-- Name: operational_closure_consent_notices operational_closure_consent_notices_proposal_id_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_consent_notices
    ADD CONSTRAINT operational_closure_consent_notices_proposal_id_account_id_fkey FOREIGN KEY (proposal_id, account_id) REFERENCES public.operational_closure_proposals(id, account_id);


--
-- Name: operational_closure_proposals operational_closure_proposals_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_proposals
    ADD CONSTRAINT operational_closure_proposals_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: operational_closure_reopenings operational_closure_reopening_account_id_shop_id_affiliati_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopening_account_id_shop_id_affiliati_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_closure_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_closure_id_shop_id_fkey FOREIGN KEY (closure_id, shop_id) REFERENCES public.operational_closure_targets(closure_id, shop_id);


--
-- Name: operational_closure_reopenings operational_closure_reopenings_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: operational_closure_reopenings operational_closure_reopenings_reopened_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_reopenings
    ADD CONSTRAINT operational_closure_reopenings_reopened_by_fkey FOREIGN KEY (reopened_by) REFERENCES public.admin_identities(id);


--
-- Name: operational_closure_targets operational_closure_targets_account_id_shop_id_affiliation_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_targets
    ADD CONSTRAINT operational_closure_targets_account_id_shop_id_affiliation_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: operational_closure_targets operational_closure_targets_closure_id_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_targets
    ADD CONSTRAINT operational_closure_targets_closure_id_account_id_fkey FOREIGN KEY (closure_id, account_id) REFERENCES public.operational_closures(id, account_id);


--
-- Name: operational_closure_targets operational_closure_targets_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closure_targets
    ADD CONSTRAINT operational_closure_targets_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: operational_closures operational_closures_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_closures
    ADD CONSTRAINT operational_closures_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: operational_restriction_deliveries operational_restriction_delive_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_deliveries
    ADD CONSTRAINT operational_restriction_delive_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id);


--
-- Name: operational_restriction_deliveries operational_restriction_deliveri_restriction_id_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_deliveries
    ADD CONSTRAINT operational_restriction_deliveri_restriction_id_account_id_fkey FOREIGN KEY (restriction_id, account_id) REFERENCES public.operational_restrictions(id, account_id);


--
-- Name: operational_restriction_release_deliveries operational_restriction_releas_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_release_deliveries
    ADD CONSTRAINT operational_restriction_releas_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id);


--
-- Name: operational_restriction_release_deliveries operational_restriction_release_deli_release_id_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_release_deliveries
    ADD CONSTRAINT operational_restriction_release_deli_release_id_account_id_fkey FOREIGN KEY (release_id, account_id) REFERENCES public.operational_restriction_releases(id, account_id);


--
-- Name: operational_restriction_releases operational_restriction_releases_released_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_releases
    ADD CONSTRAINT operational_restriction_releases_released_by_fkey FOREIGN KEY (released_by) REFERENCES public.admin_identities(id);


--
-- Name: operational_restriction_releases operational_restriction_releases_restriction_id_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restriction_releases
    ADD CONSTRAINT operational_restriction_releases_restriction_id_account_id_fkey FOREIGN KEY (restriction_id, account_id) REFERENCES public.operational_restrictions(id, account_id);


--
-- Name: operational_restrictions operational_restrictions_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restrictions
    ADD CONSTRAINT operational_restrictions_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: operational_restrictions operational_restrictions_actor_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operational_restrictions
    ADD CONSTRAINT operational_restrictions_actor_id_fkey FOREIGN KEY (actor_id) REFERENCES public.admin_identities(id);


--
-- Name: paid_fresh_starts paid_fresh_starts_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_fresh_starts
    ADD CONSTRAINT paid_fresh_starts_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: paid_slot_handoffs paid_slot_handoffs_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_account_id_incoming_shop_id_incoming_af_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_account_id_incoming_shop_id_incoming_af_fkey FOREIGN KEY (account_id, incoming_shop_id, incoming_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_account_id_outgoing_shop_id_outgoing_af_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_account_id_outgoing_shop_id_outgoing_af_fkey FOREIGN KEY (account_id, outgoing_shop_id, outgoing_affiliation_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_approval_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_approval_id_fkey FOREIGN KEY (approval_id) REFERENCES public.coverage_replacement_exception_approvals(approval_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_courtesy_grant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_courtesy_grant_id_fkey FOREIGN KEY (courtesy_grant_id) REFERENCES public.complimentary_grants(grant_id);


--
-- Name: paid_slot_handoffs paid_slot_handoffs_exception_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.paid_slot_handoffs
    ADD CONSTRAINT paid_slot_handoffs_exception_id_fkey FOREIGN KEY (exception_id) REFERENCES public.coverage_replacement_exceptions(exception_id);


--
-- Name: payment_attempt_sessions payment_attempt_sessions_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempt_sessions
    ADD CONSTRAINT payment_attempt_sessions_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id);


--
-- Name: payment_attempts payment_attempts_selection_revision_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_attempts
    ADD CONSTRAINT payment_attempts_selection_revision_fkey FOREIGN KEY (selection_revision) REFERENCES public.payment_provider_selections(revision);


--
-- Name: payment_confirmations payment_confirmations_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_confirmations
    ADD CONSTRAINT payment_confirmations_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id);


--
-- Name: payment_confirmations payment_confirmations_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_confirmations
    ADD CONSTRAINT payment_confirmations_attempt_id_fkey FOREIGN KEY (attempt_id) REFERENCES public.verified_payments(attempt_id);


--
-- Name: payment_confirmations payment_confirmations_manual_receipt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_confirmations
    ADD CONSTRAINT payment_confirmations_manual_receipt_id_fkey FOREIGN KEY (manual_receipt_id) REFERENCES public.manual_payment_receipts(receipt_id);


--
-- Name: payment_events payment_events_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_events
    ADD CONSTRAINT payment_events_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id);


--
-- Name: payment_fulfilment_deliveries payment_fulfilment_deliveries_account_id_order_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_deliveries
    ADD CONSTRAINT payment_fulfilment_deliveries_account_id_order_id_shop_id_fkey FOREIGN KEY (account_id, order_id, shop_id) REFERENCES public.payment_fulfilment_shops(account_id, order_id, shop_id);


--
-- Name: payment_fulfilments payment_fulfilment_predecessor; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilment_predecessor FOREIGN KEY (account_id, predecessor_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: payment_fulfilment_shops payment_fulfilment_shops_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_shops
    ADD CONSTRAINT payment_fulfilment_shops_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id);


--
-- Name: payment_fulfilment_shops payment_fulfilment_shops_account_id_shop_id_affiliation_re_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_shops
    ADD CONSTRAINT payment_fulfilment_shops_account_id_shop_id_affiliation_re_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: payment_fulfilment_shops payment_fulfilment_shops_replacement_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_shops
    ADD CONSTRAINT payment_fulfilment_shops_replacement_id_fkey FOREIGN KEY (replacement_id) REFERENCES public.coverage_replacements(replacement_id);


--
-- Name: payment_fulfilment_shops payment_fulfilment_shops_slot_assignment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilment_shops
    ADD CONSTRAINT payment_fulfilment_shops_slot_assignment_id_fkey FOREIGN KEY (slot_assignment_id) REFERENCES public.coverage_slot_assignments(assignment_id);


--
-- Name: payment_fulfilments payment_fulfilments_account_id_order_id_manual_receipt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_account_id_order_id_manual_receipt_id_fkey FOREIGN KEY (account_id, order_id, manual_receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id);


--
-- Name: payment_fulfilments payment_fulfilments_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: payment_fulfilments payment_fulfilments_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_fulfilments
    ADD CONSTRAINT payment_fulfilments_attempt_id_fkey FOREIGN KEY (attempt_id) REFERENCES public.verified_payments(attempt_id);


--
-- Name: payment_restoration_holds payment_restoration_holds_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_restoration_holds
    ADD CONSTRAINT payment_restoration_holds_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id);


--
-- Name: payment_restoration_holds payment_restoration_holds_account_id_order_id_manual_recei_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_restoration_holds
    ADD CONSTRAINT payment_restoration_holds_account_id_order_id_manual_recei_fkey FOREIGN KEY (account_id, order_id, manual_receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id);


--
-- Name: payment_restoration_holds payment_restoration_holds_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_restoration_holds
    ADD CONSTRAINT payment_restoration_holds_attempt_id_fkey FOREIGN KEY (attempt_id) REFERENCES public.verified_payments(attempt_id);


--
-- Name: payment_review_decisions payment_review_decisions_account_id_case_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_review_decisions
    ADD CONSTRAINT payment_review_decisions_account_id_case_id_fkey FOREIGN KEY (account_id, case_id) REFERENCES public.unmatched_payments(account_id, case_id);


--
-- Name: payment_review_decisions payment_review_decisions_admin_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_review_decisions
    ADD CONSTRAINT payment_review_decisions_admin_id_fkey FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id);


--
-- Name: payment_routing_events payment_routing_events_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_routing_events
    ADD CONSTRAINT payment_routing_events_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id);


--
-- Name: payment_trial_conversions payment_trial_conversions_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_trial_conversions
    ADD CONSTRAINT payment_trial_conversions_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.paid_fresh_starts(account_id, order_id);


--
-- Name: payment_trial_conversions payment_trial_conversions_account_id_order_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_trial_conversions
    ADD CONSTRAINT payment_trial_conversions_account_id_order_id_shop_id_fkey FOREIGN KEY (account_id, order_id, shop_id) REFERENCES public.checkout_trial_sources(account_id, order_id, shop_id);


--
-- Name: payment_trial_conversions payment_trial_conversions_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_trial_conversions
    ADD CONSTRAINT payment_trial_conversions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.trial_history(shop_id);


--
-- Name: pending_payment_attempts pending_payment_attempts_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.pending_payment_attempts
    ADD CONSTRAINT pending_payment_attempts_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id);


--
-- Name: refund_access_decisions refund_access_decisions_account_id_approval_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_account_id_approval_id_fkey FOREIGN KEY (account_id, approval_id) REFERENCES public.refund_approvals(account_id, approval_id);


--
-- Name: refund_access_decisions refund_access_decisions_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: refund_access_decisions refund_access_decisions_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_access_decisions
    ADD CONSTRAINT refund_access_decisions_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: refund_applications refund_applications_account_id_decision_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_applications
    ADD CONSTRAINT refund_applications_account_id_decision_id_fkey FOREIGN KEY (account_id, decision_id) REFERENCES public.refund_access_decisions(account_id, decision_id);


--
-- Name: refund_approvals refund_approvals_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: refund_approvals refund_approvals_account_id_refund_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_approvals
    ADD CONSTRAINT refund_approvals_account_id_refund_request_id_fkey FOREIGN KEY (account_id, refund_request_id) REFERENCES public.refund_requests(account_id, refund_request_id);


--
-- Name: refund_attempts refund_attempts_account_id_decision_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_attempts
    ADD CONSTRAINT refund_attempts_account_id_decision_id_fkey FOREIGN KEY (account_id, decision_id) REFERENCES public.refund_access_decisions(account_id, decision_id);


--
-- Name: refund_attempts refund_attempts_reservation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_attempts
    ADD CONSTRAINT refund_attempts_reservation_id_fkey FOREIGN KEY (reservation_id) REFERENCES public.refund_approvals(reservation_id);


--
-- Name: refund_bindings refund_bindings_reservation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_bindings
    ADD CONSTRAINT refund_bindings_reservation_id_fkey FOREIGN KEY (reservation_id) REFERENCES public.refund_sends(reservation_id);


--
-- Name: refund_observations refund_observations_reservation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_observations
    ADD CONSTRAINT refund_observations_reservation_id_fkey FOREIGN KEY (reservation_id) REFERENCES public.refund_attempts(reservation_id);


--
-- Name: refund_requests refund_requests_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_requests
    ADD CONSTRAINT refund_requests_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: refund_requests refund_requests_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_requests
    ADD CONSTRAINT refund_requests_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id);


--
-- Name: refund_sends refund_sends_reservation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_sends
    ADD CONSTRAINT refund_sends_reservation_id_fkey FOREIGN KEY (reservation_id) REFERENCES public.refund_attempts(reservation_id);


--
-- Name: refund_work refund_work_reservation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_work
    ADD CONSTRAINT refund_work_reservation_id_fkey FOREIGN KEY (reservation_id) REFERENCES public.refund_attempts(reservation_id);


--
-- Name: scheduled_capacity_reduction_shops scheduled_capacity_reduction__account_id_shop_id_affiliati_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_shops
    ADD CONSTRAINT scheduled_capacity_reduction__account_id_shop_id_affiliati_fkey FOREIGN KEY (account_id, shop_id, affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: scheduled_capacity_reduction_resolutions scheduled_capacity_reduction_resolu_account_id_schedule_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_resolutions
    ADD CONSTRAINT scheduled_capacity_reduction_resolu_account_id_schedule_id_fkey FOREIGN KEY (account_id, schedule_id) REFERENCES public.scheduled_capacity_reductions(account_id, schedule_id);


--
-- Name: scheduled_capacity_reduction_shops scheduled_capacity_reduction_shops_account_id_schedule_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reduction_shops
    ADD CONSTRAINT scheduled_capacity_reduction_shops_account_id_schedule_id_fkey FOREIGN KEY (account_id, schedule_id) REFERENCES public.scheduled_capacity_reductions(account_id, schedule_id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_account_id_order_id_receipt__fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_account_id_order_id_receipt__fkey FOREIGN KEY (account_id, order_id, receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_account_id_preview_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_account_id_preview_id_fkey FOREIGN KEY (account_id, preview_id) REFERENCES public.capacity_reduction_previews(account_id, preview_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_attempt_id_fkey FOREIGN KEY (attempt_id) REFERENCES public.verified_payments(attempt_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_consume_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_consume_event_fkey FOREIGN KEY (consume_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: scheduled_capacity_reductions scheduled_capacity_reductions_reserve_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_capacity_reductions
    ADD CONSTRAINT scheduled_capacity_reductions_reserve_event_fkey FOREIGN KEY (reserve_event) REFERENCES public.entitlement_messages(event_id);


--
-- Name: scheduled_subscription_change_shops scheduled_subscription_change_account_id_shop_id_request_i_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_change_shops
    ADD CONSTRAINT scheduled_subscription_change_account_id_shop_id_request_i_fkey FOREIGN KEY (account_id, shop_id, request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: scheduled_subscription_change_shops scheduled_subscription_change_shops_account_id_change_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_change_shops
    ADD CONSTRAINT scheduled_subscription_change_shops_account_id_change_id_fkey FOREIGN KEY (account_id, change_id) REFERENCES public.scheduled_subscription_changes(account_id, change_id);


--
-- Name: scheduled_subscription_changes scheduled_subscription_changes_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_changes
    ADD CONSTRAINT scheduled_subscription_changes_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: scheduled_subscription_changes scheduled_subscription_changes_withdrawn_change; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scheduled_subscription_changes
    ADD CONSTRAINT scheduled_subscription_changes_withdrawn_change FOREIGN KEY (account_id, withdraws_change_id) REFERENCES public.scheduled_subscription_changes(account_id, change_id);


--
-- Name: service_restoration_adjustments service_restoration_adjustments_account_id_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_restoration_adjustments
    ADD CONSTRAINT service_restoration_adjustments_account_id_order_id_fkey FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id);


--
-- Name: service_restoration_adjustments service_restoration_adjustments_account_id_paid_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_restoration_adjustments
    ADD CONSTRAINT service_restoration_adjustments_account_id_paid_term_id_fkey FOREIGN KEY (account_id, paid_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: service_restoration_adjustments service_restoration_adjustments_grant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_restoration_adjustments
    ADD CONSTRAINT service_restoration_adjustments_grant_id_fkey FOREIGN KEY (grant_id) REFERENCES public.complimentary_grant_receipts(grant_id);


--
-- Name: shop_affiliation_delivery_uses shop_affiliation_delivery_uses_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_delivery_uses
    ADD CONSTRAINT shop_affiliation_delivery_uses_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: shop_affiliation_inbox shop_affiliation_inbox_account_id_shop_id_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_inbox
    ADD CONSTRAINT shop_affiliation_inbox_account_id_shop_id_request_id_fkey FOREIGN KEY (account_id, shop_id, request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: shop_affiliation_requests shop_affiliation_requests_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_requests
    ADD CONSTRAINT shop_affiliation_requests_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: shop_affiliation_requests shop_affiliation_requests_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_requests
    ADD CONSTRAINT shop_affiliation_requests_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: shop_affiliations shop_affiliations_account_id_shop_id_request_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliations
    ADD CONSTRAINT shop_affiliations_account_id_shop_id_request_id_fkey FOREIGN KEY (account_id, shop_id, request_id) REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id);


--
-- Name: shop_affiliations shop_affiliations_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliations
    ADD CONSTRAINT shop_affiliations_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: shop_trial_inbox shop_trial_inbox_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT shop_trial_inbox_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: shop_trial_inbox shop_trial_inbox_replay_id_event_id_shop_id_digest_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_inbox
    ADD CONSTRAINT shop_trial_inbox_replay_id_event_id_shop_id_digest_fkey FOREIGN KEY (replay_id, event_id, shop_id, digest) REFERENCES public.shop_trial_delivery_uses(replay_id, event_id, shop_id, digest);


--
-- Name: subscription_cancellation_receipts subscription_cancellation_receipts_account_id_change_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_cancellation_receipts
    ADD CONSTRAINT subscription_cancellation_receipts_account_id_change_id_fkey FOREIGN KEY (account_id, change_id) REFERENCES public.scheduled_subscription_changes(account_id, change_id);


--
-- Name: subscription_coverage_terms subscription_coverage_terms_account_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_coverage_terms
    ADD CONSTRAINT subscription_coverage_terms_account_id_fkey FOREIGN KEY (account_id) REFERENCES public.billing_accounts(id);


--
-- Name: subscription_term_facts subscription_term_facts_account_id_term_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_term_facts
    ADD CONSTRAINT subscription_term_facts_account_id_term_id_fkey FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id);


--
-- Name: subscription_term_facts subscription_term_facts_plan_version_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_term_facts
    ADD CONSTRAINT subscription_term_facts_plan_version_id_fkey FOREIGN KEY (plan_version_id) REFERENCES public.plan_versions(id);


--
-- Name: trial_extension_receipts trial_extension_receipts_admin_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extension_receipts
    ADD CONSTRAINT trial_extension_receipts_admin_id_fkey FOREIGN KEY (admin_id) REFERENCES public.admin_identities(id);


--
-- Name: trial_extension_receipts trial_extension_receipts_extension_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extension_receipts
    ADD CONSTRAINT trial_extension_receipts_extension_id_account_id_shop_id_fkey FOREIGN KEY (extension_id, account_id, shop_id) REFERENCES public.trial_extensions(extension_id, account_id, shop_id);


--
-- Name: trial_extensions trial_extensions_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_extensions
    ADD CONSTRAINT trial_extensions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.trial_history(shop_id);


--
-- Name: trial_history trial_history_account_id_term_id_starts_at_ends_at_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_history
    ADD CONSTRAINT trial_history_account_id_term_id_starts_at_ends_at_fkey FOREIGN KEY (account_id, term_id, starts_at, ends_at) REFERENCES public.subscription_coverage_terms(account_id, term_id, starts_at, ends_at);


--
-- Name: trial_history trial_history_event_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_history
    ADD CONSTRAINT trial_history_event_id_fkey FOREIGN KEY (event_id) REFERENCES public.shop_trial_inbox(event_id);


--
-- Name: trial_history trial_history_policy_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.trial_history
    ADD CONSTRAINT trial_history_policy_id_fkey FOREIGN KEY (policy_id) REFERENCES public.trial_policy_versions(id);


--
-- Name: unmatched_payments unmatched_payments_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.unmatched_payments
    ADD CONSTRAINT unmatched_payments_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id);


--
-- Name: unmatched_payments unmatched_payments_account_id_order_id_manual_receipt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.unmatched_payments
    ADD CONSTRAINT unmatched_payments_account_id_order_id_manual_receipt_id_fkey FOREIGN KEY (account_id, order_id, manual_receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id);


--
-- Name: unmatched_payments unmatched_payments_inbox_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.unmatched_payments
    ADD CONSTRAINT unmatched_payments_inbox_id_fkey FOREIGN KEY (inbox_id) REFERENCES public.gateway_inbox_events(inbox_id);


--
-- Name: verified_payments verified_payments_account_id_attempt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.verified_payments
    ADD CONSTRAINT verified_payments_account_id_attempt_id_fkey FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id);


--
-- Name: complimentary_grant_shops account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.complimentary_grant_shops FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: complimentary_grants account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.complimentary_grants FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacement_exceptions account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.coverage_replacement_exceptions FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacements account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.coverage_replacements FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_slot_assignment_deliveries account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.coverage_slot_assignment_deliveries FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_slot_assignments account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.coverage_slot_assignments FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: manual_payment_receipts account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.manual_payment_receipts FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: paid_slot_handoffs account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.paid_slot_handoffs FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_attempt_sessions account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_attempt_sessions FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_attempts account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_attempts FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_confirmations account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_confirmations FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_events account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_events FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_fulfilment_deliveries account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_fulfilment_deliveries FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_fulfilment_shops account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_fulfilment_shops FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_fulfilments account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_fulfilments FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_routing_events account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.payment_routing_events FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: refund_requests account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.refund_requests FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_delivery_uses account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.shop_affiliation_delivery_uses FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_inbox account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.shop_affiliation_inbox FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_requests account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.shop_affiliation_requests FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliations account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.shop_affiliations FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: subscription_coverage_terms account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.subscription_coverage_terms FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: subscription_term_facts account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.subscription_term_facts FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: verified_payments account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY account_read ON public.verified_payments FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacement_exception_approvals admin_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY admin_insert ON public.coverage_replacement_exception_approvals FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.exception_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.exception_session_id'::text, true), ''::text))::uuid) AND (state = 'Preparing'::text) AND (EXISTS ( SELECT
   FROM (public.admin_sessions s
     JOIN public.admin_identities a ON ((a.id = s.admin_id)))
  WHERE ((s.id = coverage_replacement_exception_approvals.session_id) AND (a.id = coverage_replacement_exception_approvals.admin_id) AND (s.revoked_at IS NULL) AND (a.deactivated_at IS NULL) AND (a.mfa_enrolled_at IS NOT NULL) AND ((a.capabilities & 4) = 4) AND (s.mfa_verified_at = coverage_replacement_exception_approvals.mfa_verified_at))))));


--
-- Name: coverage_replacement_exceptions admin_prepare_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY admin_prepare_update ON public.coverage_replacement_exceptions FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.exception_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.exception_session_id'::text, true), ''::text))::uuid IS NOT NULL))) WITH CHECK (((state = ANY (ARRAY['Preparing'::text, 'Refused'::text])) AND (EXISTS ( SELECT
   FROM public.coverage_replacement_exception_approvals a
  WHERE ((a.exception_id = coverage_replacement_exceptions.exception_id) AND (a.account_id = coverage_replacement_exceptions.account_id) AND (a.admin_id = (NULLIF(current_setting('app.exception_admin_id'::text, true), ''::text))::uuid) AND (a.session_id = (NULLIF(current_setting('app.exception_session_id'::text, true), ''::text))::uuid) AND (a.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: coverage_replacement_exception_approvals admin_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY admin_read ON public.coverage_replacement_exception_approvals FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (((NULLIF(current_setting('app.exception_admin_id'::text, true), ''::text))::uuid IS NOT NULL) OR (EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.account_id = coverage_replacement_exception_approvals.account_id) AND (i.kind = ANY (ARRAY['CoverageReplacementExceptionReceipt'::text, 'CoverageReplacementExceptionConsent'::text])) AND (i.xmin = (pg_current_xact_id())::xid)))))));


--
-- Name: shop_affiliations affiliation_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY affiliation_insert ON public.shop_affiliations FOR INSERT WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_provider_selections append_selection; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY append_selection ON public.payment_provider_selections FOR INSERT WITH CHECK ((((provenance = 'DeploymentConfiguration'::text) AND (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text) IS NOT NULL)) OR ((provenance = 'PlatformOwner'::text) AND (admin_id = (NULLIF(current_setting('app.provider_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.provider_session_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.admin_identities a
     JOIN public.admin_sessions s ON ((s.admin_id = a.id)))
  WHERE ((a.id = payment_provider_selections.admin_id) AND (s.id = payment_provider_selections.session_id) AND (a.deactivated_at IS NULL) AND (a.mfa_enrolled_at IS NOT NULL) AND ((a.capabilities & 16) = 16) AND (s.revoked_at IS NULL) AND (s.mfa_verified_at = payment_provider_selections.mfa_verified_at)))))));


--
-- Name: refund_applications application_owner; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY application_owner ON public.refund_applications TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_applications application_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY application_read ON public.refund_applications FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacements apply_renewal; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY apply_renewal ON public.coverage_replacements FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = 'Scheduled'::text))) WITH CHECK (((state = 'Applied'::text) AND (EXISTS ( SELECT
   FROM (public.payment_fulfilments f
     JOIN public.checkout_orders q USING (account_id, order_id))
  WHERE ((f.account_id = coverage_replacements.account_id) AND (f.term_id = coverage_replacements.successor_term_id) AND (f.predecessor_term_id = coverage_replacements.term_id) AND (f.xmin = (pg_current_xact_id())::xid) AND ((((q.snapshot -> 'replacement'::text) ->> 'replacementId'::text))::uuid = coverage_replacements.replacement_id))))));


--
-- Name: payment_routing_events attempt_append; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY attempt_append ON public.payment_routing_events FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)));


--
-- Name: payment_attempt_sessions attempt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY attempt_insert ON public.payment_attempt_sessions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)));


--
-- Name: payment_events attempt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY attempt_insert ON public.payment_events FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)));


--
-- Name: payment_fulfilments attempt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY attempt_insert ON public.payment_fulfilments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)));


--
-- Name: verified_payments attempt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY attempt_insert ON public.verified_payments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)));


--
-- Name: payment_attempts attempt_start; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY attempt_start ON public.payment_attempts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = payment_attempts.replay_id) AND (u.account_id = payment_attempts.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.checkout.payment.start'::text))))));


--
-- Name: billing_account_invitations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_account_invitations ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_account_members; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_account_members ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_account_members billing_account_members_insert_manager; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_account_members_insert_manager ON public.billing_account_members FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (role = 2) AND (status = 1) AND (revision = 1) AND (EXISTS ( SELECT
   FROM public.billing_account_invitations i
  WHERE ((i.revision = 2) AND (i.account_id = billing_account_members.account_id) AND (i.invited_actor_id = billing_account_members.merchant_user_id) AND (i.accepted_member_id = billing_account_members.id) AND (i.accepted_at = billing_account_members.created_at))))));


--
-- Name: billing_account_members billing_account_members_insert_owner; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_account_members_insert_owner ON public.billing_account_members FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (role = 1) AND (status = 1) AND (revision = 1)));


--
-- Name: billing_account_members billing_account_members_lock; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_account_members_lock ON public.billing_account_members FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid))) WITH CHECK (false);


--
-- Name: billing_account_members billing_account_members_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_account_members_read ON public.billing_account_members FOR SELECT USING (((merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text) IS NULL) OR (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))));


--
-- Name: billing_accounts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_accounts ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_accounts billing_accounts_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_accounts_insert ON public.billing_accounts FOR INSERT WITH CHECK (((id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (created_by_merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)));


--
-- Name: billing_accounts billing_accounts_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_accounts_read ON public.billing_accounts FOR SELECT USING (((id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members member
  WHERE ((member.account_id = billing_accounts.id) AND (member.status = 1) AND (member.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid))))));


--
-- Name: merchant_billing_delegation_uses billing_delegation_uses_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_delegation_uses_insert ON public.merchant_billing_delegation_uses FOR INSERT WITH CHECK (((NOT (account_id IS DISTINCT FROM (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)));


--
-- Name: merchant_billing_delegation_uses billing_delegation_uses_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_delegation_uses_read ON public.merchant_billing_delegation_uses FOR SELECT USING (((NOT (account_id IS DISTINCT FROM (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)));


--
-- Name: billing_document_versions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_document_versions ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_account_invitations billing_invitation_accept; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_invitation_accept ON public.billing_account_invitations FOR UPDATE USING (((revision = 1) AND (invited_actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))) WITH CHECK (((revision = 2) AND (invited_actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)));


--
-- Name: billing_invitation_audit; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_invitation_audit ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_invitation_audit billing_invitation_audit_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_invitation_audit_insert ON public.billing_invitation_audit FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_invitations i
  WHERE ((i.id = billing_invitation_audit.invitation_id) AND (i.account_id = billing_invitation_audit.account_id) AND (i.revision = billing_invitation_audit.invitation_revision) AND (((billing_invitation_audit.action = 'Issued'::text) AND (i.issuer_actor_id = billing_invitation_audit.actor_id) AND (billing_invitation_audit.occurred_at = i.created_at)) OR ((billing_invitation_audit.action = 'Accepted'::text) AND (i.invited_actor_id = billing_invitation_audit.actor_id) AND (billing_invitation_audit.occurred_at = i.accepted_at) AND (i.accepted_member_id = billing_invitation_audit.member_id))))))));


--
-- Name: billing_account_invitations billing_invitation_issue; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_invitation_issue ON public.billing_account_invitations FOR INSERT WITH CHECK (((revision = 1) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (issuer_actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = billing_account_invitations.account_id) AND (m.merchant_user_id = billing_account_invitations.issuer_actor_id) AND (m.role = 1) AND (m.status = 1) AND (m.revision = billing_account_invitations.issuer_revision))))));


--
-- Name: billing_invitation_issue_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_invitation_issue_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_account_invitations billing_invitation_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_invitation_read ON public.billing_account_invitations FOR SELECT USING ((((invited_actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text) IS NULL) OR (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))) OR ((issuer_actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))));


--
-- Name: billing_invitation_issue_receipts billing_invitation_receipt_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_invitation_receipt_scope ON public.billing_invitation_issue_receipts USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = billing_invitation_issue_receipts.account_id) AND (m.merchant_user_id = billing_invitation_issue_receipts.actor_id) AND (m.role = 1) AND (m.status = 1))))));


--
-- Name: billing_account_members billing_member_function_lock; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_member_function_lock ON public.billing_account_members FOR UPDATE TO baseline_owner_zq USING (true) WITH CHECK (false)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: billing_account_members billing_member_function_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_member_function_read ON public.billing_account_members FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: billing_member_removals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_member_removals ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_account_members billing_member_remove; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_member_remove ON public.billing_account_members FOR UPDATE TO baseline_owner_zq USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (role = 2) AND (status = 1))) WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (role = 2) AND (status = 2)))$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: coverage_transfer_consents billing_owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_owner_insert ON public.coverage_transfer_consents FOR INSERT WITH CHECK (((purpose <> 'ShopOwner'::text) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.id = coverage_transfer_consents.member_id) AND (m.account_id = coverage_transfer_consents.account_id) AND (m.merchant_user_id = coverage_transfer_consents.actor_id) AND (m.role = 1) AND (m.status = 1) AND (m.revision = coverage_transfer_consents.authority_revision)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_transfer_consents.account_id) AND (u.actor_id = coverage_transfer_consents.actor_id) AND ((u.action)::text = 'billing.coverage-transfer.consent'::text) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: billing_ownership_acceptances; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_ownership_acceptances ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_ownership_proposals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.billing_ownership_proposals ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_member_removals billing_removal_insert; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_removal_insert ON public.billing_member_removals FOR INSERT TO baseline_owner_zq WITH CHECK (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: billing_member_removals billing_removal_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_removal_read ON public.billing_member_removals FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = billing_member_removals.account_id) AND (m.merchant_user_id = billing_member_removals.actor_id) AND (m.role = 1) AND (m.status = 1))))));


--
-- Name: merchant_billing_delegation_uses billing_transfer_acceptance_proof; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_transfer_acceptance_proof ON public.merchant_billing_delegation_uses FOR SELECT TO baseline_owner_zq USING (((account_id IS NULL) AND ((action)::text = 'billing.ownership-transfer.accept'::text) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)))$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: billing_ownership_acceptances billing_transfer_acceptance_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_transfer_acceptance_read ON public.billing_ownership_acceptances FOR SELECT USING (((recipient_actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text) IS NULL) OR (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))));


--
-- Name: billing_ownership_acceptances billing_transfer_acceptance_write; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_transfer_acceptance_write ON public.billing_ownership_acceptances FOR INSERT TO baseline_owner_zq WITH CHECK (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: billing_account_members billing_transfer_member_swap; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_transfer_member_swap ON public.billing_account_members FOR UPDATE TO baseline_owner_zq USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (status = 1))) WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (status = 1)))$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: billing_ownership_proposals billing_transfer_proposal_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY billing_transfer_proposal_read ON public.billing_ownership_proposals FOR SELECT USING (((((NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid = owner_actor_id) OR ((NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid = recipient_actor_id)) AND ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text) IS NULL) OR (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))));


--
-- Name: billing_ownership_proposals billing_transfer_proposal_write; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY billing_transfer_proposal_write ON public.billing_ownership_proposals FOR INSERT TO baseline_owner_zq WITH CHECK (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: scheduled_subscription_changes cancellation_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cancellation_insert ON public.scheduled_subscription_changes FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: scheduled_subscription_changes cancellation_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cancellation_read ON public.scheduled_subscription_changes FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) OR (EXISTS ( SELECT
   FROM public.scheduled_subscription_change_shops s
  WHERE ((s.change_id = COALESCE(scheduled_subscription_changes.withdraws_change_id, scheduled_subscription_changes.change_id)) AND (s.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid))))));


--
-- Name: subscription_cancellation_receipts cancellation_receipt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cancellation_receipt_insert ON public.subscription_cancellation_receipts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)));


--
-- Name: subscription_cancellation_receipts cancellation_receipt_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cancellation_receipt_read ON public.subscription_cancellation_receipts FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: scheduled_subscription_change_shops cancellation_shop_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cancellation_shop_insert ON public.scheduled_subscription_change_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: scheduled_subscription_change_shops cancellation_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cancellation_shop_read ON public.scheduled_subscription_change_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) OR (shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)));


--
-- Name: capacity_change_quote_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.capacity_change_quote_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: capacity_change_quotes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.capacity_change_quotes ENABLE ROW LEVEL SECURITY;

--
-- Name: capacity_increases; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.capacity_increases ENABLE ROW LEVEL SECURITY;

--
-- Name: published_catalogue_offers capacity_quote_catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_quote_catalogue_read ON public.published_catalogue_offers FOR SELECT USING ((EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = (NULLIF(current_setting('app.catalogue_public_read'::text, true), ''::text))::uuid) AND (u.account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-change.quote'::text) AND (EXISTS ( SELECT
           FROM public.billing_account_members m
          WHERE ((m.account_id = u.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))))));


--
-- Name: capacity_change_quote_shops capacity_quote_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_quote_insert ON public.capacity_change_quote_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_change_quote_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = capacity_change_quote_shops.replay_id) AND (u.account_id = capacity_change_quote_shops.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-change.quote'::text))))));


--
-- Name: capacity_change_quotes capacity_quote_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_quote_insert ON public.capacity_change_quotes FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_change_quotes.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = capacity_change_quotes.replay_id) AND (u.account_id = capacity_change_quotes.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-change.quote'::text))))));


--
-- Name: capacity_change_quote_shops capacity_quote_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_quote_read ON public.capacity_change_quote_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_change_quote_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))));


--
-- Name: capacity_change_quotes capacity_quote_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_quote_read ON public.capacity_change_quotes FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_change_quotes.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))));


--
-- Name: unmatched_payments capacity_reason_requires_decision; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_reason_requires_decision ON public.unmatched_payments AS RESTRICTIVE FOR INSERT WITH CHECK (((classification <> ALL (ARRAY['ReservationExpired'::text, 'ReservationChanged'::text, 'SourceChanged'::text, 'ReservationBusy'::text])) OR ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.capacity_increases i
  WHERE ((i.account_id = unmatched_payments.account_id) AND (i.order_id = unmatched_payments.order_id) AND (i.state = 'NeedsReview'::text) AND (i.reason = unmatched_payments.classification) AND (i.xmin = (pg_current_xact_id())::xid) AND ((i.receipt_id = unmatched_payments.manual_receipt_id) OR (i.attempt_id = unmatched_payments.attempt_id)))))) OR ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = unmatched_payments.account_id) AND (s.order_id = unmatched_payments.order_id) AND (s.state = 'NeedsReview'::text) AND (s.reason = unmatched_payments.classification) AND (s.xmin = (pg_current_xact_id())::xid) AND ((s.receipt_id = unmatched_payments.manual_receipt_id) OR (s.attempt_id = unmatched_payments.attempt_id))))))));


--
-- Name: capacity_reduction_preview_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.capacity_reduction_preview_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: capacity_reduction_previews; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.capacity_reduction_previews ENABLE ROW LEVEL SECURITY;

--
-- Name: unmatched_payments capacity_review; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_review ON public.unmatched_payments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.capacity_increases i
  WHERE ((i.account_id = unmatched_payments.account_id) AND (i.order_id = unmatched_payments.order_id) AND (i.state = 'NeedsReview'::text) AND (i.reason = unmatched_payments.classification) AND (i.xmin = (pg_current_xact_id())::xid) AND ((i.receipt_id = unmatched_payments.manual_receipt_id) OR (i.attempt_id = unmatched_payments.attempt_id)))))));


--
-- Name: payment_events capacity_review_event; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_review_event ON public.payment_events FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (kind = 'PaidNeedsReview'::text) AND (EXISTS ( SELECT
   FROM public.capacity_increases i
  WHERE ((i.account_id = payment_events.account_id) AND (i.order_id = payment_events.order_id) AND (i.attempt_id = payment_events.attempt_id) AND (i.state = 'NeedsReview'::text) AND (i.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: capacity_change_quotes capacity_settlement_quote; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY capacity_settlement_quote ON public.capacity_change_quotes FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((EXISTS ( SELECT
   FROM public.payment_attempts a
  WHERE ((a.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid) AND (a.account_id = capacity_change_quotes.account_id) AND (a.order_id = capacity_change_quotes.quote_id)))) OR ((quote_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text) IS NOT NULL)) OR (EXISTS ( SELECT
   FROM public.capacity_increases i
  WHERE ((i.account_id = capacity_change_quotes.account_id) AND (i.order_id = capacity_change_quotes.quote_id) AND (i.state = 'Pending'::text) AND (EXISTS ( SELECT
           FROM public.entitlement_merchant_inbox e
          WHERE ((e.event_id = COALESCE(i.consume_event, i.reserve_event)) AND (e.account_id = i.account_id) AND (e.kind = 'CapacityReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))))))));


--
-- Name: catalogue_draft_changes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.catalogue_draft_changes ENABLE ROW LEVEL SECURITY;

--
-- Name: catalogue_draft_changes catalogue_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_insert ON public.catalogue_draft_changes FOR INSERT WITH CHECK ((actor_admin_id = public.catalogue_manager_id()));


--
-- Name: catalogue_preparations catalogue_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_insert ON public.catalogue_preparations FOR INSERT WITH CHECK ((admin_id = public.catalogue_manager_id()));


--
-- Name: catalogue_publications catalogue_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_insert ON public.catalogue_publications FOR INSERT WITH CHECK (((admin_id = public.catalogue_manager_id()) AND (EXISTS ( SELECT
   FROM public.catalogue_preparations prepared
  WHERE ((prepared.admin_id = catalogue_publications.admin_id) AND (prepared.operation_key = catalogue_publications.operation_key) AND (prepared.version_id = catalogue_publications.version_id) AND (prepared.prepared_at <= catalogue_publications.published_at))))));


--
-- Name: plan_versions catalogue_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_insert ON public.plan_versions FOR INSERT WITH CHECK (((created_by_admin_id = public.catalogue_manager_id()) AND (updated_by_admin_id = public.catalogue_manager_id()) AND (status = 1)));


--
-- Name: plans catalogue_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_insert ON public.plans FOR INSERT WITH CHECK ((created_by_admin_id = public.catalogue_manager_id()));


--
-- Name: trial_policy_versions catalogue_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_insert ON public.trial_policy_versions FOR INSERT WITH CHECK (((created_by_admin_id = public.catalogue_manager_id()) AND (updated_by_admin_id = public.catalogue_manager_id()) AND (status = 1)));


--
-- Name: published_catalogue_offers catalogue_offer_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_offer_insert ON public.published_catalogue_offers FOR INSERT WITH CHECK ((EXISTS ( SELECT
   FROM (((public.catalogue_publications publication
     JOIN public.catalogue_preparations prepared USING (admin_id, operation_key))
     JOIN public.plan_versions version ON ((version.id = publication.version_id)))
     JOIN public.plans plan ON ((plan.id = version.plan_id)))
  WHERE ((publication.admin_id = public.catalogue_manager_id()) AND (publication.version_id = published_catalogue_offers.version_id) AND (version.status = 2) AND (plan.kind = ANY (ARRAY[1, 2])) AND (version.plan_id = published_catalogue_offers.plan_id) AND (version.version_number = published_catalogue_offers.version_number) AND ((version.source = 1) = published_catalogue_offers.development_fixture) AND (((prepared.snapshot)::jsonb -> 'plan'::text) = (published_catalogue_offers.snapshot)::jsonb)))));


--
-- Name: published_catalogue_offers catalogue_offer_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_offer_read ON public.published_catalogue_offers FOR SELECT USING (((public.catalogue_manager_id() IS NOT NULL) OR (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses used
  WHERE ((used.replay_id = (NULLIF(current_setting('app.catalogue_public_read'::text, true), ''::text))::uuid) AND (used.account_id IS NULL) AND ((used.action)::text = 'billing.catalogue.read'::text) AND (used.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid))))));


--
-- Name: catalogue_preparations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.catalogue_preparations ENABLE ROW LEVEL SECURITY;

--
-- Name: catalogue_publications; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.catalogue_publications ENABLE ROW LEVEL SECURITY;

--
-- Name: catalogue_draft_changes catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_read ON public.catalogue_draft_changes FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: catalogue_preparations catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_read ON public.catalogue_preparations FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: catalogue_publications catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_read ON public.catalogue_publications FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: plan_versions catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_read ON public.plan_versions FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: plans catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_read ON public.plans FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: trial_policy_versions catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_read ON public.trial_policy_versions FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: catalogue_retirements catalogue_retirement_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_retirement_insert ON public.catalogue_retirements FOR INSERT WITH CHECK ((admin_id = public.catalogue_manager_id()));


--
-- Name: catalogue_retirements catalogue_retirement_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_retirement_read ON public.catalogue_retirements FOR SELECT USING ((public.catalogue_manager_id() IS NOT NULL));


--
-- Name: catalogue_retirements; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.catalogue_retirements ENABLE ROW LEVEL SECURITY;

--
-- Name: plan_versions catalogue_revise; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_revise ON public.plan_versions FOR UPDATE USING ((public.catalogue_manager_id() IS NOT NULL)) WITH CHECK ((updated_by_admin_id = public.catalogue_manager_id()));


--
-- Name: trial_policy_versions catalogue_revise; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_revise ON public.trial_policy_versions FOR UPDATE USING ((public.catalogue_manager_id() IS NOT NULL)) WITH CHECK ((updated_by_admin_id = public.catalogue_manager_id()));


--
-- Name: withdrawn_catalogue_offers catalogue_withdrawal_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_withdrawal_insert ON public.withdrawn_catalogue_offers FOR INSERT WITH CHECK ((EXISTS ( SELECT
   FROM public.catalogue_retirements retirement
  WHERE ((retirement.version_id = withdrawn_catalogue_offers.version_id) AND (retirement.admin_id = public.catalogue_manager_id())))));


--
-- Name: withdrawn_catalogue_offers catalogue_withdrawal_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY catalogue_withdrawal_read ON public.withdrawn_catalogue_offers FOR SELECT USING ((EXISTS ( SELECT
   FROM public.published_catalogue_offers offer
  WHERE (offer.version_id = withdrawn_catalogue_offers.version_id))));


--
-- Name: published_catalogue_offers checkout_catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_catalogue_read ON public.published_catalogue_offers FOR SELECT USING ((EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = (NULLIF(current_setting('app.catalogue_public_read'::text, true), ''::text))::uuid) AND (u.account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.checkout.create'::text) AND (EXISTS ( SELECT
           FROM public.billing_account_members m
          WHERE ((m.account_id = u.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2])))))))));


--
-- Name: checkout_order_acceptances checkout_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_insert ON public.checkout_order_acceptances FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = checkout_order_acceptances.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2]))))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = checkout_order_acceptances.replay_id) AND (u.account_id = checkout_order_acceptances.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.checkout.accept'::text))))));


--
-- Name: checkout_order_shops checkout_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_insert ON public.checkout_order_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = checkout_order_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2]))))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = checkout_order_shops.replay_id) AND (u.account_id = checkout_order_shops.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.checkout.create'::text))))));


--
-- Name: checkout_orders checkout_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_insert ON public.checkout_orders FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = checkout_orders.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2]))))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = checkout_orders.replay_id) AND (u.account_id = checkout_orders.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.checkout.create'::text))))));


--
-- Name: checkout_order_acceptances; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.checkout_order_acceptances ENABLE ROW LEVEL SECURITY;

--
-- Name: checkout_order_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.checkout_order_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: checkout_orders; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.checkout_orders ENABLE ROW LEVEL SECURITY;

--
-- Name: checkout_order_acceptances checkout_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_read ON public.checkout_order_acceptances FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = checkout_order_acceptances.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2])))))));


--
-- Name: checkout_order_shops checkout_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_read ON public.checkout_order_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = checkout_order_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2])))))));


--
-- Name: checkout_orders checkout_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY checkout_read ON public.checkout_orders FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = checkout_orders.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2])))))));


--
-- Name: checkout_trial_sources; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.checkout_trial_sources ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_proposals complete_transfer; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY complete_transfer ON public.coverage_transfer_proposals FOR UPDATE USING (((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = 'Open'::text))) WITH CHECK (((state = 'Retired'::text) AND (retired_reason = 'AllocationCommitted'::text) AND (EXISTS ( SELECT
   FROM public.coverage_transfer_bindings b
  WHERE ((b.transfer_id = coverage_transfer_proposals.transfer_id) AND (b.source_account_id = coverage_transfer_proposals.source_account_id) AND (b.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: complimentary_grant_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.complimentary_grant_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: complimentary_grant_scopes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.complimentary_grant_scopes ENABLE ROW LEVEL SECURITY;

--
-- Name: complimentary_grant_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.complimentary_grant_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: complimentary_grants; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.complimentary_grants ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_accounts confirmation_party_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY confirmation_party_read ON public.billing_accounts FOR SELECT USING (((id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((EXISTS ( SELECT
   FROM public.payment_attempts a
  WHERE ((a.account_id = billing_accounts.id) AND (a.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)))) OR (((NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid IS NOT NULL) AND (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text) IS NOT NULL) AND (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text) IS NOT NULL)))));


--
-- Name: coverage_replacement_exceptions consent_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY consent_update ON public.coverage_replacement_exceptions FOR UPDATE USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.kind = 'CoverageReplacementExceptionConsent'::text) AND (i.account_id = coverage_replacement_exceptions.account_id) AND (i.event_id = ANY (ARRAY[coverage_replacement_exceptions.outgoing_consent_event, coverage_replacement_exceptions.outgoing_revoke_event, coverage_replacement_exceptions.incoming_consent_event, coverage_replacement_exceptions.incoming_revoke_event])) AND (i.xmin = (pg_current_xact_id())::xid) AND ((convert_from(i.payload, 'UTF8'::name))::jsonb @> jsonb_build_object('exceptionId', coverage_replacement_exceptions.exception_id, 'termId', coverage_replacement_exceptions.term_id, 'proposalRevision', coverage_replacement_exceptions.proposal_revision, 'termsDigest', coverage_replacement_exceptions.terms_digest))))));


--
-- Name: coverage_replacements consent_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY consent_update ON public.coverage_replacements FOR UPDATE USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.kind = 'CoverageReplacementConsent'::text) AND (i.account_id = coverage_replacements.account_id) AND (i.event_id = ANY (ARRAY[coverage_replacements.consent_event, coverage_replacements.revoke_event])) AND (i.xmin = (pg_current_xact_id())::xid) AND ((convert_from(i.payload, 'UTF8'::name))::jsonb @> jsonb_build_object('replacementId', coverage_replacements.replacement_id, 'shopId', coverage_replacements.incoming_shop_id, 'termId', coverage_replacements.term_id, 'affiliationRequestId', coverage_replacements.incoming_affiliation_id))))));


--
-- Name: coverage_slot_assignments consent_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY consent_update ON public.coverage_slot_assignments FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (assignment_id = (NULLIF(current_setting('app.slot_assignment_id'::text, true), ''::text))::uuid))) WITH CHECK ((EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.event_id = coverage_slot_assignments.consent_event) AND (i.account_id = coverage_slot_assignments.account_id) AND (i.kind = 'CoverageSlotConsent'::text) AND (i.xmin = (pg_current_xact_id())::xid) AND ((convert_from(i.payload, 'UTF8'::name))::jsonb @> jsonb_build_object('assignmentId', coverage_slot_assignments.assignment_id, 'shopId', coverage_slot_assignments.shop_id, 'termId', coverage_slot_assignments.term_id, 'affiliationRequestId', coverage_slot_assignments.affiliation_request_id))))));


--
-- Name: coverage_transfer_bindings consumed; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY consumed ON public.coverage_transfer_bindings FOR INSERT WITH CHECK (((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.entitlement_merchant_inbox i
     CROSS JOIN LATERAL ( SELECT (convert_from(i.payload, 'UTF8'::name))::jsonb AS body) r)
  WHERE ((i.kind = 'TransferReceipt'::text) AND (i.xmin = (pg_current_xact_id())::xid) AND (i.account_id = coverage_transfer_bindings.source_account_id) AND (((r.body ->> 'transferId'::text))::uuid = coverage_transfer_bindings.transfer_id) AND (r.body @> jsonb_build_object('phase', 'TransferConsume', 'outcome', 'Consumed', 'shopId', coverage_transfer_bindings.shop_id, 'destinationAccountId', coverage_transfer_bindings.destination_account_id))))) AND (EXISTS ( SELECT
   FROM public.coverage_transfer_approvals a
  WHERE ((a.transfer_id = coverage_transfer_bindings.transfer_id) AND (a.affiliation_id = coverage_transfer_bindings.predecessor_id) AND (a.cutover_at = coverage_transfer_bindings.cutover_at) AND (a.paid_through_at = coverage_transfer_bindings.paid_through_at) AND (a.source_term_id = coverage_transfer_bindings.source_term_id))))));


--
-- Name: development_refund_controls control_owner; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY control_owner ON public.development_refund_controls TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: development_refund_controls control_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY control_scope ON public.development_refund_controls FOR SELECT USING (((current_setting('app.development_gateway'::text, true) = 'on'::text) AND (provider_account_id = current_setting('app.gateway_account'::text, true)) AND (environment = current_setting('app.gateway_environment'::text, true)) AND (configuration_version = current_setting('app.gateway_configuration'::text, true))));


--
-- Name: coverage_replacement_exception_approvals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_replacement_exception_approvals ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_replacement_exceptions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_replacement_exceptions ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_replacements; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_replacements ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_slot_assignment_deliveries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_slot_assignment_deliveries ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_slot_assignments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_slot_assignments ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_approvals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_approvals ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_bindings; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_bindings ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_consents; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_consents ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_evidence; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_evidence ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_accounts coverage_transfer_function_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY coverage_transfer_function_read ON public.billing_accounts FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: coverage_transfer_progress; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_progress ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_proposals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_proposals ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_access_decisions decision_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY decision_insert ON public.refund_access_decisions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: refund_access_decisions decision_projection; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY decision_projection ON public.refund_access_decisions FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_access_decisions decision_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY decision_read ON public.refund_access_decisions FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_slot_assignment_deliveries delivery_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY delivery_insert ON public.coverage_slot_assignment_deliveries FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.coverage_slot_assignments a
     JOIN public.entitlement_snapshots s USING (account_id, shop_id))
  WHERE ((a.assignment_id = coverage_slot_assignment_deliveries.assignment_id) AND (a.state = 'Applied'::text) AND (a.xmin = (pg_current_xact_id())::xid) AND (s.xmin = (pg_current_xact_id())::xid) AND (s.event_id = coverage_slot_assignment_deliveries.event_id) AND (s.shop_id = coverage_slot_assignment_deliveries.shop_id))))));


--
-- Name: payment_provider_bindings deployment_registration; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY deployment_registration ON public.payment_provider_bindings FOR INSERT WITH CHECK (((provenance = 'DeploymentConfiguration'::text) AND (EXISTS ( SELECT
   FROM public.payment_provider_routing
  WHERE ((payment_provider_routing.id = 1) AND (payment_provider_routing.revision = 0)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.checkout.payment.start'::text))))));


--
-- Name: development_gateway_controls; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.development_gateway_controls ENABLE ROW LEVEL SECURITY;

--
-- Name: development_gateway_payments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.development_gateway_payments ENABLE ROW LEVEL SECURITY;

--
-- Name: development_gateway_refunds; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.development_gateway_refunds ENABLE ROW LEVEL SECURITY;

--
-- Name: development_gateway_sessions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.development_gateway_sessions ENABLE ROW LEVEL SECURITY;

--
-- Name: development_refund_controls; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.development_refund_controls ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_account_heads; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_account_heads ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_merchant_inbox; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_merchant_inbox ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_messages; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_messages ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_outbox; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_outbox ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_request_uses; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_request_uses ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_account_heads entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_account_heads USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_merchant_inbox entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_merchant_inbox USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_messages entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_messages USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_outbox entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_outbox USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_request_uses entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_request_uses USING ((replay_id = (NULLIF(current_setting('app.entitlement_replay_id'::text, true), ''::text))::uuid)) WITH CHECK ((replay_id = (NULLIF(current_setting('app.entitlement_replay_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_shop_heads entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_shop_heads USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_snapshots entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_snapshots USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: offline_device_observations entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.offline_device_observations USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_closure_consent_notices entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_closure_consent_notices USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_closure_proposals entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_closure_proposals USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_closure_reopenings entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_closure_reopenings USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_closure_targets entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_closure_targets USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_closures entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_closures USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_restriction_deliveries entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_restriction_deliveries USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_restriction_release_deliveries entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_restriction_release_deliveries USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_restriction_releases entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_restriction_releases USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: operational_restrictions entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.operational_restrictions USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_shop_heads; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_shop_heads ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_snapshots; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_snapshots ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_approvals entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.coverage_transfer_approvals TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: coverage_transfer_bindings entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.coverage_transfer_bindings TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: coverage_transfer_progress entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.coverage_transfer_progress TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: entitlement_messages entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.entitlement_messages TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: entitlement_outbox entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.entitlement_outbox TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: entitlement_shop_heads entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.entitlement_shop_heads TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: gateway_inbox_events entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.gateway_inbox_events TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: gateway_inbox_work entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.gateway_inbox_work TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: pending_payment_attempts entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.pending_payment_attempts TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: shop_affiliations entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.shop_affiliations TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: coverage_slot_assignments expire_request; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY expire_request ON public.coverage_slot_assignments FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_slot_assignments.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((u.action)::text = 'billing.slot-assignment.request'::text) AND (u.xmin = (pg_current_xact_id())::xid)))))) WITH CHECK ((state = 'Expired'::text));


--
-- Name: billing_document_versions finance_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY finance_insert ON public.billing_document_versions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.admin_sessions s
     JOIN public.admin_identities a ON ((a.id = s.admin_id)))
  WHERE ((s.id = (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid) AND (a.id = (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid) AND (s.revoked_at IS NULL) AND (a.mfa_enrolled_at IS NOT NULL) AND (a.deactivated_at IS NULL) AND ((a.capabilities & 2) = 2) AND (s.mfa_verified_at = billing_document_versions.mfa_verified_at))))));


--
-- Name: refund_approvals finance_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY finance_insert ON public.refund_approvals FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid) AND (mfa_verified_at = public.live_refund_session())));


--
-- Name: billing_document_versions finance_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY finance_read ON public.billing_document_versions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.admin_sessions s
     JOIN public.admin_identities a ON ((a.id = s.admin_id)))
  WHERE ((s.id = (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid) AND (a.id = (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid) AND (s.revoked_at IS NULL) AND (a.mfa_enrolled_at IS NOT NULL) AND (a.deactivated_at IS NULL) AND ((a.capabilities & 2) = 2))))));


--
-- Name: refund_approvals finance_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY finance_read ON public.refund_approvals FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (public.live_refund_session() IS NOT NULL)));


--
-- Name: coverage_transfer_evidence freeze_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY freeze_insert ON public.coverage_transfer_evidence FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.coverage_transfer_proposals p
  WHERE ((p.transfer_id = coverage_transfer_evidence.transfer_id) AND (p.xmin = (pg_current_xact_id())::xid) AND (coverage_transfer_evidence.account_id =
        CASE coverage_transfer_evidence.side
            WHEN 'Source'::text THEN p.source_account_id
            ELSE p.destination_account_id
        END))))));


--
-- Name: payment_fulfilment_deliveries fulfilment_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY fulfilment_insert ON public.payment_fulfilment_deliveries FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = payment_fulfilment_deliveries.account_id) AND (f.order_id = payment_fulfilment_deliveries.order_id) AND (f.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: payment_fulfilment_shops fulfilment_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY fulfilment_insert ON public.payment_fulfilment_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = payment_fulfilment_shops.account_id) AND (f.order_id = payment_fulfilment_shops.order_id) AND (f.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: gateway_inbox_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gateway_inbox_events ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_attempt_sessions gateway_inbox_lookup; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY gateway_inbox_lookup ON public.payment_attempt_sessions FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: payment_attempts gateway_inbox_lookup; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY gateway_inbox_lookup ON public.payment_attempts FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: gateway_inbox_work; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.gateway_inbox_work ENABLE ROW LEVEL SECURITY;

--
-- Name: complimentary_grants grant_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY grant_insert ON public.complimentary_grants FOR INSERT WITH CHECK (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.complimentary_grant_scopes s
  WHERE ((s.account_id = complimentary_grants.account_id) AND (s.revision = complimentary_grants.granted_version))))));


--
-- Name: complimentary_grant_scopes grant_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY grant_scope ON public.complimentary_grant_scopes USING (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid))) WITH CHECK (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)));


--
-- Name: subscription_coverage_terms grant_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY grant_shop_read ON public.subscription_coverage_terms FOR SELECT USING ((EXISTS ( SELECT
   FROM (public.complimentary_grant_shops l
     JOIN public.shop_affiliations a ON (((a.shop_id = l.shop_id) AND (a.account_id = l.account_id))))
  WHERE ((l.term_id = subscription_coverage_terms.term_id) AND (l.account_id = subscription_coverage_terms.account_id) AND (l.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: subscription_coverage_terms grant_term_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY grant_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.complimentary_grants g
  WHERE ((g.account_id = subscription_coverage_terms.account_id) AND (g.term_id = subscription_coverage_terms.term_id) AND (g.starts_at = subscription_coverage_terms.starts_at) AND (g.ends_at = subscription_coverage_terms.ends_at) AND (g.coverage_mode = subscription_coverage_terms.mode) AND (g.shop_capacity = subscription_coverage_terms.capacity))))));


--
-- Name: shop_affiliation_inbox inbox_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY inbox_insert ON public.shop_affiliation_inbox FOR INSERT WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: capacity_increases increase_ack; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY increase_ack ON public.capacity_increases FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox e
  WHERE ((e.kind = 'CapacityReceipt'::text) AND (e.account_id = capacity_increases.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[capacity_increases.reserve_event, capacity_increases.consume_event])))))));


--
-- Name: capacity_increases increase_fund; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY increase_fund ON public.capacity_increases FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = 'Pending'::text) AND (consume_event IS NULL) AND ((attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid) OR (receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid))));


--
-- Name: capacity_increases increase_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY increase_read ON public.capacity_increases FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) OR ((state = 'Applied'::text) AND (EXISTS ( SELECT
   FROM public.shop_affiliations a
  WHERE ((a.account_id = capacity_increases.account_id) AND (a.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))))));


--
-- Name: complimentary_grant_shops link_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY link_insert ON public.complimentary_grant_shops FOR INSERT WITH CHECK (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)));


--
-- Name: coverage_transfer_approvals manager_decision; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manager_decision ON public.coverage_transfer_approvals FOR INSERT WITH CHECK (((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.exception_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.exception_session_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.admin_sessions s
     JOIN public.admin_identities a ON ((a.id = s.admin_id)))
  WHERE ((s.id = coverage_transfer_approvals.session_id) AND (a.id = coverage_transfer_approvals.admin_id) AND (s.revoked_at IS NULL) AND (a.deactivated_at IS NULL) AND (a.mfa_enrolled_at IS NOT NULL) AND ((a.capabilities & 4) = 4) AND (s.mfa_verified_at = coverage_transfer_approvals.mfa_verified_at)))) AND (EXISTS ( SELECT
   FROM public.coverage_transfer_proposals p
  WHERE ((p.transfer_id = coverage_transfer_approvals.transfer_id) AND (p.source_account_id = coverage_transfer_approvals.source_account_id) AND (p.destination_account_id = coverage_transfer_approvals.destination_account_id) AND (p.source_term_id = coverage_transfer_approvals.source_term_id) AND (p.shop_id = coverage_transfer_approvals.shop_id) AND (p.source_affiliation_id = coverage_transfer_approvals.affiliation_id) AND (p.source_paid_through_at = coverage_transfer_approvals.paid_through_at) AND (COALESCE(p.cutover_at, p.source_paid_through_at) = coverage_transfer_approvals.cutover_at) AND (p.state = 'Open'::text) AND (p.expires_at > coverage_transfer_approvals.decided_at))))));


--
-- Name: manual_payment_receipts manual_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_insert ON public.manual_payment_receipts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL) AND (admin_id = (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid) AND (receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.admin_sessions s
     JOIN public.admin_identities a ON ((a.id = s.admin_id)))
  WHERE ((s.id = manual_payment_receipts.session_id) AND (a.id = manual_payment_receipts.admin_id) AND (s.revoked_at IS NULL) AND (s.mfa_verified_at = manual_payment_receipts.mfa_verified_at) AND (a.mfa_enrolled_at IS NOT NULL) AND (a.deactivated_at IS NULL) AND ((a.capabilities & 2) = 2))))));


--
-- Name: payment_fulfilment_deliveries manual_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_insert ON public.payment_fulfilment_deliveries FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = payment_fulfilment_deliveries.account_id) AND (f.order_id = payment_fulfilment_deliveries.order_id) AND (f.order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (f.manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid))))));


--
-- Name: payment_fulfilment_shops manual_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_insert ON public.payment_fulfilment_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = payment_fulfilment_shops.account_id) AND (f.order_id = payment_fulfilment_shops.order_id) AND (f.order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (f.manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid))))));


--
-- Name: payment_fulfilments manual_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_insert ON public.payment_fulfilments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL) AND (manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid) AND (attempt_id IS NULL)));


--
-- Name: subscription_coverage_terms manual_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = subscription_coverage_terms.account_id) AND (f.term_id = subscription_coverage_terms.term_id) AND (f.order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (f.manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid))))));


--
-- Name: subscription_term_facts manual_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_insert ON public.subscription_term_facts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (basis = 'Paid'::text) AND ((source_reference)::text = 'payment-fulfilment/1'::text) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = subscription_term_facts.account_id) AND (f.term_id = subscription_term_facts.term_id) AND (f.order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (f.manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid))))));


--
-- Name: checkout_order_acceptances manual_order_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_order_read ON public.checkout_order_acceptances FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: checkout_order_shops manual_order_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_order_read ON public.checkout_order_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: checkout_orders manual_order_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY manual_order_read ON public.checkout_orders FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: manual_payment_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.manual_payment_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: billing_document_versions member_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY member_read ON public.billing_document_versions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = billing_document_versions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2]))))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = billing_document_versions.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((u.action)::text = ANY (ARRAY[('billing.document.list'::character varying)::text, ('billing.document.download'::character varying)::text])) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: merchant_billing_delegation_uses; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.merchant_billing_delegation_uses ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_device_observations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.offline_device_observations ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_closure_consent_notices; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_closure_consent_notices ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_closure_proposals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_closure_proposals ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_closure_reopenings; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_closure_reopenings ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_closure_targets; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_closure_targets ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_closures; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_closures ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_restriction_deliveries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_restriction_deliveries ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_restriction_release_deliveries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_restriction_release_deliveries ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_restriction_releases; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_restriction_releases ENABLE ROW LEVEL SECURITY;

--
-- Name: operational_restrictions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operational_restrictions ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_evidence own_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY own_read ON public.coverage_transfer_evidence FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliations owner_applied_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_applied_read ON public.shop_affiliations FOR SELECT USING (((shop_id = (NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid) AND (request_id = (NULLIF(current_setting('app.affiliation_request_id'::text, true), ''::text))::uuid)));


--
-- Name: shop_affiliation_inbox owner_inbox_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_inbox_read ON public.shop_affiliation_inbox FOR SELECT USING (((shop_id = (NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid) AND (request_id = (NULLIF(current_setting('app.affiliation_request_id'::text, true), ''::text))::uuid)));


--
-- Name: coverage_replacement_exceptions owner_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_read ON public.coverage_replacement_exceptions FOR SELECT USING (((exception_id = (NULLIF(current_setting('app.coverage_exception_id'::text, true), ''::text))::uuid) AND (((NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid = outgoing_shop_id) OR ((NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid = incoming_shop_id))));


--
-- Name: coverage_replacements owner_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_read ON public.coverage_replacements FOR SELECT USING (((incoming_shop_id = (NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid) AND (replacement_id = (NULLIF(current_setting('app.replacement_id'::text, true), ''::text))::uuid)));


--
-- Name: coverage_slot_assignments owner_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_read ON public.coverage_slot_assignments FOR SELECT USING (((assignment_id = (NULLIF(current_setting('app.slot_assignment_id'::text, true), ''::text))::uuid) AND (shop_id = (NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid)));


--
-- Name: paid_slot_handoffs owner_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_read ON public.paid_slot_handoffs FOR SELECT USING (((exception_id = (NULLIF(current_setting('app.coverage_exception_id'::text, true), ''::text))::uuid) AND (((NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid = outgoing_shop_id) OR ((NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid = incoming_shop_id))));


--
-- Name: payment_provider_bindings owner_registration; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_registration ON public.payment_provider_bindings FOR INSERT WITH CHECK (((provenance = 'PlatformOwner'::text) AND (EXISTS ( SELECT
   FROM (public.admin_identities a
     JOIN public.admin_sessions s ON ((s.admin_id = a.id)))
  WHERE ((a.id = (NULLIF(current_setting('app.provider_admin_id'::text, true), ''::text))::uuid) AND (s.id = (NULLIF(current_setting('app.provider_session_id'::text, true), ''::text))::uuid) AND (a.deactivated_at IS NULL) AND (a.mfa_enrolled_at IS NOT NULL) AND ((a.capabilities & 16) = 16) AND (s.revoked_at IS NULL) AND (payment_provider_bindings.registered_at >= s.mfa_verified_at) AND (payment_provider_bindings.registered_at < (s.mfa_verified_at + '00:05:00'::interval)))))));


--
-- Name: coverage_replacement_exceptions owner_request; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_request ON public.coverage_replacement_exceptions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (requester_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (state = 'AwaitingConsents'::text) AND (outgoing_consent_event IS NULL) AND (incoming_consent_event IS NULL) AND (outgoing_revoke_event IS NULL) AND (incoming_revoke_event IS NULL) AND (reason IS NULL) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_replacement_exceptions.account_id) AND (u.actor_id = coverage_replacement_exceptions.requester_id) AND ((u.action)::text = 'billing.coverage-replacement-exception.request'::text) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: shop_affiliation_requests owner_request_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_request_read ON public.shop_affiliation_requests FOR SELECT USING (((shop_id = (NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid) AND (request_id = (NULLIF(current_setting('app.affiliation_request_id'::text, true), ''::text))::uuid)));


--
-- Name: coverage_replacements owner_schedule; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_schedule ON public.coverage_replacements FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (requester_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (state = 'AwaitingConsent'::text) AND (consent_event IS NULL) AND (revoke_event IS NULL) AND (reason IS NULL) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_replacements.account_id) AND (u.actor_id = coverage_replacements.requester_id) AND ((u.action)::text = 'billing.coverage-replacement.schedule'::text) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: subscription_coverage_terms owner_term_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_term_read ON public.subscription_coverage_terms FOR SELECT USING ((EXISTS ( SELECT
   FROM public.shop_affiliation_requests r
  WHERE ((r.term_id = subscription_coverage_terms.term_id) AND (r.request_id = (NULLIF(current_setting('app.affiliation_request_id'::text, true), ''::text))::uuid) AND (r.shop_id = (NULLIF(current_setting('app.affiliation_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: subscription_term_facts paid_facts_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY paid_facts_insert ON public.subscription_term_facts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (basis = 'Paid'::text) AND ((source_reference)::text = 'payment-fulfilment/1'::text) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = subscription_term_facts.account_id) AND (f.term_id = subscription_term_facts.term_id) AND (f.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: paid_fresh_starts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.paid_fresh_starts ENABLE ROW LEVEL SECURITY;

--
-- Name: subscription_coverage_terms paid_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY paid_shop_read ON public.subscription_coverage_terms FOR SELECT USING ((EXISTS ( SELECT
   FROM public.payment_fulfilment_shops l
  WHERE ((l.term_id = subscription_coverage_terms.term_id) AND (l.account_id = subscription_coverage_terms.account_id) AND (l.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: subscription_term_facts paid_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY paid_shop_read ON public.subscription_term_facts FOR SELECT USING ((EXISTS ( SELECT
   FROM public.payment_fulfilment_shops l
  WHERE ((l.term_id = subscription_term_facts.term_id) AND (l.account_id = subscription_term_facts.account_id) AND (l.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: paid_slot_handoffs; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.paid_slot_handoffs ENABLE ROW LEVEL SECURITY;

--
-- Name: subscription_coverage_terms paid_term_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY paid_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = subscription_coverage_terms.account_id) AND (f.term_id = subscription_coverage_terms.term_id) AND (f.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: coverage_transfer_consents party_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY party_read ON public.coverage_transfer_consents FOR SELECT USING ((((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid = source_account_id) OR ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid = destination_account_id) OR (transfer_id = (NULLIF(current_setting('app.coverage_transfer_id'::text, true), ''::text))::uuid)));


--
-- Name: coverage_transfer_proposals party_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY party_read ON public.coverage_transfer_proposals FOR SELECT USING ((((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid = source_account_id) OR ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid = destination_account_id) OR (transfer_id = (NULLIF(current_setting('app.coverage_transfer_id'::text, true), ''::text))::uuid)));


--
-- Name: checkout_order_acceptances payment_attempt_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY payment_attempt_read ON public.checkout_order_acceptances FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_attempts a
  WHERE ((a.account_id = checkout_order_acceptances.account_id) AND (a.order_id = checkout_order_acceptances.order_id) AND (a.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: checkout_order_shops payment_attempt_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY payment_attempt_read ON public.checkout_order_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_attempts a
  WHERE ((a.account_id = checkout_order_shops.account_id) AND (a.order_id = checkout_order_shops.order_id) AND (a.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: checkout_orders payment_attempt_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY payment_attempt_read ON public.checkout_orders FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.payment_attempts a
  WHERE ((a.account_id = checkout_orders.account_id) AND (a.order_id = checkout_orders.order_id) AND (a.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))))));


--
-- Name: payment_attempt_sessions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_attempt_sessions ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_attempts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_attempts ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_confirmations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_confirmations ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_events ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_fulfilment_deliveries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_fulfilment_deliveries ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_fulfilment_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_fulfilment_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_fulfilments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_fulfilments ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_provider_bindings; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_provider_bindings ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_provider_routing; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_provider_routing ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_provider_selections; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_provider_selections ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_restoration_holds; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_restoration_holds ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_review_decisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_review_decisions ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_routing_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_routing_events ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_trial_conversions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_trial_conversions ENABLE ROW LEVEL SECURITY;

--
-- Name: pending_payment_attempts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.pending_payment_attempts ENABLE ROW LEVEL SECURITY;

--
-- Name: development_gateway_controls pinned_identity; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY pinned_identity ON public.development_gateway_controls AS RESTRICTIVE USING ((EXISTS ( SELECT
   FROM public.development_gateway_sessions s
  WHERE ((s.provider_order_reference)::text = (development_gateway_controls.provider_order_reference)::text)))) WITH CHECK ((EXISTS ( SELECT
   FROM public.development_gateway_sessions s
  WHERE ((s.provider_order_reference)::text = (development_gateway_controls.provider_order_reference)::text))));


--
-- Name: development_gateway_payments pinned_identity; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY pinned_identity ON public.development_gateway_payments AS RESTRICTIVE USING ((EXISTS ( SELECT
   FROM public.development_gateway_sessions s
  WHERE ((s.provider_order_reference)::text = (development_gateway_payments.provider_order_reference)::text)))) WITH CHECK ((EXISTS ( SELECT
   FROM public.development_gateway_sessions s
  WHERE ((s.provider_order_reference)::text = (development_gateway_payments.provider_order_reference)::text))));


--
-- Name: development_gateway_sessions pinned_identity; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY pinned_identity ON public.development_gateway_sessions AS RESTRICTIVE USING ((((provider_account_id)::text = current_setting('app.gateway_account'::text, true)) AND ((environment)::text = current_setting('app.gateway_environment'::text, true)) AND ((configuration_version)::text = current_setting('app.gateway_configuration'::text, true)))) WITH CHECK ((((provider_account_id)::text = current_setting('app.gateway_account'::text, true)) AND ((environment)::text = current_setting('app.gateway_environment'::text, true)) AND ((configuration_version)::text = current_setting('app.gateway_configuration'::text, true))));


--
-- Name: plan_versions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.plan_versions ENABLE ROW LEVEL SECURITY;

--
-- Name: plans; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.plans ENABLE ROW LEVEL SECURITY;

--
-- Name: trial_extensions provider_extension_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY provider_extension_insert ON public.trial_extensions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.trial_extension_admin_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: trial_extension_receipts provider_extension_receipt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY provider_extension_receipt_insert ON public.trial_extension_receipts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.trial_extension_admin_id'::text, true), ''::text))::uuid)));


--
-- Name: trial_extension_receipts provider_extension_receipt_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY provider_extension_receipt_read ON public.trial_extension_receipts FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.trial_extension_admin_id'::text, true), ''::text))::uuid)));


--
-- Name: complimentary_grant_receipts provider_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY provider_read ON public.complimentary_grant_receipts FOR SELECT USING (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)));


--
-- Name: published_catalogue_offers; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.published_catalogue_offers ENABLE ROW LEVEL SECURITY;

--
-- Name: complimentary_grant_receipts receipt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY receipt_insert ON public.complimentary_grant_receipts FOR INSERT WITH CHECK (((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text) IS NOT NULL) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.complimentary_grants g
  WHERE ((g.grant_id = complimentary_grant_receipts.grant_id) AND (g.account_id = complimentary_grant_receipts.account_id) AND (g.granted_version = (complimentary_grant_receipts.expected_version + 1)))))));


--
-- Name: paid_slot_handoffs receipt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY receipt_insert ON public.paid_slot_handoffs FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.coverage_replacement_exception_approvals a
     JOIN public.entitlement_merchant_inbox i ON (((i.account_id = a.account_id) AND (i.event_id = a.consume_receipt_event) AND (i.kind = 'CoverageReplacementExceptionReceipt'::text))))
  WHERE ((a.approval_id = paid_slot_handoffs.approval_id) AND (a.exception_id = paid_slot_handoffs.exception_id) AND (a.account_id = paid_slot_handoffs.account_id) AND (a.state = 'Applied'::text) AND (a.xmin = (pg_current_xact_id())::xid) AND (i.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: payment_confirmations receipt_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY receipt_insert ON public.payment_confirmations FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (((facts -> 'receipt'::text) ->> 'issuanceBasis'::text) = 'Verification'::text) AND (((attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid) AND (manual_receipt_id IS NULL)) OR ((manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND (attempt_id IS NULL)))));


--
-- Name: coverage_transfer_progress receipt_progress; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY receipt_progress ON public.coverage_transfer_progress FOR INSERT WITH CHECK (((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((EXISTS ( SELECT
   FROM (public.entitlement_merchant_inbox i
     CROSS JOIN LATERAL ( SELECT (convert_from(i.payload, 'UTF8'::name))::jsonb AS body) r)
  WHERE ((i.kind = 'TransferReceipt'::text) AND (i.xmin = (pg_current_xact_id())::xid) AND (i.account_id = coverage_transfer_progress.source_account_id) AND (((r.body ->> 'transferId'::text))::uuid = coverage_transfer_progress.transfer_id) AND (i.event_id = coverage_transfer_progress.event_id) AND (((coverage_transfer_progress.state = 'Scheduled'::text) AND ((r.body ->> 'outcome'::text) = 'Prepared'::text)) OR ((coverage_transfer_progress.state = ANY (ARRAY['PendingActivation'::text, 'NeedsReview'::text])) AND ((r.body ->> 'outcome'::text) = 'Consumed'::text)) OR ((coverage_transfer_progress.state = 'NeedsReview'::text) AND ((r.body ->> 'outcome'::text) = 'Refused'::text)))))) OR (EXISTS ( SELECT
   FROM public.entitlement_messages m
  WHERE ((m.xmin = (pg_current_xact_id())::xid) AND (m.event_id = coverage_transfer_progress.event_id) AND (m.account_id = coverage_transfer_progress.source_account_id) AND ((((convert_from(m.payload, 'UTF8'::name))::jsonb ->> 'transferId'::text))::uuid = coverage_transfer_progress.transfer_id) AND (((m.kind = 'TransferConsume'::text) AND (coverage_transfer_progress.state = 'Consuming'::text)) OR ((m.kind = 'TransferAbort'::text) AND (coverage_transfer_progress.state = 'NeedsReview'::text)))))))));


--
-- Name: coverage_replacement_exception_approvals receipt_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY receipt_update ON public.coverage_replacement_exception_approvals FOR UPDATE USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.account_id = coverage_replacement_exception_approvals.account_id) AND (i.kind = ANY (ARRAY['CoverageReplacementExceptionReceipt'::text, 'CoverageReplacementExceptionConsent'::text])) AND (i.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: coverage_replacement_exceptions receipt_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY receipt_update ON public.coverage_replacement_exceptions FOR UPDATE USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid)) WITH CHECK ((EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.kind = 'CoverageReplacementExceptionReceipt'::text) AND (i.account_id = coverage_replacement_exceptions.account_id) AND (i.xmin = (pg_current_xact_id())::xid) AND ((((convert_from(i.payload, 'UTF8'::name))::jsonb ->> 'exceptionId'::text))::uuid = coverage_replacement_exceptions.exception_id)))));


--
-- Name: published_catalogue_offers reduction_catalogue_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_catalogue_read ON public.published_catalogue_offers FOR SELECT USING ((EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = (NULLIF(current_setting('app.catalogue_public_read'::text, true), ''::text))::uuid) AND (u.account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.preview'::text) AND (EXISTS ( SELECT
           FROM public.billing_account_members m
          WHERE ((m.account_id = u.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))))));


--
-- Name: scheduled_capacity_reductions reduction_due_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY reduction_due_worker ON public.scheduled_capacity_reductions FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: payment_fulfilment_deliveries reduction_inbox_deliveries_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_deliveries_insert ON public.payment_fulfilment_deliveries FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.scheduled_capacity_reductions s
     JOIN public.entitlement_merchant_inbox e ON (((e.account_id = s.account_id) AND (e.event_id = s.consume_event) AND (e.kind = 'ReductionReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))
  WHERE ((s.account_id = payment_fulfilment_deliveries.account_id) AND (s.state = 'Applied'::text) AND (s.xmin = (pg_current_xact_id())::xid) AND (s.order_id = payment_fulfilment_deliveries.order_id))))));


--
-- Name: payment_events reduction_inbox_event_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_event_insert ON public.payment_events FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (((kind = 'OrderFulfilled'::text) AND (EXISTS ( SELECT
   FROM (public.scheduled_capacity_reductions s
     JOIN public.entitlement_merchant_inbox e ON (((e.account_id = s.account_id) AND (e.event_id = s.consume_event) AND (e.kind = 'ReductionReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))
  WHERE ((s.account_id = payment_events.account_id) AND (s.order_id = payment_events.order_id) AND (s.attempt_id = payment_events.attempt_id) AND (s.state = 'Applied'::text) AND (s.xmin = (pg_current_xact_id())::xid))))) OR ((kind = 'PaidNeedsReview'::text) AND (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = payment_events.account_id) AND (s.order_id = payment_events.order_id) AND (s.attempt_id = payment_events.attempt_id) AND (s.state = 'NeedsReview'::text) AND (s.xmin = (pg_current_xact_id())::xid))))))));


--
-- Name: subscription_term_facts reduction_inbox_facts_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_facts_insert ON public.subscription_term_facts FOR INSERT WITH CHECK (((basis = 'Paid'::text) AND ((source_reference)::text = 'payment-fulfilment/1'::text) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.scheduled_capacity_reductions s
     JOIN public.entitlement_merchant_inbox e ON (((e.account_id = s.account_id) AND (e.event_id = s.consume_event) AND (e.kind = 'ReductionReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))
  WHERE ((s.account_id = subscription_term_facts.account_id) AND (s.state = 'Applied'::text) AND (s.xmin = (pg_current_xact_id())::xid) AND (s.successor_term_id = subscription_term_facts.term_id))))));


--
-- Name: payment_fulfilments reduction_inbox_fulfilment_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_fulfilment_insert ON public.payment_fulfilments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.scheduled_capacity_reductions s
     JOIN public.entitlement_merchant_inbox e ON (((e.account_id = s.account_id) AND (e.event_id = s.consume_event) AND (e.kind = 'ReductionReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))
  WHERE ((s.account_id = payment_fulfilments.account_id) AND (s.state = 'Applied'::text) AND (s.xmin = (pg_current_xact_id())::xid) AND (s.order_id = payment_fulfilments.order_id) AND (s.successor_term_id = payment_fulfilments.term_id))))));


--
-- Name: checkout_order_acceptances reduction_inbox_order_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_order_read ON public.checkout_order_acceptances FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = checkout_order_acceptances.account_id) AND (s.order_id = checkout_order_acceptances.order_id) AND ((EXISTS ( SELECT
           FROM public.entitlement_merchant_inbox e
          WHERE ((e.kind = 'ReductionReceipt'::text) AND (e.account_id = s.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[s.reserve_event, s.consume_event]))))) OR (current_setting('app.reduction_due_scan'::text, true) = '1'::text)))))));


--
-- Name: checkout_order_shops reduction_inbox_order_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_order_read ON public.checkout_order_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = checkout_order_shops.account_id) AND (s.order_id = checkout_order_shops.order_id) AND ((EXISTS ( SELECT
           FROM public.entitlement_merchant_inbox e
          WHERE ((e.kind = 'ReductionReceipt'::text) AND (e.account_id = s.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[s.reserve_event, s.consume_event]))))) OR (current_setting('app.reduction_due_scan'::text, true) = '1'::text)))))));


--
-- Name: checkout_orders reduction_inbox_order_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_order_read ON public.checkout_orders FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = checkout_orders.account_id) AND (s.order_id = checkout_orders.order_id) AND ((EXISTS ( SELECT
           FROM public.entitlement_merchant_inbox e
          WHERE ((e.kind = 'ReductionReceipt'::text) AND (e.account_id = s.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[s.reserve_event, s.consume_event]))))) OR (current_setting('app.reduction_due_scan'::text, true) = '1'::text)))))));


--
-- Name: payment_fulfilment_shops reduction_inbox_shops_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_shops_insert ON public.payment_fulfilment_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.scheduled_capacity_reductions s
     JOIN public.entitlement_merchant_inbox e ON (((e.account_id = s.account_id) AND (e.event_id = s.consume_event) AND (e.kind = 'ReductionReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))
  WHERE ((s.account_id = payment_fulfilment_shops.account_id) AND (s.state = 'Applied'::text) AND (s.xmin = (pg_current_xact_id())::xid) AND (s.order_id = payment_fulfilment_shops.order_id) AND (s.successor_term_id = payment_fulfilment_shops.term_id))))));


--
-- Name: subscription_coverage_terms reduction_inbox_term_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_inbox_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.scheduled_capacity_reductions s
     JOIN public.entitlement_merchant_inbox e ON (((e.account_id = s.account_id) AND (e.event_id = s.consume_event) AND (e.kind = 'ReductionReceipt'::text) AND (e.xmin = (pg_current_xact_id())::xid))))
  WHERE ((s.account_id = subscription_coverage_terms.account_id) AND (s.state = 'Applied'::text) AND (s.xmin = (pg_current_xact_id())::xid) AND (s.successor_term_id = subscription_coverage_terms.term_id))))));


--
-- Name: scheduled_capacity_reductions reduction_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_insert ON public.scheduled_capacity_reductions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = 'Scheduled'::text) AND (order_id IS NULL) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reductions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = scheduled_capacity_reductions.replay_id) AND (u.account_id = scheduled_capacity_reductions.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.schedule'::text))))));


--
-- Name: capacity_reduction_preview_shops reduction_preview_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_preview_insert ON public.capacity_reduction_preview_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_reduction_preview_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = capacity_reduction_preview_shops.replay_id) AND (u.account_id = capacity_reduction_preview_shops.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.preview'::text))))));


--
-- Name: capacity_reduction_previews reduction_preview_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_preview_insert ON public.capacity_reduction_previews FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_reduction_previews.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = capacity_reduction_previews.replay_id) AND (u.account_id = capacity_reduction_previews.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.preview'::text))))));


--
-- Name: capacity_reduction_preview_shops reduction_preview_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_preview_read ON public.capacity_reduction_preview_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_reduction_preview_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))));


--
-- Name: capacity_reduction_previews reduction_preview_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_preview_read ON public.capacity_reduction_previews FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = capacity_reduction_previews.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))));


--
-- Name: scheduled_capacity_reductions reduction_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_read ON public.scheduled_capacity_reductions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reductions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) OR (current_setting('app.reduction_due_scan'::text, true) = '1'::text) OR ((NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid IS NOT NULL) OR (((NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid IS NOT NULL) AND (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text) IS NOT NULL)) OR (EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox e
  WHERE ((e.kind = 'ReductionReceipt'::text) AND (e.account_id = scheduled_capacity_reductions.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[scheduled_capacity_reductions.reserve_event, scheduled_capacity_reductions.consume_event]))))) OR (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reductions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2]))))))));


--
-- Name: scheduled_capacity_reduction_resolutions reduction_resolution_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_resolution_insert ON public.scheduled_capacity_reduction_resolutions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reduction_resolutions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = scheduled_capacity_reduction_resolutions.replay_id) AND (u.account_id = scheduled_capacity_reduction_resolutions.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.resolve'::text))))));


--
-- Name: scheduled_capacity_reduction_resolutions reduction_resolution_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_resolution_read ON public.scheduled_capacity_reduction_resolutions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reduction_resolutions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1))))));


--
-- Name: unmatched_payments reduction_review_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_review_insert ON public.unmatched_payments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = unmatched_payments.account_id) AND (s.order_id = unmatched_payments.order_id) AND (s.state = 'NeedsReview'::text) AND (s.reason = unmatched_payments.classification) AND (s.xmin = (pg_current_xact_id())::xid) AND ((s.receipt_id = unmatched_payments.manual_receipt_id) OR (s.attempt_id = unmatched_payments.attempt_id)))))));


--
-- Name: unmatched_payments reduction_settlement_case_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY reduction_settlement_case_read ON public.unmatched_payments FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: payment_review_decisions reduction_settlement_decision_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY reduction_settlement_decision_read ON public.payment_review_decisions FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: scheduled_capacity_reduction_shops reduction_shop_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_shop_insert ON public.scheduled_capacity_reduction_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reduction_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = scheduled_capacity_reduction_shops.replay_id) AND (u.account_id = scheduled_capacity_reduction_shops.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.schedule'::text))))));


--
-- Name: scheduled_capacity_reduction_shops reduction_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_shop_read ON public.scheduled_capacity_reduction_shops FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reduction_shops.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) OR (current_setting('app.reduction_due_scan'::text, true) = '1'::text) OR ((NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid IS NOT NULL) OR (((NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid IS NOT NULL) AND (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text) IS NOT NULL)) OR (EXISTS ( SELECT
   FROM public.scheduled_capacity_reductions s
  WHERE ((s.account_id = scheduled_capacity_reduction_shops.account_id) AND (s.schedule_id = scheduled_capacity_reduction_shops.schedule_id) AND (EXISTS ( SELECT
           FROM public.entitlement_merchant_inbox e
          WHERE ((e.kind = 'ReductionReceipt'::text) AND (e.account_id = s.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[s.reserve_event, s.consume_event])))))))))));


--
-- Name: scheduled_capacity_reductions reduction_update; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_update ON public.scheduled_capacity_reductions FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((((NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((attempt_id IS NULL) OR (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))) OR (((NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((receipt_id IS NULL) OR (receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid))) OR ((current_setting('app.reduction_due_scan'::text, true) = '1'::text) AND (state = 'Funded'::text)) OR (EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox e
  WHERE ((e.kind = 'ReductionReceipt'::text) AND (e.account_id = scheduled_capacity_reductions.account_id) AND (e.xmin = (pg_current_xact_id())::xid) AND (e.event_id = ANY (ARRAY[scheduled_capacity_reductions.reserve_event, scheduled_capacity_reductions.consume_event]))))) OR ((EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = scheduled_capacity_reductions.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = 1)))) AND (state = 'NeedsResolution'::text) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = scheduled_capacity_reductions.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.capacity-reduction.resolve'::text)))))))) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: refund_access_decisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_access_decisions ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_applications; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_applications ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_approvals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_approvals ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_attempts refund_attempt_finance; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY refund_attempt_finance ON public.refund_attempts FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (public.live_refund_session() IS NOT NULL)));


--
-- Name: refund_attempts refund_attempt_owner; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY refund_attempt_owner ON public.refund_attempts TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_attempts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_attempts ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_bindings; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_bindings ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_bindings refund_internal; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY refund_internal ON public.refund_bindings TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_observations refund_internal; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY refund_internal ON public.refund_observations TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_sends refund_internal; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY refund_internal ON public.refund_sends TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_work refund_internal; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY refund_internal ON public.refund_work TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_observations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_observations ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_approvals refund_projection; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY refund_projection ON public.refund_approvals FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: refund_requests refund_request_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY refund_request_insert ON public.refund_requests FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.account_id = refund_requests.account_id) AND (m.merchant_user_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (m.status = 1) AND (m.role = ANY (ARRAY[1, 2]))))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = refund_requests.replay_id) AND (u.account_id = refund_requests.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid) AND ((u.action)::text = 'billing.refund-request.create'::text))))));


--
-- Name: refund_requests; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_requests ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_sends; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_sends ENABLE ROW LEVEL SECURITY;

--
-- Name: refund_work; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.refund_work ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_slot_assignments request_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY request_insert ON public.coverage_slot_assignments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (requester_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (state = 'AwaitingConsent'::text) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_slot_assignments.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((u.action)::text = 'billing.slot-assignment.request'::text) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: shop_affiliation_requests request_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY request_insert ON public.shop_affiliation_requests FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (requester_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)));


--
-- Name: billing_accounts restriction_account_lookup; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY restriction_account_lookup ON public.billing_accounts FOR SELECT USING ((id = (NULLIF(current_setting('app.restriction_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacement_exceptions retire_obsolete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY retire_obsolete ON public.coverage_replacement_exceptions FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = ANY (ARRAY['AwaitingConsents'::text, 'ReadyForReview'::text])))) WITH CHECK (((state = 'Blocked'::text) AND (reason = 'SourceChanged'::text) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_replacement_exceptions.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((u.action)::text = 'billing.coverage-replacement-exception.request'::text) AND (u.xmin = (pg_current_xact_id())::xid)))) AND public.coverage_exception_stale(exception_id, (NULLIF(current_setting('app.exception_evaluated_at'::text, true), ''::text))::timestamp with time zone)));


--
-- Name: coverage_replacements retire_obsolete; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY retire_obsolete ON public.coverage_replacements FOR UPDATE USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = ANY (ARRAY['AwaitingConsent'::text, 'Scheduled'::text])))) WITH CHECK (((state = 'Blocked'::text) AND (reason = 'SourceChanged'::text) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_replacements.account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((u.action)::text = 'billing.coverage-replacement.schedule'::text) AND (u.xmin = (pg_current_xact_id())::xid)))) AND public.replacement_stale(replacement_id, (NULLIF(current_setting('app.replacement_evaluated_at'::text, true), ''::text))::timestamp with time zone)));


--
-- Name: payment_provider_routing safe_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY safe_read ON public.payment_provider_routing FOR SELECT USING (true);


--
-- Name: payment_provider_selections safe_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY safe_read ON public.payment_provider_selections FOR SELECT USING (true);


--
-- Name: payment_provider_bindings safe_references; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY safe_references ON public.payment_provider_bindings FOR SELECT USING (true);


--
-- Name: scheduled_capacity_reduction_resolutions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.scheduled_capacity_reduction_resolutions ENABLE ROW LEVEL SECURITY;

--
-- Name: scheduled_capacity_reduction_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.scheduled_capacity_reduction_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: scheduled_capacity_reductions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.scheduled_capacity_reductions ENABLE ROW LEVEL SECURITY;

--
-- Name: scheduled_subscription_change_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.scheduled_subscription_change_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: scheduled_subscription_changes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.scheduled_subscription_changes ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_approvals scope_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scope_read ON public.coverage_transfer_approvals FOR SELECT USING ((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_transfer_progress scope_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scope_read ON public.coverage_transfer_progress FOR SELECT USING ((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: trial_extensions scoped_extension_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_extension_read ON public.trial_extensions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) OR (shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)));


--
-- Name: checkout_trial_sources scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.checkout_trial_sources FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text) IS NOT NULL)));


--
-- Name: paid_fresh_starts scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.paid_fresh_starts FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (((NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid IS NOT NULL) OR (((NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid IS NOT NULL) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid)))));


--
-- Name: payment_restoration_holds scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.payment_restoration_holds FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid) OR ((manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid)))));


--
-- Name: payment_review_decisions scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.payment_review_decisions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (admin_id = (NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid) AND (session_id = (NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM (public.admin_sessions s
     JOIN public.admin_identities a ON ((a.id = s.admin_id)))
  WHERE ((s.id = payment_review_decisions.session_id) AND (a.id = payment_review_decisions.admin_id) AND (s.revoked_at IS NULL) AND (s.mfa_verified_at = payment_review_decisions.mfa_verified_at) AND (a.mfa_enrolled_at IS NOT NULL) AND (a.deactivated_at IS NULL) AND ((a.capabilities & 2) = 2))))));


--
-- Name: payment_trial_conversions scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.payment_trial_conversions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (((NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid IS NOT NULL) OR (((NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid IS NOT NULL) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid)))));


--
-- Name: service_restoration_adjustments scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.service_restoration_adjustments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.grant_admin_id'::text, true), ''::text))::uuid IS NOT NULL)));


--
-- Name: unmatched_payments scoped_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_insert ON public.unmatched_payments FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid) OR ((manual_receipt_id = (NULLIF(current_setting('app.manual_receipt_id'::text, true), ''::text))::uuid) AND (order_id = (NULLIF(current_setting('app.manual_order_id'::text, true), ''::text))::uuid) AND ((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL)))));


--
-- Name: checkout_trial_sources scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.checkout_trial_sources FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_transfer_bindings scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.coverage_transfer_bindings FOR SELECT USING ((((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid = source_account_id) OR ((NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid = destination_account_id) OR (shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)));


--
-- Name: paid_fresh_starts scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.paid_fresh_starts FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_restoration_holds scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.payment_restoration_holds FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: payment_review_decisions scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.payment_review_decisions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL)) OR (EXISTS ( SELECT
   FROM public.unmatched_payments c
  WHERE ((c.account_id = payment_review_decisions.account_id) AND (c.case_id = payment_review_decisions.case_id) AND (c.attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid)))))));


--
-- Name: payment_trial_conversions scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.payment_trial_conversions FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) OR (shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)));


--
-- Name: service_restoration_adjustments scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.service_restoration_adjustments FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: unmatched_payments scoped_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY scoped_read ON public.unmatched_payments FOR SELECT USING (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND ((((NULLIF(current_setting('app.manual_admin_id'::text, true), ''::text))::uuid IS NOT NULL) AND ((NULLIF(current_setting('app.manual_session_id'::text, true), ''::text))::uuid IS NOT NULL)) OR (attempt_id = (NULLIF(current_setting('app.payment_attempt_id'::text, true), ''::text))::uuid))));


--
-- Name: payment_provider_routing select_provider; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY select_provider ON public.payment_provider_routing FOR UPDATE USING (true) WITH CHECK (true);


--
-- Name: service_restoration_adjustments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.service_restoration_adjustments ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_affiliation_delivery_uses; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_affiliation_delivery_uses ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_affiliation_inbox; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_affiliation_inbox ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_affiliation_requests; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_affiliation_requests ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_affiliations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_affiliations ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_consents shop_owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_owner_insert ON public.coverage_transfer_consents FOR INSERT WITH CHECK (((purpose = 'ShopOwner'::text) AND (account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_merchant_inbox i
  WHERE ((i.kind = 'CoverageTransferConsent'::text) AND (i.account_id = coverage_transfer_consents.account_id) AND (i.event_id = coverage_transfer_consents.event_id) AND (i.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: complimentary_grant_shops shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.complimentary_grant_shops FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid));


--
-- Name: complimentary_grants shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.complimentary_grants FOR SELECT USING ((EXISTS ( SELECT
   FROM public.complimentary_grant_shops l
  WHERE ((l.grant_id = complimentary_grants.grant_id) AND (l.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: paid_slot_handoffs shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.paid_slot_handoffs FOR SELECT USING ((((NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid = outgoing_shop_id) OR ((NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid = incoming_shop_id)));


--
-- Name: payment_fulfilment_shops shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.payment_fulfilment_shops FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid));


--
-- Name: paid_fresh_starts shop_status_fresh_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_status_fresh_read ON public.paid_fresh_starts FOR SELECT USING ((EXISTS ( SELECT
   FROM public.payment_fulfilments f
  WHERE ((f.account_id = paid_fresh_starts.account_id) AND (f.order_id = paid_fresh_starts.order_id)))));


--
-- Name: payment_fulfilments shop_status_fulfilment_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_status_fulfilment_read ON public.payment_fulfilments FOR SELECT USING ((EXISTS ( SELECT
   FROM public.shop_affiliations a
  WHERE ((a.account_id = payment_fulfilments.account_id) AND (a.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid) AND ((a.term_id = ANY (ARRAY[payment_fulfilments.term_id, payment_fulfilments.predecessor_term_id])) OR (EXISTS ( SELECT
           FROM public.payment_fulfilment_shops l
          WHERE ((l.account_id = a.account_id) AND (l.shop_id = a.shop_id) AND (l.affiliation_request_id = a.request_id) AND (l.term_id = ANY (ARRAY[payment_fulfilments.term_id, payment_fulfilments.predecessor_term_id]))))))))));


--
-- Name: shop_trial_delivery_uses; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_trial_delivery_uses ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_trial_inbox; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_trial_inbox ENABLE ROW LEVEL SECURITY;

--
-- Name: development_gateway_controls simulator_only; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY simulator_only ON public.development_gateway_controls USING ((current_setting('app.development_gateway'::text, true) = 'on'::text)) WITH CHECK ((current_setting('app.development_gateway'::text, true) = 'on'::text));


--
-- Name: development_gateway_payments simulator_only; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY simulator_only ON public.development_gateway_payments USING ((current_setting('app.development_gateway'::text, true) = 'on'::text)) WITH CHECK ((current_setting('app.development_gateway'::text, true) = 'on'::text));


--
-- Name: development_gateway_sessions simulator_only; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY simulator_only ON public.development_gateway_sessions USING ((current_setting('app.development_gateway'::text, true) = 'on'::text)) WITH CHECK ((current_setting('app.development_gateway'::text, true) = 'on'::text));


--
-- Name: development_gateway_refunds simulator_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY simulator_scope ON public.development_gateway_refunds USING (((current_setting('app.development_gateway'::text, true) = 'on'::text) AND (provider_account_id = current_setting('app.gateway_account'::text, true)) AND (environment = current_setting('app.gateway_environment'::text, true)) AND (configuration_version = current_setting('app.gateway_configuration'::text, true))));


--
-- Name: payment_fulfilment_shops slot_link_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY slot_link_insert ON public.payment_fulfilment_shops FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.coverage_slot_assignments a
  WHERE ((a.account_id = payment_fulfilment_shops.account_id) AND (a.order_id = payment_fulfilment_shops.order_id) AND (a.shop_id = payment_fulfilment_shops.shop_id) AND (a.term_id = payment_fulfilment_shops.term_id) AND (a.affiliation_request_id = payment_fulfilment_shops.affiliation_request_id) AND (a.state = 'Applied'::text) AND (a.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: coverage_transfer_proposals source_request; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY source_request ON public.coverage_transfer_proposals FOR INSERT WITH CHECK (((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (requester_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND (state = 'Open'::text) AND (EXISTS ( SELECT
   FROM public.billing_account_members m
  WHERE ((m.id = coverage_transfer_proposals.requester_member_id) AND (m.account_id = coverage_transfer_proposals.source_account_id) AND (m.merchant_user_id = coverage_transfer_proposals.requester_id) AND (m.role = 1) AND (m.status = 1) AND (m.revision = coverage_transfer_proposals.requester_revision)))) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_transfer_proposals.source_account_id) AND (u.actor_id = coverage_transfer_proposals.requester_id) AND ((u.action)::text = 'billing.coverage-transfer.request'::text) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: coverage_transfer_proposals source_retire; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY source_retire ON public.coverage_transfer_proposals FOR UPDATE USING (((source_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (state = 'Open'::text))) WITH CHECK (((state = 'Retired'::text) AND (EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.account_id = coverage_transfer_proposals.source_account_id) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid) AND ((u.action)::text = 'billing.coverage-transfer.request'::text) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: shop_affiliations status_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY status_shop_read ON public.shop_affiliations FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid));


--
-- Name: subscription_coverage_terms status_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY status_shop_read ON public.subscription_coverage_terms FOR SELECT USING ((EXISTS ( SELECT
   FROM public.shop_affiliations a
  WHERE ((a.term_id = subscription_coverage_terms.term_id) AND (a.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: subscription_term_facts status_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY status_shop_read ON public.subscription_term_facts FOR SELECT USING ((EXISTS ( SELECT
   FROM public.shop_affiliations a
  WHERE ((a.term_id = subscription_term_facts.term_id) AND (a.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: subscription_cancellation_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.subscription_cancellation_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: subscription_coverage_terms; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.subscription_coverage_terms ENABLE ROW LEVEL SECURITY;

--
-- Name: subscription_term_facts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.subscription_term_facts ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_shop_heads transfer_head; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY transfer_head ON public.entitlement_shop_heads FOR UPDATE USING ((EXISTS ( SELECT
   FROM public.coverage_transfer_bindings b
  WHERE ((b.shop_id = entitlement_shop_heads.shop_id) AND (b.source_account_id = entitlement_shop_heads.account_id) AND (b.destination_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (b.xmin = (pg_current_xact_id())::xid))))) WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_shop_heads transfer_head_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY transfer_head_read ON public.entitlement_shop_heads FOR SELECT USING ((EXISTS ( SELECT
   FROM public.coverage_transfer_bindings b
  WHERE ((b.shop_id = entitlement_shop_heads.shop_id) AND (b.source_account_id = entitlement_shop_heads.account_id) AND (b.destination_account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (b.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: shop_affiliation_requests transfer_request; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY transfer_request ON public.shop_affiliation_requests FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.coverage_transfer_bindings b
  WHERE ((b.transfer_id = shop_affiliation_requests.request_id) AND (b.shop_id = shop_affiliation_requests.shop_id) AND (b.destination_account_id = shop_affiliation_requests.account_id) AND (b.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: shop_affiliation_delivery_uses transport_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY transport_insert ON public.shop_affiliation_delivery_uses FOR INSERT WITH CHECK ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: trial_history trial_account_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_account_read ON public.trial_history FOR SELECT USING ((account_id = (NULLIF(current_setting('app.billing_account_id'::text, true), ''::text))::uuid));


--
-- Name: shop_trial_delivery_uses trial_delivery; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_delivery ON public.shop_trial_delivery_uses FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid) AND (replay_id = (NULLIF(current_setting('app.trial_delivery_id'::text, true), ''::text))::uuid)));


--
-- Name: trial_extension_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.trial_extension_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: trial_extensions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.trial_extensions ENABLE ROW LEVEL SECURITY;

--
-- Name: trial_history; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.trial_history ENABLE ROW LEVEL SECURITY;

--
-- Name: trial_history trial_history_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_history_insert ON public.trial_history FOR INSERT WITH CHECK ((EXISTS ( SELECT
   FROM ((public.shop_trial_inbox i
     JOIN public.shop_affiliations a ON (((a.shop_id = i.shop_id) AND (a.account_id = i.account_id))))
     JOIN public.trial_policy_versions p ON ((p.id = trial_history.policy_id)))
  WHERE ((i.replay_id = (NULLIF(current_setting('app.trial_delivery_id'::text, true), ''::text))::uuid) AND (i.event_id = trial_history.event_id) AND (i.status = 'Applied'::text) AND (a.term_id IS NULL) AND (a.applied_at <= trial_history.starts_at) AND ((i.account_id = trial_history.account_id) AND (i.shop_id = trial_history.shop_id) AND (i.term_id = trial_history.term_id) AND (i.actor_id = trial_history.actor_id) AND (i.operation_key = trial_history.operation_key) AND (i.digest = trial_history.consent_digest)) AND (p.status = 2) AND (p.duration_days = 14) AND ((p.revision = trial_history.policy_revision) AND ((p.trial_terms_version)::text = trial_history.terms_version) AND (p.active_member_cap = trial_history.member_cap) AND (p.max_concurrent_trial_shops = trial_history.account_trial_cap) AND ((p.source = 1) = trial_history.development_fixture))))));


--
-- Name: shop_trial_inbox trial_inbox; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_inbox ON public.shop_trial_inbox FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid) AND (replay_id = (NULLIF(current_setting('app.trial_delivery_id'::text, true), ''::text))::uuid) AND (NOT (account_id IS DISTINCT FROM ( SELECT a.account_id
   FROM public.shop_affiliations a
  WHERE (a.shop_id = shop_trial_inbox.shop_id))))));


--
-- Name: trial_policy_versions trial_policy_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_policy_read ON public.trial_policy_versions FOR SELECT USING (((status = 2) AND (source = ANY (ARRAY[1, 2])) AND ((EXISTS ( SELECT
   FROM public.merchant_billing_delegation_uses u
  WHERE ((u.replay_id = (NULLIF(current_setting('app.trial_preview_replay'::text, true), ''::text))::uuid) AND (u.account_id IS NULL) AND ((u.action)::text = 'licensing.shop-trial.preview'::text) AND (u.actor_id = (NULLIF(current_setting('app.billing_actor_id'::text, true), ''::text))::uuid)))) OR (EXISTS ( SELECT
   FROM public.shop_trial_delivery_uses u
  WHERE (u.replay_id = (NULLIF(current_setting('app.trial_delivery_id'::text, true), ''::text))::uuid))))));


--
-- Name: trial_policy_versions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.trial_policy_versions ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_trial_delivery_uses trial_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_shop_read ON public.shop_trial_delivery_uses FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_trial_inbox trial_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_shop_read ON public.shop_trial_inbox FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid));


--
-- Name: subscription_coverage_terms trial_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_shop_read ON public.subscription_coverage_terms FOR SELECT USING ((EXISTS ( SELECT
   FROM public.trial_history h
  WHERE ((h.term_id = subscription_coverage_terms.term_id) AND (h.shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid)))));


--
-- Name: trial_history trial_shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_shop_read ON public.trial_history FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.status_shop_id'::text, true), ''::text))::uuid));


--
-- Name: subscription_coverage_terms trial_term_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY trial_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (((mode = 'Independent'::text) AND (capacity = 1) AND (EXISTS ( SELECT
   FROM public.shop_trial_inbox i
  WHERE ((i.replay_id = (NULLIF(current_setting('app.trial_delivery_id'::text, true), ''::text))::uuid) AND (i.account_id = subscription_coverage_terms.account_id) AND (i.term_id = subscription_coverage_terms.term_id) AND (i.status = 'Applied'::text))))));


--
-- Name: unmatched_payments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.unmatched_payments ENABLE ROW LEVEL SECURITY;

--
-- Name: verified_payments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.verified_payments ENABLE ROW LEVEL SECURITY;

--
-- Name: withdrawn_catalogue_offers; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.withdrawn_catalogue_offers ENABLE ROW LEVEL SECURITY;

--
-- Name: FUNCTION apply_confirmed_refund(a public.refund_attempts, at_time timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.apply_confirmed_refund(a public.refund_attempts, at_time timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION billing_transfer_has_proof(p_request uuid, p_actor uuid, p_account uuid, p_action text, p_at timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.billing_transfer_has_proof(p_request uuid, p_actor uuid, p_account uuid, p_action text, p_at timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION billing_transfer_membership_snapshot(p_account uuid, p_actor uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.billing_transfer_membership_snapshot(p_account uuid, p_actor uuid) FROM PUBLIC;


--
-- Name: FUNCTION claim_entitlement_delivery(p_now timestamp with time zone, p_lease uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.claim_entitlement_delivery(p_now timestamp with time zone, p_lease uuid) FROM PUBLIC;


--
-- Name: FUNCTION claim_gateway_inbox_event(p_now timestamp with time zone, p_token uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.claim_gateway_inbox_event(p_now timestamp with time zone, p_token uuid) FROM PUBLIC;


--
-- Name: FUNCTION claim_pending_payment(p_now timestamp with time zone, p_token uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.claim_pending_payment(p_now timestamp with time zone, p_token uuid) FROM PUBLIC;


--
-- Name: FUNCTION claim_refund(at_time timestamp with time zone, claim uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.claim_refund(at_time timestamp with time zone, claim uuid) FROM PUBLIC;


--
-- Name: FUNCTION complete_pending_payment(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.complete_pending_payment() FROM PUBLIC;


--
-- Name: FUNCTION due_entitlement_verification(p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.due_entitlement_verification(p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION due_scheduled_capacity_reductions(p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.due_scheduled_capacity_reductions(p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION due_transfer_applications(p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.due_transfer_applications(p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamp with time zone, p_success boolean, p_wait_ack boolean, p_failure text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamp with time zone, p_success boolean, p_wait_ack boolean, p_failure text) FROM PUBLIC;


--
-- Name: FUNCTION finish_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamp with time zone, p_outcome text, p_failure text, p_dead boolean); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.finish_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamp with time zone, p_outcome text, p_failure text, p_dead boolean) FROM PUBLIC;


--
-- Name: FUNCTION finish_pending_payment(p_token uuid, p_now timestamp with time zone, p_complete boolean); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.finish_pending_payment(p_token uuid, p_now timestamp with time zone, p_complete boolean) FROM PUBLIC;


--
-- Name: FUNCTION finish_refund(claim uuid, at_time timestamp with time zone, outcome text, detail text, remote text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.finish_refund(claim uuid, at_time timestamp with time zone, outcome text, detail text, remote text) FROM PUBLIC;


--
-- Name: FUNCTION lock_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.lock_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION lock_pending_payment(p_token uuid, p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.lock_pending_payment(p_token uuid, p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION prepare_approved_refund(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.prepare_approved_refund() FROM PUBLIC;


--
-- Name: FUNCTION queue_pending_payment(); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.queue_pending_payment() FROM PUBLIC;


--
-- Name: FUNCTION receive_gateway_inbox_event(p_inbox uuid, p_provider text, p_account text, p_environment text, p_version text, p_event text, p_kind text, p_order text, p_payment text, p_hash bytea, p_evidence bytea, p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.receive_gateway_inbox_event(p_inbox uuid, p_provider text, p_account text, p_environment text, p_version text, p_event text, p_kind text, p_order text, p_payment text, p_hash bytea, p_evidence bytea, p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION receive_refund_hint(id uuid, provider text, account text, env text, version text, event_id text, payment text, remote text, receipt text, hash bytea, evidence bytea, at_time timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.receive_refund_hint(id uuid, provider text, account text, env text, version text, event_id text, payment text, remote text, receipt text, hash bytea, evidence bytea, at_time timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION recheck_refund(acct uuid, request uuid, at_time timestamp with time zone, simulated text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.recheck_refund(acct uuid, request uuid, at_time timestamp with time zone, simulated text) FROM PUBLIC;


--
-- Name: FUNCTION refund_finance_observation(acct uuid, request uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.refund_finance_observation(acct uuid, request uuid) FROM PUBLIC;


--
-- Name: FUNCTION refund_outcome(acct uuid, request uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.refund_outcome(acct uuid, request uuid) FROM PUBLIC;


--
-- Name: FUNCTION requeue_gateway_inbox_event(p_inbox uuid, p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.requeue_gateway_inbox_event(p_inbox uuid, p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION start_refund_send(claim uuid, at_time timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.start_refund_send(claim uuid, at_time timestamp with time zone) FROM PUBLIC;


--
-- PostgreSQL database dump complete
--
