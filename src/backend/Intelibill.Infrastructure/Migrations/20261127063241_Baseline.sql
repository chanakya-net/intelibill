-- Merchant schema baseline
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
-- Name: bound_entitlement_reconciliation_schedule(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.bound_entitlement_reconciliation_schedule() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.last_queued_at IS NULL THEN
        NEW.last_queued_at := NEW.next_check_at - interval '1 hour';
    ELSIF TG_OP = 'UPDATE' AND NEW.event_id <> OLD.event_id AND NEW.last_queued_at = OLD.last_queued_at THEN
        NEW.last_queued_at := NEW.next_check_at - interval '55 seconds';
    END IF;
    NEW.next_check_at := least(NEW.next_check_at, NEW.last_queued_at + interval '55 seconds');
    RETURN NEW;
END $$;


--
-- Name: capacity_fence_shops(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.capacity_fence_shops(account uuid, term uuid) RETURNS TABLE(shop_id uuid, affiliation_id uuid, shop_revision bigint, state text, starts_at timestamp with time zone, next_boundary timestamp with time zone, access_ends_at timestamp with time zone, capacity integer, members integer, active_members integer, seat_hash text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT s.shop_id,s.affiliation_request_id,s.revision,s.state,s.starts_at,s.next_boundary,s.access_ends_at,
        s.shop_capacity,s.member_allowance,m.active_members,m.seat_hash
    FROM public.shop_license_states s
    CROSS JOIN LATERAL (SELECT count(*) FILTER(WHERE m.status='Active')::int AS active_members,
        encode(sha256(convert_to(coalesce(string_agg(m.id::text || '/' || m.authority_version::text,','
            ORDER BY m.id),''),'UTF8')),'hex') AS seat_hash
        FROM public.shop_memberships m WHERE m.shop_id=s.shop_id) m
    JOIN public.shop_affiliation_consent_events accepted ON accepted.shop_id=s.shop_id AND accepted.account_id=s.account_id
        AND accepted.request_id=s.affiliation_request_id AND accepted.kind='Accepted'
    JOIN public.shop_affiliation_delivery_receipts receipt ON receipt.event_id=accepted.id AND receipt.status='Applied'
    WHERE NOT EXISTS (SELECT FROM public.shop_affiliation_consent_events pending
        LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id=pending.id
        WHERE pending.account_id=account AND pending.term_id=term AND pending.kind='Accepted' AND r.status IS NULL)
        AND s.term_id=term AND s.account_id=account AND s.basis='Paid'
        AND account=nullif(current_setting('app.entitlement_account_id',true),'')::uuid
        AND EXISTS(SELECT FROM public.entitlement_request_uses u WHERE u.replay_id=
            nullif(current_setting('app.entitlement_replay_id',true),'')::uuid AND u.xmin=pg_current_xact_id()::xid)
    ORDER BY s.shop_id
$$;


--
-- Name: claim_entitlement_delivery(timestamp with time zone, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.claim_entitlement_delivery(p_now timestamp with time zone, p_lease uuid) RETURNS TABLE(event_id uuid, kind text, payload bytea, attempts integer)
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH due AS MATERIALIZED (
        SELECT o.event_id, (o.attempts < 12 OR o.status = 'DeadLetter') AS claimable FROM public.entitlement_outbox o
        WHERE (o.status IN ('Pending', 'Leased', 'AwaitingAcknowledgement') )
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
-- Name: coverage_transfer_shop_inspect(uuid, uuid, uuid, uuid, uuid, uuid, uuid, bigint, bigint, text, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.coverage_transfer_shop_inspect(p_account uuid, p_shop uuid, p_affiliation uuid, p_transfer uuid, p_event uuid, p_actor uuid, p_membership uuid, p_version bigint, p_revision bigint, p_digest text, p_at timestamp with time zone) RETURNS TABLE(reason text, permit_count integer, max_permit_valid_until timestamp with time zone, permit_digest text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH gate AS (
        SELECT p_account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
            AND p_shop = nullif(current_setting('app.active_shop_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.entitlement_request_uses u
                WHERE u.replay_id = nullif(current_setting('app.entitlement_replay_id', true), '')::uuid
                    AND u.xmin = pg_current_xact_id()::xid) AS ok
    ), consent AS (
        SELECT e.generation FROM public.coverage_transfer_consent_events e
        WHERE e.id = p_event AND e.transfer_id = p_transfer AND e.kind = 'Accepted' AND e.shop_id = p_shop
            AND e.account_id = p_account AND e.affiliation_request_id = p_affiliation
            AND e.proposal_revision = p_revision AND e.terms_digest = p_digest AND e.actor_id = p_actor
            AND e.membership_id = p_membership AND e.authority_version = p_version
    ), permits AS (
        -- Match the licensing digest without changing the bytes bound to existing proposal terms.
        SELECT count(*)::int AS cnt, max(p.valid_until) AS max_end,
            encode(sha256(convert_to(coalesce(string_agg(p.id::text || '/' || extract(epoch FROM p.not_before)::text
                || '/' || extract(epoch FROM p.valid_until)::text, ',' ORDER BY p.id), ''), 'UTF8')), 'hex') AS digest
        FROM public.offline_license_permits p WHERE p.shop_id = p_shop AND p.valid_until > p_at
    )
    SELECT CASE
        WHEN NOT EXISTS (SELECT FROM consent) THEN 'ShopConsentMissing'
        WHEN EXISTS (SELECT FROM public.coverage_transfer_consent_events v, consent c
            WHERE v.transfer_id = p_transfer
                AND (v.generation > c.generation OR (v.generation = c.generation AND v.kind = 'Revoked')))
            THEN 'ShopConsentSuperseded'
        WHEN NOT EXISTS (SELECT FROM public.shop_memberships m JOIN public.users u ON u.id = m.user_id
            WHERE u.is_login_enabled AND ((u.email IS NOT NULL AND u.is_email_verified)
                OR (u.phone_number IS NOT NULL AND u.is_phone_verified))
            AND m.id = p_membership AND m.shop_id = p_shop
            AND m.user_id = p_actor AND m.role = 'Owner' AND m.status = 'Active' AND m.authority_version = p_version)
            THEN 'ShopOwnerChanged'
        WHEN NOT EXISTS (SELECT FROM public.shop_license_states s WHERE s.shop_id = p_shop
            AND s.account_id = p_account AND s.affiliation_request_id = p_affiliation AND s.state <> 'Closed')
            THEN 'AffiliationChanged'
    END, pm.cnt, pm.max_end, pm.digest
    FROM gate g CROSS JOIN permits pm WHERE g.ok
$$;


--
-- Name: due_merchant_entitlement_reconciliation(timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.due_merchant_entitlement_reconciliation(p_now timestamp with time zone) RETURNS TABLE(account_id uuid, shop_id uuid, affiliation_request_id uuid)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT e.account_id, e.shop_id, e.request_id FROM public.merchant_entitlement_bindings e
    LEFT JOIN public.entitlement_reconciliation_requests r USING (shop_id)
    LEFT JOIN public.entitlement_outbox o ON o.event_id = r.event_id
    LEFT JOIN public.shop_license_states s ON s.shop_id = e.shop_id AND s.account_id = e.account_id
        AND s.affiliation_request_id = e.request_id
    WHERE e.kind = 'Accepted' AND (r.shop_id IS NULL OR (o.status IN ('Delivered', 'DeadLetter') AND
        (r.next_check_at <= p_now OR s.next_boundary > r.last_queued_at AND s.next_boundary <= p_now)))
        AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
AND nullif(current_setting('app.current_user_id', true), '') IS NULL
AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        AND NOT EXISTS (SELECT FROM public.shop_affiliation_delivery_receipts f
            WHERE f.event_id = e.id AND f.status = 'NeedsReview')
    ORDER BY r.next_check_at NULLS FIRST, e.shop_id LIMIT 20
$$;


--
-- Name: exception_fence_inspect(uuid, uuid, uuid, bigint, text, uuid, uuid, uuid, uuid, uuid, uuid, integer, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.exception_fence_inspect(p_account uuid, p_term uuid, p_exception uuid, p_proposal_rev bigint, p_terms_digest text, p_out_shop uuid, p_out_aff uuid, p_in_shop uuid, p_in_aff uuid, p_out_event uuid, p_in_event uuid, p_allowance integer, p_at timestamp with time zone) RETURNS TABLE(outgoing_bound boolean, incoming_bound boolean, outgoing_consent_valid boolean, incoming_consent_valid boolean, incoming_active_members integer, outgoing_permit_count integer, max_permit_valid_until timestamp with time zone, permit_digest text, fence_hash text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH gate AS (
        SELECT p_account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.entitlement_request_uses u
                WHERE u.replay_id = nullif(current_setting('app.entitlement_replay_id', true), '')::uuid
                    AND u.xmin = pg_current_xact_id()::xid) AS ok
    ), out_bind AS (
        SELECT EXISTS (
            SELECT FROM public.shop_affiliation_consent_events e
            LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id
            JOIN public.shop_license_states s ON s.shop_id = e.shop_id AND s.account_id = e.account_id
                AND s.affiliation_request_id = e.request_id
            WHERE e.shop_id = p_out_shop AND e.account_id = p_account AND e.request_id = p_out_aff
                AND e.kind = 'Accepted' AND r.status IS DISTINCT FROM 'NeedsReview'
                AND s.state <> 'Closed'
        ) AS bound,
        coalesce((SELECT s.revision FROM public.shop_license_states s WHERE s.shop_id = p_out_shop), 0) AS rev
    ), in_bind AS (
        SELECT EXISTS (
            SELECT FROM public.shop_affiliation_consent_events e
            LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id
            LEFT JOIN public.shop_license_states s ON s.shop_id = e.shop_id
            WHERE e.shop_id = p_in_shop AND e.account_id = p_account AND e.request_id = p_in_aff
                AND e.kind = 'Accepted' AND r.status IS DISTINCT FROM 'NeedsReview'
                AND (s.shop_id IS NULL OR (s.account_id = p_account AND s.affiliation_request_id = p_in_aff
                    AND s.state <> 'Closed'))
        ) AS bound,
        coalesce((SELECT s.revision FROM public.shop_license_states s WHERE s.shop_id = p_in_shop), 0) AS rev
    ), out_consent AS (
        SELECT EXISTS (
            SELECT FROM public.coverage_exception_consent_events e
            JOIN public.shop_memberships m ON m.shop_id = e.shop_id AND m.id = e.membership_id
                AND m.user_id = e.actor_id
            WHERE e.id = p_out_event AND e.exception_id = p_exception AND e.leg = 'Outgoing'
                AND e.kind = 'Accepted' AND e.account_id = p_account AND e.term_id = p_term
                AND e.shop_id = p_out_shop AND e.affiliation_request_id = p_out_aff
                AND e.proposal_revision = p_proposal_rev AND e.terms_digest = p_terms_digest
                AND m.authority_version = e.authority_version AND m.role = 'Owner' AND m.status = 'Active'
                AND NOT EXISTS (SELECT FROM public.coverage_exception_consent_events v
                    WHERE v.exception_id = p_exception AND v.leg = 'Outgoing' AND v.kind = 'Revoked')
        ) AS valid
    ), in_consent AS (
        SELECT EXISTS (
            SELECT FROM public.coverage_exception_consent_events e
            JOIN public.shop_memberships m ON m.shop_id = e.shop_id AND m.id = e.membership_id
                AND m.user_id = e.actor_id
            WHERE e.id = p_in_event AND e.exception_id = p_exception AND e.leg = 'Incoming'
                AND e.kind = 'Accepted' AND e.account_id = p_account AND e.term_id = p_term
                AND e.shop_id = p_in_shop AND e.affiliation_request_id = p_in_aff
                AND e.proposal_revision = p_proposal_rev AND e.terms_digest = p_terms_digest
                AND e.member_allowance = p_allowance
                AND m.authority_version = e.authority_version AND m.role = 'Owner' AND m.status = 'Active'
                AND NOT EXISTS (SELECT FROM public.coverage_exception_consent_events v
                    WHERE v.exception_id = p_exception AND v.leg = 'Incoming' AND v.kind = 'Revoked')
        ) AS valid
    ), in_seats AS (
        SELECT count(*) FILTER (WHERE m.status = 'Active')::int AS active_members,
            encode(sha256(convert_to(coalesce(string_agg(m.id::text || '/' || m.authority_version::text || '/' || m.status::text, ','
                ORDER BY m.id), ''), 'UTF8')), 'hex') AS seat_hash
        FROM public.shop_memberships m WHERE m.shop_id = p_in_shop
    ), out_seats AS (
        SELECT encode(sha256(convert_to(coalesce(string_agg(m.id::text || '/' || m.authority_version::text || '/' || m.status::text, ','
            ORDER BY m.id), ''), 'UTF8')), 'hex') AS seat_hash
        FROM public.shop_memberships m WHERE m.shop_id = p_out_shop
    ), permits AS (
        SELECT count(*)::int AS cnt, max(p.valid_until) AS max_end,
            encode(sha256(convert_to(coalesce(string_agg(p.id::text || '/' || to_char(p.valid_until AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), ','
                ORDER BY p.id), 'none'), 'UTF8')), 'hex') AS digest
        FROM public.offline_license_permits p
        WHERE p.shop_id = p_out_shop AND p.valid_until > p_at
    )
    SELECT ob.bound, ib.bound, oc.valid, ic.valid, s_in.active_members,
        pm.cnt, pm.max_end, pm.digest,
        encode(sha256(convert_to(
            ob.rev::text || '/' || ib.rev::text || '/' || s_out.seat_hash || '/' || s_in.seat_hash || '/' || pm.digest || '/'
            || coalesce((SELECT revision FROM public.account_license_restrictions WHERE account_id = p_account), 0)::text,
            'UTF8')), 'hex')
    FROM gate g CROSS JOIN out_bind ob CROSS JOIN in_bind ib CROSS JOIN out_consent oc
    CROSS JOIN in_consent ic CROSS JOIN in_seats s_in CROSS JOIN out_seats s_out CROSS JOIN permits pm
    WHERE g.ok
$$;


--
-- Name: exception_outgoing_released(uuid, bigint, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.exception_outgoing_released(p_exception uuid, p_generation bigint, p_at timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    dec public.merchant_coverage_exception_decisions%ROWTYPE;
    prior_scope text := current_setting('app.active_shop_id', true);
    released bool;
BEGIN
    SELECT * INTO dec FROM public.merchant_coverage_exception_decisions d
    WHERE d.exception_id = p_exception AND d.generation = p_generation AND d.outcome = 'Consumed'
        AND d.incoming_shop_id = nullif(prior_scope, '')::uuid;
    IF NOT FOUND OR dec.paid_cutover_at > p_at THEN RETURN false; END IF;
    BEGIN
        PERFORM set_config('app.active_shop_id', dec.outgoing_shop_id::text, true);
        SELECT EXISTS (
                SELECT FROM public.merchant_coverage_exception_cutoffs c
                WHERE c.exception_id = p_exception AND c.generation = p_generation
                    AND c.shop_id = dec.outgoing_shop_id AND c.cutoff_at <= p_at)
            AND EXISTS (
                SELECT FROM public.shop_license_states s
                JOIN public.license_grant_evidence e ON e.event_id = s.evidence_id
                WHERE s.shop_id = dec.outgoing_shop_id AND s.account_id = dec.account_id
                    AND convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'replacementExceptionId' = p_exception::text
                    AND convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'replacementExceptionLeg' = 'Outgoing'
                    AND (convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'replacementExceptionGeneration')::bigint = p_generation
                    AND (s.state NOT IN ('Active', 'Grace') OR s.access_ends_at <= p_at))
            AND NOT EXISTS (
                SELECT FROM public.offline_license_permits p
                WHERE p.shop_id = dec.outgoing_shop_id AND p.valid_until > p_at)
            INTO released;
        PERFORM set_config('app.active_shop_id', coalesce(prior_scope, ''), true);
        RETURN released;
    EXCEPTION WHEN OTHERS THEN
        PERFORM set_config('app.active_shop_id', coalesce(prior_scope, ''), true);
        RAISE;
    END;
END $$;


--
-- Name: expiry_protect_disposal_adjustment(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_protect_disposal_adjustment() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.disposition_change_id IS NOT NULL THEN RAISE EXCEPTION 'Disposal is immutable' USING ERRCODE = '23514'; END IF;
        RETURN OLD;
    END IF;
    IF OLD.disposition_change_id IS NOT NULL AND (NEW IS DISTINCT FROM OLD) THEN
        RAISE EXCEPTION 'Disposal is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END; $$;


--
-- Name: expiry_protect_source_disposition(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_protect_source_disposition() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.quarantine_change_id IS NOT NULL THEN RAISE EXCEPTION 'Original disposition is immutable' USING ERRCODE = '23514'; END IF;
        RETURN OLD;
    END IF;
    IF OLD.quarantine_change_id IS DISTINCT FROM NEW.quarantine_change_id OR OLD.quarantined_quantity IS DISTINCT FROM NEW.quarantined_quantity
        OR OLD.disposition_reason_code IS DISTINCT FROM NEW.disposition_reason_code THEN
        RAISE EXCEPTION 'Original disposition is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END; $$;


--
-- Name: expiry_reject_revision_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_reject_revision_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Expiry policy revisions are immutable' USING ERRCODE = '23514';
END;
$$;


--
-- Name: expiry_reject_sale_evidence_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_reject_sale_evidence_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.expiry_decisions IS NOT NULL THEN
            RAISE EXCEPTION 'Sale expiry evidence is immutable' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;
    IF OLD.expiry_decisions IS NOT NULL AND NEW.expiry_decisions IS DISTINCT FROM OLD.expiry_decisions THEN
        RAISE EXCEPTION 'Sale expiry evidence is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: expiry_validate_batch_quarantine(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_validate_batch_quarantine() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE retained numeric;
BEGIN
    SELECT COALESCE(sum(CASE WHEN root_change_id IS NULL THEN quantity ELSE -quantity END),0) INTO retained
        FROM batch_disposition_changes WHERE shop_id = NEW.shop_id AND inventory_batch_id = NEW.id;
    IF (SELECT quarantined_quantity FROM inventory_batches WHERE id = NEW.id AND shop_id = NEW.shop_id) IS DISTINCT FROM retained THEN
        RAISE EXCEPTION 'Quarantine must equal retained root balances' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END; $$;


--
-- Name: expiry_validate_disposition(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_validate_disposition() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE root batch_disposition_changes; balance numeric; aggregate_hold numeric; physical_hold numeric; valid_source boolean;
BEGIN
    IF NEW.root_change_id IS NULL THEN
        root := NEW;
    ELSE
        SELECT * INTO root FROM batch_disposition_changes WHERE id = NEW.root_change_id AND shop_id = NEW.shop_id;
        IF root.root_change_id IS NOT NULL OR root.action <> 'Quarantine' OR root.source_kind <> NEW.source_kind
            OR root.source_id <> NEW.source_id OR root.source_line_id <> NEW.source_line_id
            OR root.reason_code <> NEW.reason_code OR root.expiry_date IS DISTINCT FROM NEW.expiry_date
            OR root.manufacturing_date IS DISTINCT FROM NEW.manufacturing_date OR root.policy_json <> NEW.policy_json THEN
            RAISE EXCEPTION 'Disposition must retain its original root' USING ERRCODE = '23514';
        END IF;
    END IF;
    SELECT root.quantity - COALESCE(sum(quantity),0) INTO balance FROM batch_disposition_changes WHERE root_change_id = root.id AND shop_id = root.shop_id;
    IF balance < 0 THEN RAISE EXCEPTION 'Disposition root is exhausted' USING ERRCODE = '23514'; END IF;
    SELECT COALESCE(sum(CASE WHEN root_change_id IS NULL THEN quantity ELSE -quantity END),0) INTO aggregate_hold
        FROM batch_disposition_changes WHERE shop_id = NEW.shop_id AND inventory_batch_id = NEW.inventory_batch_id;
    SELECT quarantined_quantity INTO physical_hold FROM inventory_batches WHERE id = NEW.inventory_batch_id AND shop_id = NEW.shop_id;
    IF physical_hold IS DISTINCT FROM aggregate_hold THEN RAISE EXCEPTION 'Quarantine must equal retained root balances' USING ERRCODE = '23514'; END IF;
    IF root.source_kind = 'SaleReturn' THEN
        SELECT EXISTS (SELECT 1 FROM sale_return_items ri JOIN sale_items si ON si.id = ri.sale_item_id
            WHERE ri.id = root.source_line_id AND ri.sale_return_id = root.source_id AND ri.shop_id = root.shop_id
                AND si.inventory_batch_id = root.inventory_batch_id AND ri.quarantine_change_id = root.id
                AND ri.quarantined_quantity = root.quantity AND ri.disposition_reason_code = root.reason_code) INTO valid_source;
    ELSE
        SELECT EXISTS (SELECT 1 FROM sale_cancellation_lines cl WHERE cl.id = root.source_line_id AND cl.sale_cancellation_id = root.source_id
            AND cl.shop_id = root.shop_id AND cl.inventory_batch_id = root.inventory_batch_id AND cl.quarantine_change_id = root.id
            AND cl.quarantined_quantity = root.quantity AND cl.disposition_reason_code = root.reason_code) INTO valid_source;
    END IF;
    IF NOT valid_source THEN RAISE EXCEPTION 'Disposition source must name its exact recovered goods' USING ERRCODE = '23514'; END IF;
    IF NEW.action = 'Dispose' AND NOT EXISTS (SELECT 1 FROM inventory_adjustments a WHERE a.id = NEW.inventory_adjustment_id
        AND a.shop_id = NEW.shop_id AND a.inventory_batch_id = NEW.inventory_batch_id AND a.disposition_change_id = NEW.id
        AND a.quantity = NEW.quantity AND NOT a.is_voided) THEN
        RAISE EXCEPTION 'Disposal must have its original independent loss adjustment' USING ERRCODE = '23514';
    END IF;
    IF NEW.stock_movement_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM stock_transactions m WHERE m.id = NEW.stock_movement_id
        AND m.shop_id = NEW.shop_id AND m.inventory_batch_id = NEW.inventory_batch_id
        AND m.quantity = CASE WHEN NEW.action = 'Quarantine' THEN NEW.quantity ELSE -NEW.quantity END) THEN
        RAISE EXCEPTION 'Disposition movement must retain exact physical effect' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END; $$;


--
-- Name: expiry_validate_source_disposition(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.expiry_validate_source_disposition() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE expected_source_id uuid; expected_source_kind text;
BEGIN
    IF NEW.quarantine_change_id IS NULL THEN
        IF NEW.quarantined_quantity <> 0 OR NEW.disposition_reason_code IS NOT NULL THEN
            RAISE EXCEPTION 'Held source goods need their exact root' USING ERRCODE = '23514';
        END IF;
        RETURN NEW;
    END IF;
    IF TG_TABLE_NAME = 'sale_return_items' THEN
        expected_source_id := NEW.sale_return_id; expected_source_kind := 'SaleReturn';
    ELSE
        expected_source_id := NEW.sale_cancellation_id; expected_source_kind := 'SaleCancellation';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM batch_disposition_changes root WHERE root.id = NEW.quarantine_change_id
        AND root.shop_id = NEW.shop_id AND root.source_id = expected_source_id AND root.source_kind = expected_source_kind
        AND root.source_line_id = NEW.id AND root.root_change_id IS NULL AND root.action = 'Quarantine'
        AND root.quantity = NEW.quarantined_quantity AND root.quantity <= NEW.quantity AND root.reason_code = NEW.disposition_reason_code) THEN
        RAISE EXCEPTION 'Held source goods must retain their own exact quarantine root' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END; $$;


--
-- Name: finish_entitlement_delivery(uuid, uuid, timestamp with time zone, boolean, boolean, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamp with time zone, p_success boolean, p_wait_ack boolean, p_failure text) RETURNS void
    LANGUAGE sql SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    
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
-- Name: guard_enforcement_activation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_enforcement_activation() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.enforcement_activated_at IS NOT NULL THEN
            RAISE EXCEPTION 'New projections are observe-only' USING ERRCODE = '55000';
        END IF;
        RETURN NEW;
    END IF;
    IF TG_OP = 'DELETE' OR (OLD.enforcement_activated_at IS NOT NULL
        AND NEW.enforcement_activated_at IS DISTINCT FROM OLD.enforcement_activated_at) THEN
        RAISE EXCEPTION 'Enforcement activation is retained' USING ERRCODE = '55000';
    END IF;
    IF NEW.enforcement_activated_at IS DISTINCT FROM OLD.enforcement_activated_at
        AND current_user <> (SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = TG_RELID) THEN
        RAISE EXCEPTION 'Only the schema owner can preserve historical activation' USING ERRCODE = '55000';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_license_projection(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_license_projection() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    account_scope boolean := TG_TABLE_NAME = 'account_license_restrictions';
    active_shop uuid := nullif(current_setting('app.active_shop_id', true), '')::uuid;
    prior_issued_at timestamptz;
BEGIN
    IF NEW.revision < OLD.revision OR (NEW.revision = OLD.revision AND (to_jsonb(NEW) - 'enforcement_activated_at')
        IS DISTINCT FROM (to_jsonb(OLD) - 'enforcement_activated_at')) THEN
        RAISE EXCEPTION 'Entitlement head cannot go backwards' USING ERRCODE = '23514';
    END IF;
    IF NEW.upstream_verified_at < OLD.upstream_verified_at THEN
        SELECT CASE WHEN account_scope THEN account_issued_at ELSE shop_issued_at END INTO prior_issued_at
        FROM public.merchant_licence_proof_issuance(active_shop)
        WHERE EXISTS (SELECT FROM public.shop_affiliation_consent_events c
            WHERE c.kind = 'Accepted' AND c.account_id = OLD.account_id AND c.shop_id = active_shop)
            AND (account_scope OR active_shop = (to_jsonb(OLD)->>'shop_id')::uuid);
        IF prior_issued_at IS NULL OR OLD.upstream_verified_at < prior_issued_at + interval '1 second'
            OR NOT EXISTS (SELECT FROM public.license_grant_evidence e
                WHERE e.event_id = NEW.evidence_id AND e.shop_id = active_shop
                    AND e.account_id = NEW.account_id AND NEW.account_id = OLD.account_id
                    AND NEW.upstream_verified_at < e.issued_at + interval '1 second'
                    AND NEW.upstream_verified_at = CASE WHEN account_scope THEN e.account_upstream_verified_at
                        ELSE e.shop_upstream_verified_at END
                    AND NEW.revision = CASE WHEN account_scope THEN e.account_revision ELSE e.shop_revision END)
        THEN
            RAISE EXCEPTION 'Entitlement stamp correction requires coherent higher evidence' USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_offline_permit_exception_cutoff(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_offline_permit_exception_cutoff() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF EXISTS (
        SELECT FROM public.merchant_coverage_exception_cutoffs c
        JOIN public.offline_license_grants g ON g.id = NEW.grant_id
            AND g.shop_id = c.shop_id AND g.account_id = c.account_id
            AND g.affiliation_request_id = c.affiliation_request_id
        LEFT JOIN public.shop_license_states s ON s.shop_id = c.shop_id
            AND s.account_id = c.account_id AND s.affiliation_request_id = c.affiliation_request_id
        LEFT JOIN public.license_grant_evidence e ON e.event_id = g.shop_evidence_id
            AND e.shop_id = g.shop_id AND e.account_id = g.account_id
        WHERE c.shop_id = NEW.shop_id
            AND (s.term_id = c.term_id
                OR (convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'termId')::uuid = c.term_id)
            AND (NEW.not_before >= c.cutoff_at OR NEW.valid_until > c.cutoff_at)
    ) THEN
        RAISE EXCEPTION 'Offline permit exceeds coverage exception cutoff' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_shop_affiliation_consent(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_shop_affiliation_consent() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    -- The application already holds this shop fence after the user and session locks.
    PERFORM 1 FROM public.shops WHERE id = NEW.shop_id FOR UPDATE;
    IF NOT EXISTS (
        SELECT FROM public.shop_memberships m
        WHERE m.id = NEW.membership_id AND m.user_id = NEW.actor_id AND m.shop_id = NEW.shop_id
            AND m.role = 'Owner' AND m.status = 'Active' AND m.authority_version = NEW.authority_version
    ) OR (NEW.kind IN ('Accepted', 'Revoked') AND NOT EXISTS (
        SELECT FROM public.shop_affiliation_consent_events g
        WHERE g.shop_id = NEW.shop_id AND g.request_id = NEW.request_id AND g.kind = 'Granted'
            AND g.account_id = NEW.account_id AND g.term_id IS NOT DISTINCT FROM NEW.term_id
            AND g.actor_id = NEW.actor_id AND g.membership_id = NEW.membership_id
            AND g.authority_version = NEW.authority_version AND g.recorded_at <= NEW.recorded_at
            AND g.request_expires_at = NEW.request_expires_at
    )) OR EXISTS (
        SELECT FROM public.shop_affiliation_consent_events e
        WHERE e.shop_id = NEW.shop_id AND (e.kind = 'Accepted'
            OR (e.request_id = NEW.request_id AND e.kind = 'Revoked'))
    ) THEN
        RAISE EXCEPTION 'Shop affiliation consent is invalid' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: guard_transfer_permit(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.guard_transfer_permit() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    IF NEW.valid_until > (SELECT public.transfer_source_cutoff(NEW.shop_id,g.affiliation_request_id)
        FROM public.offline_license_grants g WHERE g.id=NEW.grant_id AND g.shop_id=NEW.shop_id) THEN
        RAISE EXCEPTION 'Permit crosses coverage transfer' USING ERRCODE='23514'; END IF;
    RETURN NEW;
END $$;


--
-- Name: mark_identity_business_access(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.mark_identity_business_access() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'public'
    AS $$
BEGIN
    UPDATE public.users SET requires_operator_recovery = true WHERE id = NEW.user_id;
    RETURN NEW;
END;
$$;


--
-- Name: merchant_licence_proof_issuance(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.merchant_licence_proof_issuance(p_shop_id uuid) RETURNS TABLE(shop_issued_at timestamp with time zone, account_issued_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    WITH transferred AS (
        SELECT se.issued_at AS shop_issued_at, ae.issued_at AS account_issued_at
        FROM public.shop_license_states s
        JOIN public.merchant_transfer_decisions d ON d.transfer_id=s.affiliation_request_id
            AND d.destination_account_id=s.account_id AND d.shop_id=s.shop_id AND d.outcome='Consumed'
        JOIN public.license_grant_evidence se ON se.event_id=s.evidence_id AND se.shop_id=s.shop_id
        LEFT JOIN public.account_license_restrictions a ON a.account_id=s.account_id
        LEFT JOIN public.license_grant_evidence ae ON ae.event_id=a.evidence_id AND ae.account_id=s.account_id
        WHERE s.shop_id=p_shop_id AND s.shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid
    )
    SELECT * FROM transferred UNION ALL
    SELECT * FROM public.original_licence_proof_issuance(p_shop_id) WHERE NOT EXISTS (SELECT FROM transferred)
$$;


--
-- Name: original_licence_proof_issuance(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.original_licence_proof_issuance(p_shop_id uuid) RETURNS TABLE(shop_issued_at timestamp with time zone, account_issued_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT se.issued_at, ae.issued_at
    FROM public.shop_affiliation_consent_events c
    LEFT JOIN public.shop_license_states s ON s.shop_id = c.shop_id AND s.account_id = c.account_id
        AND s.affiliation_request_id = c.request_id
    LEFT JOIN public.license_grant_evidence se ON se.event_id = s.evidence_id AND se.shop_id = s.shop_id
    LEFT JOIN public.account_license_restrictions a ON a.account_id = c.account_id
    LEFT JOIN public.license_grant_evidence ae ON ae.event_id = a.evidence_id AND ae.account_id = a.account_id
    WHERE c.kind = 'Accepted' AND c.shop_id = p_shop_id
        AND p_shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
$$;


--
-- Name: pending_shop_affiliation_decisions(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.pending_shop_affiliation_decisions(p_after uuid) RETURNS TABLE(event_id uuid, request_id uuid, account_id uuid, shop_id uuid, term_id uuid, actor_id uuid, membership_id uuid, authority_version bigint, shop_name text, owner_name text, accepted_at timestamp with time zone)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT e.id, e.request_id, e.account_id, e.shop_id, e.term_id, e.actor_id, e.membership_id,
        e.authority_version, e.shop_name, e.owner_name, e.recorded_at
    FROM public.shop_affiliation_consent_events e
    WHERE has_table_privilege(session_user, 'public.shop_affiliation_consent_events', 'INSERT')
        AND e.kind = 'Accepted' AND e.id > p_after
        AND NOT EXISTS (SELECT FROM public.shop_affiliation_delivery_receipts r WHERE r.event_id = e.id)
    ORDER BY e.id LIMIT 20
$$;


--
-- Name: pending_shop_trial_consents(uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.pending_shop_trial_consents(p_after uuid) RETURNS TABLE(event_id uuid, payload text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT c.event_id, c.payload::text FROM public.shop_trial_consents c
    WHERE has_table_privilege(session_user, 'public.shop_trial_consents', 'INSERT')
        AND c.event_id > p_after
        AND NOT EXISTS (SELECT FROM public.shop_trial_receipts r WHERE r.event_id = c.event_id)
    ORDER BY c.event_id LIMIT 20
$$;


--
-- Name: preserve_identity_business_history(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.preserve_identity_business_history() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog', 'public'
    AS $$
BEGIN
    NEW.requires_operator_recovery := OLD.requires_operator_recovery OR NEW.requires_operator_recovery;
    RETURN NEW;
END;
$$;


--
-- Name: protect_return_settlement_evidence(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.protect_return_settlement_evidence() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.refund_confirmed IS NOT NULL OR OLD.void_settlement_evidence IS NOT NULL THEN
            RAISE EXCEPTION 'Return settlement evidence must be retained' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;
    IF OLD.refund_confirmed IS DISTINCT FROM NEW.refund_confirmed OR OLD.refund_tender IS DISTINCT FROM NEW.refund_tender OR
        (OLD.void_settlement_evidence IS NOT NULL AND OLD.void_settlement_evidence IS DISTINCT FROM NEW.void_settlement_evidence) OR
        (NEW.void_settlement_evidence IS NOT NULL AND (NOT NEW.is_voided OR (OLD.void_settlement_evidence IS NULL AND OLD.is_voided))) THEN
        RAISE EXCEPTION 'Return settlement evidence is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: protect_source_settlement_evidence(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.protect_source_settlement_evidence() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.settlement_evidence IS NOT NULL THEN
            RAISE EXCEPTION 'Source settlement evidence must be retained' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;
    IF OLD.settlement_evidence IS DISTINCT FROM NEW.settlement_evidence THEN
        RAISE EXCEPTION 'Source settlement evidence is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END $$;


--
-- Name: reduction_fence_shops(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.reduction_fence_shops(account uuid, term uuid) RETURNS TABLE(shop_id uuid, affiliation_id uuid, state text, starts_at timestamp with time zone, access_ends_at timestamp with time zone, capacity integer, members integer, active_members integer, seat_hash text, suspension_mode text, closed boolean, account_restriction text)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT s.shop_id, s.affiliation_request_id, s.state, s.starts_at, s.access_ends_at,
        coalesce((SELECT (convert_from(r.command, 'UTF8')::jsonb->>'capacity')::int
            FROM public.applied_shop_capacity a
            JOIN public.capacity_reservation_receipts r
                ON r.event_id = a.applied_event AND r.account_id = a.account_id
            WHERE a.shop_id = s.shop_id AND a.account_id = s.account_id AND a.term_id = s.term_id
                AND a.affiliation_id = s.affiliation_request_id
            ORDER BY a.revision DESC LIMIT 1),
            s.shop_capacity) AS capacity,
        coalesce((SELECT a.members FROM public.applied_shop_capacity a
            WHERE a.shop_id = s.shop_id AND a.account_id = s.account_id AND a.term_id = s.term_id
                AND a.affiliation_id = s.affiliation_request_id ORDER BY a.revision DESC LIMIT 1),
            s.member_allowance) AS members,
        m.active_members, m.seat_hash, s.suspension_mode, (s.state = 'Closed') AS closed,
        coalesce((SELECT ar.restriction FROM public.account_license_restrictions ar WHERE ar.account_id = account), 'Unrestricted') AS account_restriction
    FROM public.shop_license_states s
    CROSS JOIN LATERAL (
        SELECT count(*) FILTER (WHERE sm.status = 'Active')::int AS active_members,
            encode(sha256(convert_to(coalesce(string_agg(sm.id::text || '/' || sm.authority_version::text, ','
                ORDER BY sm.id), ''), 'UTF8')), 'hex') AS seat_hash
        FROM public.shop_memberships sm WHERE sm.shop_id = s.shop_id
    ) m
    JOIN public.shop_affiliation_consent_events accepted ON accepted.shop_id = s.shop_id AND accepted.account_id = s.account_id
        AND accepted.request_id = s.affiliation_request_id AND accepted.kind = 'Accepted'
    JOIN public.shop_affiliation_delivery_receipts receipt ON receipt.event_id = accepted.id AND receipt.status = 'Applied'
    WHERE NOT EXISTS (SELECT FROM public.shop_affiliation_consent_events pending
        LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = pending.id
        WHERE pending.account_id = account AND pending.term_id = term AND pending.kind = 'Accepted' AND r.status IS NULL)
        AND s.term_id = term AND s.account_id = account AND s.basis = 'Paid'
        AND account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
        AND EXISTS (SELECT FROM public.entitlement_request_uses u WHERE u.replay_id =
            nullif(current_setting('app.entitlement_replay_id', true), '')::uuid AND u.xmin = pg_current_xact_id()::xid)
    ORDER BY s.shop_id
$$;


--
-- Name: reduction_observe_shops(uuid, uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.reduction_observe_shops(account uuid, shop_ids uuid[]) RETURNS TABLE(shop_id uuid, affiliation_id uuid, active_members integer, seat_hash text, has_pending_affiliation boolean)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT requested.shop_id,
        accepted.request_id AS affiliation_id,
        coalesce(m.active_members, 0) AS active_members,
        coalesce(m.seat_hash, encode(sha256(''::bytea), 'hex')) AS seat_hash,
        EXISTS (SELECT FROM public.shop_affiliation_consent_events pending
            LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = pending.id
            WHERE pending.account_id = account AND pending.shop_id = requested.shop_id
                AND pending.kind = 'Accepted' AND r.status IS NULL) AS has_pending_affiliation
    FROM unnest(shop_ids) AS requested(shop_id)
    LEFT JOIN LATERAL (
        SELECT count(*) FILTER (WHERE sm.status = 'Active')::int AS active_members,
            encode(sha256(convert_to(coalesce(string_agg(sm.id::text || '/' || sm.authority_version::text, ','
                ORDER BY sm.id), ''), 'UTF8')), 'hex') AS seat_hash
        FROM public.shop_memberships sm WHERE sm.shop_id = requested.shop_id
    ) m ON true
    LEFT JOIN LATERAL (
        SELECT e.request_id FROM public.shop_affiliation_consent_events e
        JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id AND r.status = 'Applied'
        WHERE e.shop_id = requested.shop_id AND e.account_id = account AND e.kind = 'Accepted'
        ORDER BY e.recorded_at DESC, e.id DESC LIMIT 1
    ) accepted ON true
    WHERE account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
        AND EXISTS (SELECT FROM public.entitlement_request_uses u WHERE u.replay_id =
            nullif(current_setting('app.entitlement_replay_id', true), '')::uuid AND u.xmin = pg_current_xact_id()::xid)
    ORDER BY requested.shop_id
$$;


--
-- Name: replacement_outgoing_released(uuid, timestamp with time zone); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.replacement_outgoing_released(change_id uuid, at_time timestamp with time zone) RETURNS boolean
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE consent public.coverage_replacement_consent_events%ROWTYPE;
        prior_scope text := current_setting('app.active_shop_id',true);
        released bool;
BEGIN
    SELECT * INTO consent FROM public.coverage_replacement_consent_events e
        WHERE e.replacement_id=change_id AND e.shop_id=nullif(prior_scope,'')::uuid AND e.kind='Accepted';
    IF NOT FOUND OR consent.effective_at>at_time THEN RETURN false; END IF;
    PERFORM set_config('app.active_shop_id',consent.outgoing_shop_id::text,true);
    SELECT EXISTS(SELECT FROM public.shop_license_states s JOIN public.license_grant_evidence e ON e.event_id=s.evidence_id
        WHERE s.shop_id=consent.outgoing_shop_id AND s.account_id=consent.account_id
            AND convert_from(e.payload,'UTF8')::jsonb->'shop'->>'releasedReplacementId'=consent.replacement_id::text
            AND (s.state NOT IN ('Active','Grace') OR s.access_ends_at<=at_time))
        AND NOT EXISTS(SELECT FROM public.offline_license_permits p WHERE p.shop_id=consent.outgoing_shop_id
            AND p.valid_until>at_time) INTO released;
    PERFORM set_config('app.active_shop_id',prior_scope,true);
    RETURN released;
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
-- Name: retain_shop_affiliation_evidence(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.retain_shop_affiliation_evidence() RETURNS trigger
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
BEGIN
    RAISE EXCEPTION 'Shop affiliation evidence is immutable' USING ERRCODE = '55000';
END $$;


--
-- Name: sale_returns_business_date_default(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sale_returns_business_date_default() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.business_time_zone IS NULL THEN
        NEW.business_time_zone := 'Asia/Kolkata';
    END IF;
    IF NEW.business_date IS NULL THEN
        NEW.business_date := (NEW.processed_at AT TIME ZONE NEW.business_time_zone)::date;
    END IF;
    IF NEW.voided_at IS NOT NULL AND NEW.voided_business_date IS NULL THEN
        NEW.voided_business_date := (NEW.voided_at AT TIME ZONE NEW.business_time_zone)::date;
    END IF;
    RETURN NEW;
END
$$;


--
-- Name: sales_business_date_default(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sales_business_date_default() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.business_time_zone IS NULL THEN
        NEW.business_time_zone := 'Asia/Kolkata';
    END IF;
    IF NEW.business_date IS NULL THEN
        NEW.business_date := (NEW.sold_at AT TIME ZONE NEW.business_time_zone)::date;
    END IF;
    RETURN NEW;
END
$$;


--
-- Name: take_merchant_licence_fence(uuid, uuid, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.take_merchant_licence_fence(p_account_id uuid, p_shop_id uuid, p_shared boolean) RETURNS void
    LANGUAGE plpgsql
    SET search_path TO 'pg_catalog'
    AS $$
DECLARE
    fence_key bigint;
BEGIN
    -- Preserve the projection writer's original keys for rolling upgrades.
    -- Account before shop. The shop key also fences first delivery when no projection row exists.
    FOREACH fence_key IN ARRAY ARRAY[
        hashtextextended('a' || p_account_id::text, 859),
        hashtextextended('s' || p_shop_id::text, 859)
    ] LOOP
        IF fence_key IS NOT NULL THEN
            IF p_shared THEN
                PERFORM pg_advisory_xact_lock_shared(fence_key);
            ELSE
                PERFORM pg_advisory_xact_lock(fence_key);
            END IF;
        END IF;
    END LOOP;
END $$;


--
-- Name: till_guard_closed_session(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.till_guard_closed_session() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF TG_OP = 'DELETE' OR OLD.state = 2 THEN
        RAISE EXCEPTION 'A closed till session is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: till_guard_source_session(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.till_guard_source_session() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE session_state integer;
BEGIN
    SELECT state INTO session_state FROM till_sessions
        WHERE shop_id = NEW.shop_id AND id = NEW.session_id FOR NO KEY UPDATE;
    IF session_state IS DISTINCT FROM 1 THEN
        RAISE EXCEPTION 'An open till session is required' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: till_reject_fact_mutation(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.till_reject_fact_mutation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    RAISE EXCEPTION 'Till facts are immutable' USING ERRCODE = '23514';
END;
$$;


--
-- Name: transfer_source_cutoff(uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.transfer_source_cutoff(shop uuid, affiliation uuid) RETURNS timestamp with time zone
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog'
    AS $$
    SELECT min(p.cutover_at) FROM public.merchant_transfer_decisions p WHERE p.shop_id=shop
        AND p.affiliation_id=affiliation AND p.outcome='Prepared' AND NOT EXISTS (
            SELECT FROM public.merchant_transfer_decisions e WHERE e.transfer_id=p.transfer_id AND e.outcome IN ('Aborted','Refused'))
$$;



--
-- Name: account_license_restrictions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.account_license_restrictions (
    account_id uuid NOT NULL,
    revision bigint NOT NULL,
    upstream_verified_at timestamp with time zone NOT NULL,
    restriction text NOT NULL,
    released_revision bigint,
    evidence_id uuid NOT NULL,
    suspension_effective_at timestamp with time zone,
    suspension_explanation text,
    previous_release_revision bigint,
    CONSTRAINT coherent_account_suspension CHECK ((((suspension_effective_at IS NULL) AND (suspension_explanation IS NULL)) OR ((suspension_effective_at IS NOT NULL) AND isfinite(suspension_effective_at) AND (suspension_explanation IS NOT NULL) AND ((length(suspension_explanation) >= 1) AND (length(suspension_explanation) <= 500)) AND (restriction = ANY (ARRAY['Trading'::text, 'Security'::text]))))),
    CONSTRAINT coherent_previous_release CHECK (((previous_release_revision IS NULL) OR ((previous_release_revision > 0) AND (previous_release_revision < revision) AND (restriction = ANY (ARRAY['Trading'::text, 'Security'::text]))))),
    CONSTRAINT valid_account_license_restriction CHECK (((revision > 0) AND isfinite(upstream_verified_at) AND (restriction = ANY (ARRAY['Unrestricted'::text, 'Trading'::text, 'Security'::text])) AND ((released_revision IS NULL) OR ((restriction = 'Unrestricted'::text) AND ((released_revision >= 1) AND (released_revision <= (revision - 1)))))))
);

ALTER TABLE ONLY public.account_license_restrictions FORCE ROW LEVEL SECURITY;


--
-- Name: applied_shop_capacity; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.applied_shop_capacity (
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    affiliation_id uuid NOT NULL,
    revision bigint NOT NULL,
    applied_event uuid NOT NULL,
    members integer NOT NULL,
    paid_through timestamp with time zone NOT NULL,
    CONSTRAINT applied_shop_capacity_members_check CHECK (((members >= 1) AND (members <= 100000))),
    CONSTRAINT applied_shop_capacity_paid_through_check CHECK (isfinite(paid_through)),
    CONSTRAINT applied_shop_capacity_revision_check CHECK ((revision > 0))
);

ALTER TABLE ONLY public.applied_shop_capacity FORCE ROW LEVEL SECURITY;


--
-- Name: applied_shop_reductions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.applied_shop_reductions (
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    predecessor_term_id uuid NOT NULL,
    successor_term_id uuid NOT NULL,
    affiliation_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    generation bigint NOT NULL,
    applied_event uuid NOT NULL,
    members integer NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    paid_through timestamp with time zone NOT NULL,
    CONSTRAINT applied_shop_reductions_check CHECK ((isfinite(paid_through) AND (paid_through > starts_at))),
    CONSTRAINT applied_shop_reductions_generation_check CHECK ((generation > 0)),
    CONSTRAINT applied_shop_reductions_members_check CHECK (((members >= 1) AND (members <= 100000))),
    CONSTRAINT applied_shop_reductions_starts_at_check CHECK (isfinite(starts_at))
);

ALTER TABLE ONLY public.applied_shop_reductions FORCE ROW LEVEL SECURITY;


--
-- Name: auth_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.auth_sessions (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    security_version bigint NOT NULL,
    recovery_epoch character varying(64) NOT NULL,
    active_shop_id uuid,
    scope_version bigint NOT NULL,
    device_label character varying(64),
    user_agent character varying(256),
    created_from_ip character varying(64),
    last_seen_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    revocation_reason character varying(64),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: bank_accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.bank_accounts (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    bank_name character varying(120) NOT NULL,
    account_number character varying(50) NOT NULL,
    account_type character varying(16),
    ifsc_code character varying(20),
    account_holder_name character varying(120),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.bank_accounts FORCE ROW LEVEL SECURITY;


--
-- Name: batch_disposition_changes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batch_disposition_changes (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    root_change_id uuid,
    action character varying(20) NOT NULL,
    quantity numeric(18,3) NOT NULL,
    source_kind character varying(30) NOT NULL,
    source_id uuid NOT NULL,
    source_line_id uuid NOT NULL,
    stock_movement_id uuid,
    inventory_adjustment_id uuid,
    expiry_date date,
    manufacturing_date date,
    reason_code character varying(80) NOT NULL,
    reason character varying(500) NOT NULL,
    policy_json jsonb NOT NULL,
    business_date date NOT NULL,
    actor_user_id uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_batch_disposition_action CHECK ((((root_change_id IS NULL) AND ((action)::text = 'Quarantine'::text) AND (stock_movement_id IS NOT NULL) AND (inventory_adjustment_id IS NULL)) OR ((root_change_id IS NOT NULL) AND ((((action)::text = 'Release'::text) AND (stock_movement_id IS NULL) AND (inventory_adjustment_id IS NULL)) OR (((action)::text = 'Dispose'::text) AND (stock_movement_id IS NOT NULL) AND (inventory_adjustment_id IS NOT NULL)) OR (((action)::text = 'Reverse'::text) AND (stock_movement_id IS NOT NULL) AND (inventory_adjustment_id IS NULL)))))),
    CONSTRAINT ck_batch_disposition_cause CHECK (((reason_code)::text = ANY ((ARRAY['Expiry.Expired'::character varying, 'Expiry.RequiredDateMissing'::character varying])::text[]))),
    CONSTRAINT ck_batch_disposition_quantity CHECK ((quantity > (0)::numeric)),
    CONSTRAINT ck_batch_disposition_reason CHECK (((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500))),
    CONSTRAINT ck_batch_disposition_source CHECK ((((source_kind)::text = ANY ((ARRAY['SaleReturn'::character varying, 'SaleCancellation'::character varying])::text[])) AND (source_id <> '00000000-0000-0000-0000-000000000000'::uuid) AND (source_line_id <> '00000000-0000-0000-0000-000000000000'::uuid)))
);

ALTER TABLE ONLY public.batch_disposition_changes FORCE ROW LEVEL SECURITY;


--
-- Name: batch_expiry_revisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batch_expiry_revisions (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    previous_expiry_date date,
    new_expiry_date date,
    previous_manufacturing_date date,
    new_manufacturing_date date,
    previous_batch_version bigint NOT NULL,
    catalog_version bigint NOT NULL,
    actor_user_id uuid NOT NULL,
    reason character varying(500) NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_batch_expiry_revisions_dates CHECK (((new_expiry_date IS NULL) OR (new_manufacturing_date IS NULL) OR (new_manufacturing_date <= new_expiry_date))),
    CONSTRAINT ck_batch_expiry_revisions_reason CHECK (((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500))),
    CONSTRAINT ck_batch_expiry_revisions_version CHECK (((catalog_version > 1) AND (previous_batch_version > 0)))
);

ALTER TABLE ONLY public.batch_expiry_revisions FORCE ROW LEVEL SECURITY;


--
-- Name: batch_price_revisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batch_price_revisions (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    previous_mrp numeric(18,2) NOT NULL,
    new_mrp numeric(18,2) NOT NULL,
    previous_sales_price numeric(18,2) NOT NULL,
    new_sales_price numeric(18,2) NOT NULL,
    previous_tax_rate_percent numeric(5,2) NOT NULL,
    new_tax_rate_percent numeric(5,2) NOT NULL,
    previous_tax_included boolean NOT NULL,
    new_tax_included boolean NOT NULL,
    reason character varying(500),
    performed_at timestamp with time zone NOT NULL,
    performed_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_batch_price_revisions_sales_lte_mrp CHECK ((new_sales_price <= new_mrp)),
    CONSTRAINT ck_batch_price_revisions_tax_rate_range CHECK (((new_tax_rate_percent >= (0)::numeric) AND (new_tax_rate_percent <= (100)::numeric)))
);

ALTER TABLE ONLY public.batch_price_revisions FORCE ROW LEVEL SECURITY;


--
-- Name: cache_entries; Type: TABLE; Schema: public; Owner: -
--

CREATE UNLOGGED TABLE public.cache_entries (
    id character varying(449) NOT NULL COLLATE pg_catalog."C",
    value bytea NOT NULL,
    expiresattime timestamp with time zone NOT NULL,
    slidingexpirationinseconds bigint,
    absoluteexpiration timestamp with time zone
);


--
-- Name: capacity_reservation_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.capacity_reservation_receipts (
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    order_id uuid NOT NULL,
    event_id uuid NOT NULL,
    phase text NOT NULL,
    revision bigint NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    digest text NOT NULL,
    fence_hash text,
    outcome text NOT NULL,
    command bytea NOT NULL,
    assertion text NOT NULL,
    public_key text NOT NULL,
    CONSTRAINT capacity_reservation_receipts_check CHECK ((((phase = 'CapacityReserve'::text) OR (outcome <> 'Reserved'::text)) AND ((phase = 'CapacityConsume'::text) OR (outcome <> 'Applied'::text)))),
    CONSTRAINT capacity_reservation_receipts_command_check CHECK (((octet_length(command) >= 1) AND (octet_length(command) <= 8192))),
    CONSTRAINT capacity_reservation_receipts_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT capacity_reservation_receipts_expires_at_check CHECK (isfinite(expires_at)),
    CONSTRAINT capacity_reservation_receipts_outcome_check CHECK ((outcome = ANY (ARRAY['Reserved'::text, 'Applied'::text, 'NeedsReview'::text]))),
    CONSTRAINT capacity_reservation_receipts_phase_check CHECK ((phase = ANY (ARRAY['CapacityReserve'::text, 'CapacityConsume'::text]))),
    CONSTRAINT capacity_reservation_receipts_revision_check CHECK ((revision > 0))
);

ALTER TABLE ONLY public.capacity_reservation_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_exception_consent_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_exception_consent_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    exception_id uuid NOT NULL,
    proposal_revision bigint NOT NULL,
    terms_digest text NOT NULL,
    leg text NOT NULL,
    affiliation_request_id uuid NOT NULL,
    term_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    operation_key uuid NOT NULL,
    kind text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    requested_early_start_at timestamp with time zone NOT NULL,
    paid_cutover_at timestamp with time zone NOT NULL,
    normal_renewal_at timestamp with time zone NOT NULL,
    overlap_disposition text NOT NULL,
    member_allowance integer NOT NULL,
    active_members integer,
    seat_hash text,
    CONSTRAINT coverage_exception_consent_event_requested_early_start_at_check CHECK (isfinite(requested_early_start_at)),
    CONSTRAINT coverage_exception_consent_events_active_members_check CHECK (((active_members IS NULL) OR (active_members >= 1))),
    CONSTRAINT coverage_exception_consent_events_authority_version_check CHECK ((authority_version >= 0)),
    CONSTRAINT coverage_exception_consent_events_check CHECK ((isfinite(expires_at) AND ((kind = 'Revoked'::text) OR (recorded_at < expires_at)))),
    CONSTRAINT coverage_exception_consent_events_check1 CHECK (((requested_early_start_at <= paid_cutover_at) AND (paid_cutover_at < normal_renewal_at))),
    CONSTRAINT coverage_exception_consent_events_check2 CHECK (((kind = 'Accepted'::text) = (active_members IS NOT NULL))),
    CONSTRAINT coverage_exception_consent_events_check3 CHECK (((kind = 'Accepted'::text) = (seat_hash IS NOT NULL))),
    CONSTRAINT coverage_exception_consent_events_check4 CHECK (((leg <> 'Incoming'::text) OR (active_members IS NULL) OR (active_members <= member_allowance))),
    CONSTRAINT coverage_exception_consent_events_kind_check CHECK ((kind = ANY (ARRAY['Granted'::text, 'Revoked'::text, 'Accepted'::text]))),
    CONSTRAINT coverage_exception_consent_events_leg_check CHECK ((leg = ANY (ARRAY['Outgoing'::text, 'Incoming'::text]))),
    CONSTRAINT coverage_exception_consent_events_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT coverage_exception_consent_events_normal_renewal_at_check CHECK (isfinite(normal_renewal_at)),
    CONSTRAINT coverage_exception_consent_events_overlap_disposition_check CHECK ((overlap_disposition = ANY (ARRAY['Wait'::text, 'CourtesyBridge'::text]))),
    CONSTRAINT coverage_exception_consent_events_paid_cutover_at_check CHECK (isfinite(paid_cutover_at)),
    CONSTRAINT coverage_exception_consent_events_proposal_revision_check CHECK ((proposal_revision > 0)),
    CONSTRAINT coverage_exception_consent_events_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT coverage_exception_consent_events_seat_hash_check CHECK (((seat_hash IS NULL) OR (seat_hash ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT coverage_exception_consent_events_terms_digest_check CHECK ((terms_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.coverage_exception_consent_events FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_replacement_activations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_replacement_activations (
    replacement_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    event_id uuid NOT NULL,
    activated_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY public.coverage_replacement_activations FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_replacement_consent_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_replacement_consent_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    replacement_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    term_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    operation_key uuid NOT NULL,
    kind text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    outgoing_shop_id uuid NOT NULL,
    member_allowance integer NOT NULL,
    active_members integer,
    seat_hash text,
    CONSTRAINT coverage_replacement_consent_events_check CHECK (((kind = 'Revoked'::text) OR (recorded_at < expires_at))),
    CONSTRAINT coverage_replacement_consent_events_check1 CHECK ((effective_at <= expires_at)),
    CONSTRAINT coverage_replacement_consent_events_check2 CHECK (((kind = 'Accepted'::text) = (active_members IS NOT NULL))),
    CONSTRAINT coverage_replacement_consent_events_check3 CHECK (((kind = 'Accepted'::text) = (seat_hash IS NOT NULL))),
    CONSTRAINT coverage_replacement_consent_events_check4 CHECK (((active_members >= 1) AND (active_members <= member_allowance))),
    CONSTRAINT coverage_replacement_consent_events_kind_check CHECK ((kind = ANY (ARRAY['Granted'::text, 'Revoked'::text, 'Accepted'::text]))),
    CONSTRAINT coverage_replacement_consent_events_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT coverage_replacement_consent_events_seat_hash_check CHECK (((length(seat_hash) = 64) AND (seat_hash !~ '[^0-9a-f]'::text)))
);

ALTER TABLE ONLY public.coverage_replacement_consent_events FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_slot_activations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_slot_activations (
    assignment_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    event_id uuid NOT NULL,
    activated_at timestamp with time zone NOT NULL
);

ALTER TABLE ONLY public.coverage_slot_activations FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_slot_consent_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_slot_consent_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    assignment_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    term_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    operation_key uuid NOT NULL,
    kind text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    coverage_ends_at timestamp with time zone NOT NULL,
    member_allowance integer NOT NULL,
    active_members integer,
    seat_hash text,
    CONSTRAINT coverage_slot_consent_events_check CHECK ((recorded_at < expires_at)),
    CONSTRAINT coverage_slot_consent_events_check1 CHECK ((expires_at <= coverage_ends_at)),
    CONSTRAINT coverage_slot_consent_events_check2 CHECK (((kind = 'Accepted'::text) = (active_members IS NOT NULL))),
    CONSTRAINT coverage_slot_consent_events_check3 CHECK (((kind = 'Accepted'::text) = (seat_hash IS NOT NULL))),
    CONSTRAINT coverage_slot_consent_events_check4 CHECK (((active_members >= 1) AND (active_members <= member_allowance))),
    CONSTRAINT coverage_slot_consent_events_kind_check CHECK ((kind = ANY (ARRAY['Granted'::text, 'Revoked'::text, 'Accepted'::text]))),
    CONSTRAINT coverage_slot_consent_events_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT coverage_slot_consent_events_seat_hash_check CHECK (((length(seat_hash) = 64) AND (seat_hash !~ '[^0-9a-f]'::text)))
);

ALTER TABLE ONLY public.coverage_slot_consent_events FORCE ROW LEVEL SECURITY;


--
-- Name: coverage_transfer_consent_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.coverage_transfer_consent_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    transfer_id uuid NOT NULL,
    proposal_revision bigint NOT NULL,
    terms_digest text NOT NULL,
    affiliation_request_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    operation_key uuid NOT NULL,
    request_hash text NOT NULL,
    generation integer NOT NULL,
    kind text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT coverage_transfer_consent_events_authority_version_check CHECK ((authority_version >= 0)),
    CONSTRAINT coverage_transfer_consent_events_check CHECK ((account_id <> destination_account_id)),
    CONSTRAINT coverage_transfer_consent_events_generation_check CHECK ((generation > 0)),
    CONSTRAINT coverage_transfer_consent_events_kind_check CHECK ((kind = ANY (ARRAY['Accepted'::text, 'Revoked'::text]))),
    CONSTRAINT coverage_transfer_consent_events_proposal_revision_check CHECK ((proposal_revision > 0)),
    CONSTRAINT coverage_transfer_consent_events_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT coverage_transfer_consent_events_request_hash_check CHECK ((request_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT coverage_transfer_consent_events_terms_digest_check CHECK ((terms_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.coverage_transfer_consent_events FORCE ROW LEVEL SECURITY;


--
-- Name: credit_note_redemptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.credit_note_redemptions (
    id uuid NOT NULL,
    credit_note_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_id uuid NOT NULL,
    amount numeric(18,2) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_credit_note_redemptions_amount_positive CHECK ((amount > (0)::numeric))
);

ALTER TABLE ONLY public.credit_note_redemptions FORCE ROW LEVEL SECURITY;


--
-- Name: credit_notes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.credit_notes (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_return_id uuid NOT NULL,
    code character varying(50) NOT NULL,
    original_amount numeric(18,2) NOT NULL,
    available_balance numeric(18,2) NOT NULL,
    reason character varying(1000) NOT NULL,
    expires_at timestamp with time zone,
    is_voided boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    void_reason character varying(500),
    linked_customer_id uuid,
    CONSTRAINT ck_credit_notes_available_balance_non_negative CHECK ((available_balance >= (0)::numeric)),
    CONSTRAINT ck_credit_notes_balance_le_original CHECK ((available_balance <= original_amount)),
    CONSTRAINT ck_credit_notes_original_amount_positive CHECK ((original_amount > (0)::numeric))
);

ALTER TABLE ONLY public.credit_notes FORCE ROW LEVEL SECURITY;


--
-- Name: customer_ledger_entries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_ledger_entries (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    sale_id uuid,
    entry_type character varying(30) NOT NULL,
    amount numeric(18,2) NOT NULL,
    entry_date date NOT NULL,
    notes character varying(255),
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_customer_ledger_entries_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT ck_customer_ledger_entries_sale_by_type CHECK (((((entry_type)::text = ANY ((ARRAY['SALE_DUE'::character varying, 'SALE_CANCELLATION_CREDIT'::character varying])::text[])) AND (sale_id IS NOT NULL)) OR (((entry_type)::text = ANY ((ARRAY['PAYMENT_RECEIVED'::character varying, 'PAYMENT_REVERSAL'::character varying])::text[])) AND (sale_id IS NULL)) OR ((entry_type)::text = ANY ((ARRAY['RETURN_CREDIT'::character varying, 'RETURN_CREDIT_REVERSAL'::character varying])::text[]))))
);

ALTER TABLE ONLY public.customer_ledger_entries FORCE ROW LEVEL SECURITY;


--
-- Name: customers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customers (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    name character varying(180) NOT NULL,
    phone_number character varying(32) NOT NULL,
    address character varying(320),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    credit_limit numeric(10,2) DEFAULT 0.0 NOT NULL,
    gstin character varying(15),
    state_code character varying(2),
    credit_policy_mode integer DEFAULT 3 NOT NULL,
    credit_terms_days integer,
    CONSTRAINT ck_customers_credit_policy_mode CHECK ((credit_policy_mode = ANY (ARRAY[1, 2, 3]))),
    CONSTRAINT ck_customers_credit_terms_days CHECK (((credit_terms_days IS NULL) OR ((credit_terms_days >= 0) AND (credit_terms_days <= 365))))
);

ALTER TABLE ONLY public.customers FORCE ROW LEVEL SECURITY;


--
-- Name: discount_rules; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.discount_rules (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    rule_type character varying(32) NOT NULL,
    name character varying(200) NOT NULL,
    description character varying(1000),
    inventory_batch_id uuid,
    percentage numeric(5,2) NOT NULL,
    threshold_amount numeric(18,2),
    starts_at timestamp with time zone,
    ends_at timestamp with time zone,
    is_active boolean DEFAULT true NOT NULL,
    disabled_at timestamp with time zone,
    disabled_reason character varying(500),
    below_cost_confirmed boolean DEFAULT false NOT NULL,
    below_cost_confirmation_reason character varying(500),
    replaces_rule_id uuid,
    replaced_by_rule_id uuid,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_discount_rules_disabled_audit CHECK ((((is_active = true) AND (disabled_at IS NULL)) OR ((is_active = false) AND (disabled_at IS NOT NULL)))),
    CONSTRAINT ck_discount_rules_ends_after_starts CHECK (((starts_at IS NULL) OR (ends_at IS NULL) OR (ends_at > starts_at))),
    CONSTRAINT ck_discount_rules_percentage_range CHECK (((percentage > (0)::numeric) AND (percentage <= (100)::numeric))),
    CONSTRAINT ck_discount_rules_threshold_amount_positive CHECK (((threshold_amount IS NULL) OR (threshold_amount > (0)::numeric))),
    CONSTRAINT ck_discount_rules_threshold_required CHECK ((((rule_type)::text <> 'SaleThresholdPercentage'::text) OR (threshold_amount IS NOT NULL)))
);

ALTER TABLE ONLY public.discount_rules FORCE ROW LEVEL SECURITY;


--
-- Name: email_ownership_challenges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.email_ownership_challenges (
    id uuid NOT NULL,
    purpose character varying(32) NOT NULL,
    destination character varying(256) NOT NULL,
    token_hash character varying(64) NOT NULL,
    user_id uuid,
    security_version bigint NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);


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
    CONSTRAINT entitlement_messages_kind_check CHECK ((kind = ANY (ARRAY['Snapshot'::text, 'Acknowledgement'::text, 'Reconciliation'::text, 'DeviceObservation'::text, 'CapacityReceipt'::text, 'ReductionReceipt'::text, 'CoverageSlotConsent'::text, 'CoverageReplacementConsent'::text, 'CoverageReplacementExceptionConsent'::text, 'CoverageReplacementExceptionReceipt'::text, 'CoverageTransferConsent'::text, 'TransferReceipt'::text]))),
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
    ack_confirmed_at timestamp with time zone,
    CONSTRAINT entitlement_outbox_ack_confirmed_at_check CHECK (isfinite(ack_confirmed_at)),
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
-- Name: entitlement_reconciliation_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_reconciliation_requests (
    shop_id uuid NOT NULL,
    event_id uuid NOT NULL,
    next_check_at timestamp with time zone NOT NULL,
    last_queued_at timestamp with time zone NOT NULL,
    CONSTRAINT entitlement_reconciliation_requests_last_queued_at_check CHECK (isfinite(last_queued_at)),
    CONSTRAINT entitlement_reconciliation_requests_next_check_at_check CHECK (isfinite(next_check_at))
);

ALTER TABLE ONLY public.entitlement_reconciliation_requests FORCE ROW LEVEL SECURITY;


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
-- Name: entitlement_snapshot_inbox; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.entitlement_snapshot_inbox (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    digest text NOT NULL,
    acknowledgement bytea NOT NULL,
    CONSTRAINT entitlement_snapshot_inbox_acknowledgement_check CHECK (((octet_length(acknowledgement) >= 1) AND (octet_length(acknowledgement) <= 8192))),
    CONSTRAINT entitlement_snapshot_inbox_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.entitlement_snapshot_inbox FORCE ROW LEVEL SECURITY;


--
-- Name: expense_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.expense_categories (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.expense_categories FORCE ROW LEVEL SECURITY;


--
-- Name: expenses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.expenses (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    category_id uuid NOT NULL,
    amount numeric(10,2) NOT NULL,
    paid_to character varying(255) NOT NULL,
    description character varying(500),
    expense_date date NOT NULL,
    actor_user_id uuid NOT NULL,
    is_voided boolean DEFAULT false NOT NULL,
    original_expense_id uuid,
    supplier_ledger_entry_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    treatment integer DEFAULT 0 NOT NULL,
    settlement_evidence jsonb,
    CONSTRAINT ck_expenses_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT ck_expenses_treatment CHECK ((treatment = ANY (ARRAY[0, 1, 2, 3])))
);

ALTER TABLE ONLY public.expenses FORCE ROW LEVEL SECURITY;


--
-- Name: expiry_policy_revisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.expiry_policy_revisions (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid,
    catalog_version bigint NOT NULL,
    mode integer,
    expiry_required boolean,
    sell_through_expiry_date boolean,
    warning_days integer,
    override_allowed boolean,
    actor_user_id uuid,
    reason character varying(500) NOT NULL,
    effective_at timestamp with time zone NOT NULL,
    pre_feature_baseline boolean NOT NULL,
    CONSTRAINT ck_expiry_policy_revisions_baseline CHECK ((((NOT pre_feature_baseline) AND (actor_user_id IS NOT NULL)) OR (pre_feature_baseline AND (catalog_version = 1) AND (item_id IS NULL) AND (mode = 0) AND (expiry_required = false) AND (sell_through_expiry_date = true) AND (warning_days = 7) AND (override_allowed = false) AND (actor_user_id IS NULL)))),
    CONSTRAINT ck_expiry_policy_revisions_reason CHECK (((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500))),
    CONSTRAINT ck_expiry_policy_revisions_settings CHECK (((catalog_version >= 1) AND (((item_id IS NOT NULL) AND (mode IS NULL) AND (expiry_required IS NULL) AND (sell_through_expiry_date IS NULL) AND (warning_days IS NULL) AND (override_allowed IS NULL)) OR ((mode IS NOT NULL) AND ((mode >= 0) AND (mode <= 2)) AND (expiry_required IS NOT NULL) AND (sell_through_expiry_date IS NOT NULL) AND (warning_days IS NOT NULL) AND ((warning_days >= 0) AND (warning_days <= 365)) AND (override_allowed IS NOT NULL)))))
);

ALTER TABLE ONLY public.expiry_policy_revisions FORCE ROW LEVEL SECURITY;


--
-- Name: hsn_cache; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.hsn_cache (
    id uuid NOT NULL,
    product_name character varying(500) NOT NULL,
    hsn_codes text[] NOT NULL,
    cached_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    tax_scenarios jsonb
);


--
-- Name: identity_mail; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.identity_mail (
    id uuid NOT NULL,
    challenge_id uuid,
    destination character varying(256) NOT NULL,
    encrypted_content text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    attempts integer NOT NULL,
    completed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    invitation_id uuid,
    password_reset_token_id uuid
);


--
-- Name: identity_sms; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.identity_sms (
    id uuid NOT NULL,
    challenge_id uuid,
    destination character varying(16) NOT NULL,
    encrypted_content text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    next_attempt_at timestamp with time zone NOT NULL,
    attempts integer NOT NULL,
    completed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: inventory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    quantity numeric(18,3) NOT NULL,
    reorder_level numeric(18,3) NOT NULL,
    max_level numeric(18,3) NOT NULL,
    last_updated_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_inventory_max_non_negative CHECK ((max_level >= (0)::numeric)),
    CONSTRAINT ck_inventory_quantity_non_negative CHECK ((quantity >= (0)::numeric)),
    CONSTRAINT ck_inventory_reorder_lte_max CHECK ((reorder_level <= max_level)),
    CONSTRAINT ck_inventory_reorder_non_negative CHECK ((reorder_level >= (0)::numeric))
);

ALTER TABLE ONLY public.inventory FORCE ROW LEVEL SECURITY;


--
-- Name: inventory_adjustments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory_adjustments (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    adjustment_number character varying(40) NOT NULL,
    direction character varying(16) NOT NULL,
    reason character varying(32) NOT NULL,
    quantity numeric(18,3) NOT NULL,
    unit_cost numeric(18,2) NOT NULL,
    cost_impact numeric(18,2) NOT NULL,
    batch_quantity_before numeric(18,3) NOT NULL,
    batch_quantity_after numeric(18,3) NOT NULL,
    inventory_quantity_before numeric(18,3) NOT NULL,
    inventory_quantity_after numeric(18,3) NOT NULL,
    performed_at timestamp with time zone NOT NULL,
    performed_by uuid NOT NULL,
    notes character varying(1000),
    is_voided boolean DEFAULT false NOT NULL,
    void_reason character varying(500),
    voided_by uuid,
    voided_at timestamp with time zone,
    reversal_stock_transaction_id uuid,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    disposition_change_id uuid,
    CONSTRAINT ck_inventory_adjustments_cost_impact_non_negative CHECK ((cost_impact >= (0)::numeric)),
    CONSTRAINT ck_inventory_adjustments_direction_reason CHECK (((((direction)::text = 'Decrease'::text) AND ((reason)::text = ANY ((ARRAY['Damaged'::character varying, 'Expired'::character varying, 'Stolen'::character varying, 'MissingLost'::character varying, 'StockCountCorrection'::character varying, 'OtherLoss'::character varying])::text[]))) OR (((direction)::text = 'Increase'::text) AND ((reason)::text = ANY ((ARRAY['FoundStock'::character varying, 'StockCountCorrection'::character varying, 'ReturnRestockCorrection'::character varying, 'OtherGain'::character varying])::text[]))))),
    CONSTRAINT ck_inventory_adjustments_other_reason_notes CHECK ((((reason)::text <> ALL ((ARRAY['OtherLoss'::character varying, 'OtherGain'::character varying])::text[])) OR ((notes IS NOT NULL) AND (length(btrim((notes)::text)) > 0)))),
    CONSTRAINT ck_inventory_adjustments_quantity_positive CHECK ((quantity > (0)::numeric)),
    CONSTRAINT ck_inventory_adjustments_quantity_snapshots CHECK (((((direction)::text = 'Decrease'::text) AND ((batch_quantity_before - quantity) = batch_quantity_after) AND ((inventory_quantity_before - quantity) = inventory_quantity_after)) OR (((direction)::text = 'Increase'::text) AND ((batch_quantity_before + quantity) = batch_quantity_after) AND ((inventory_quantity_before + quantity) = inventory_quantity_after)))),
    CONSTRAINT ck_inventory_adjustments_quantity_snapshots_non_negative CHECK (((batch_quantity_before >= (0)::numeric) AND (batch_quantity_after >= (0)::numeric) AND (inventory_quantity_before >= (0)::numeric) AND (inventory_quantity_after >= (0)::numeric))),
    CONSTRAINT ck_inventory_adjustments_unit_cost_non_negative CHECK ((unit_cost >= (0)::numeric)),
    CONSTRAINT ck_inventory_adjustments_void_audit CHECK ((((is_voided = false) AND (voided_at IS NULL) AND (voided_by IS NULL) AND (void_reason IS NULL) AND (reversal_stock_transaction_id IS NULL)) OR ((is_voided = true) AND (voided_at IS NOT NULL) AND (voided_by IS NOT NULL) AND (void_reason IS NOT NULL) AND (reversal_stock_transaction_id IS NOT NULL))))
);

ALTER TABLE ONLY public.inventory_adjustments FORCE ROW LEVEL SECURITY;


--
-- Name: inventory_batches; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory_batches (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    batch_number character varying(80) NOT NULL,
    quantity numeric(18,3) NOT NULL,
    original_quantity numeric(18,3) NOT NULL,
    cost_price numeric(18,2) NOT NULL,
    mrp numeric(18,2) NOT NULL,
    sales_price numeric(18,2) NOT NULL,
    tax_rate_percent numeric(5,2) NOT NULL,
    tax_included boolean DEFAULT false NOT NULL,
    expiry_date date,
    manufacturing_date date,
    supplier_id uuid,
    is_voided boolean DEFAULT false NOT NULL,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    purchase_tax_included boolean DEFAULT false NOT NULL,
    quarantined_quantity numeric(18,3) DEFAULT 0.0 NOT NULL,
    CONSTRAINT ck_inventory_batches_quantity_non_negative CHECK ((quantity >= (0)::numeric)),
    CONSTRAINT ck_inventory_batches_quarantine_range CHECK (((quarantined_quantity >= (0)::numeric) AND (quarantined_quantity <= quantity))),
    CONSTRAINT ck_inventory_batches_sales_lte_mrp CHECK ((sales_price <= mrp)),
    CONSTRAINT ck_inventory_batches_tax_rate_range CHECK (((tax_rate_percent >= (0)::numeric) AND (tax_rate_percent <= (100)::numeric)))
);

ALTER TABLE ONLY public.inventory_batches FORCE ROW LEVEL SECURITY;


--
-- Name: invoice_leases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invoice_leases (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    invoice_sequence_id uuid NOT NULL,
    fiscal_year_start integer NOT NULL,
    device_id character varying(120) NOT NULL,
    prefix character varying(32) NOT NULL,
    range_start integer NOT NULL,
    range_end integer NOT NULL,
    next_number integer NOT NULL,
    number_padding integer NOT NULL,
    reserved_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    reserved_by_session_id uuid,
    reserved_by_user_id uuid,
    seller_profile jsonb
);

ALTER TABLE ONLY public.invoice_leases FORCE ROW LEVEL SECURITY;


--
-- Name: invoice_sequences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invoice_sequences (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    fiscal_year_start integer NOT NULL,
    next_number integer NOT NULL,
    prefix character varying(32) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    seller_registration_key character varying(64) DEFAULT ''::character varying NOT NULL,
    series_code character varying(4) DEFAULT ''::character varying NOT NULL
);

ALTER TABLE ONLY public.invoice_sequences FORCE ROW LEVEL SECURITY;


--
-- Name: item_barcode_sequences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.item_barcode_sequences (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    next_number integer NOT NULL,
    prefix character varying(32) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.item_barcode_sequences FORCE ROW LEVEL SECURITY;


--
-- Name: items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.items (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    name character varying(180) NOT NULL,
    description character varying(1000),
    uom character varying(32) NOT NULL,
    barcode character varying(128) NOT NULL,
    is_active boolean NOT NULL,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    hsn_code character varying(20),
    default_tax_included boolean DEFAULT false NOT NULL,
    default_tax_rate_percent numeric(5,2) DEFAULT 0.0 NOT NULL,
    quantity_scale smallint DEFAULT 0 NOT NULL,
    unit_code character varying(8),
    unit_review_required boolean DEFAULT false NOT NULL,
    CONSTRAINT ck_items_quantity_scale CHECK (((quantity_scale >= 0) AND (quantity_scale <= 3)))
);

ALTER TABLE ONLY public.items FORCE ROW LEVEL SECURITY;


--
-- Name: license_grant_evidence; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.license_grant_evidence (
    event_id uuid NOT NULL,
    account_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    account_revision bigint,
    shop_revision bigint,
    account_upstream_verified_at timestamp with time zone,
    shop_upstream_verified_at timestamp with time zone,
    issued_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL,
    applied_at timestamp with time zone NOT NULL,
    key_id character varying(128) NOT NULL,
    public_key character varying(2048) NOT NULL,
    assertion character varying(4096) NOT NULL,
    digest character varying(64) NOT NULL,
    payload bytea NOT NULL,
    CONSTRAINT valid_license_grant_evidence CHECK ((((octet_length(payload) >= 1) AND (octet_length(payload) <= 8192)) AND ((digest)::text ~ '^[0-9a-f]{64}$'::text) AND isfinite(issued_at) AND isfinite(received_at) AND isfinite(applied_at) AND isfinite(account_upstream_verified_at) AND isfinite(shop_upstream_verified_at) AND (issued_at <= received_at) AND (received_at <= applied_at)))
);

ALTER TABLE ONLY public.license_grant_evidence FORCE ROW LEVEL SECURITY;


--
-- Name: license_history_review_observations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.license_history_review_observations (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    case_id uuid NOT NULL,
    offline_submission_id uuid,
    observed_at timestamp with time zone NOT NULL,
    reason_code character varying(128) NOT NULL,
    subreason character varying(64) NOT NULL,
    compared_field character varying(64),
    window_start timestamp with time zone,
    window_end timestamp with time zone,
    observed_account_revision bigint,
    observed_shop_revision bigint,
    source_completion_cutoff timestamp with time zone,
    competing_submission_id uuid,
    fingerprint character varying(64) NOT NULL,
    CONSTRAINT valid_license_history_observation CHECK (((btrim((reason_code)::text) <> ''::text) AND (btrim((subreason)::text) <> ''::text) AND ((compared_field IS NULL) OR (btrim((compared_field)::text) <> ''::text)) AND ((fingerprint)::text ~ '^[0-9a-f]{64}$'::text) AND isfinite(observed_at) AND ((window_start IS NULL) OR isfinite(window_start)) AND ((window_end IS NULL) OR isfinite(window_end)) AND ((window_start IS NULL) OR (window_end IS NULL) OR (window_start <= window_end)) AND ((observed_account_revision IS NULL) OR (observed_account_revision > 0)) AND ((observed_shop_revision IS NULL) OR (observed_shop_revision > 0)) AND ((source_completion_cutoff IS NULL) OR isfinite(source_completion_cutoff)) AND ((competing_submission_id IS NULL) OR (offline_submission_id IS NULL) OR (competing_submission_id <> offline_submission_id))))
);

ALTER TABLE ONLY public.license_history_review_observations FORCE ROW LEVEL SECURITY;


--
-- Name: merchant_coverage_exception_activations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merchant_coverage_exception_activations (
    exception_id uuid NOT NULL,
    leg text NOT NULL,
    generation bigint NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    event_id uuid NOT NULL,
    shop_revision bigint NOT NULL,
    activated_at timestamp with time zone NOT NULL,
    CONSTRAINT merchant_coverage_exception_activations_activated_at_check CHECK (isfinite(activated_at)),
    CONSTRAINT merchant_coverage_exception_activations_generation_check CHECK ((generation > 0)),
    CONSTRAINT merchant_coverage_exception_activations_leg_check CHECK ((leg = ANY (ARRAY['Outgoing'::text, 'IncomingPaid'::text, 'IncomingCourtesy'::text]))),
    CONSTRAINT merchant_coverage_exception_activations_shop_revision_check CHECK ((shop_revision > 0))
);

ALTER TABLE ONLY public.merchant_coverage_exception_activations FORCE ROW LEVEL SECURITY;


--
-- Name: merchant_coverage_exception_cutoffs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merchant_coverage_exception_cutoffs (
    exception_id uuid NOT NULL,
    generation bigint NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    cutoff_at timestamp with time zone NOT NULL,
    decision_event_id uuid NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT merchant_coverage_exception_cutoffs_cutoff_at_check CHECK (isfinite(cutoff_at)),
    CONSTRAINT merchant_coverage_exception_cutoffs_generation_check CHECK ((generation > 0)),
    CONSTRAINT merchant_coverage_exception_cutoffs_recorded_at_check CHECK (isfinite(recorded_at))
);

ALTER TABLE ONLY public.merchant_coverage_exception_cutoffs FORCE ROW LEVEL SECURITY;


--
-- Name: merchant_coverage_exception_decisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merchant_coverage_exception_decisions (
    event_id uuid NOT NULL,
    exception_id uuid NOT NULL,
    approval_id uuid NOT NULL,
    generation bigint NOT NULL,
    phase text NOT NULL,
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    outgoing_shop_id uuid NOT NULL,
    outgoing_affiliation_id uuid NOT NULL,
    incoming_shop_id uuid NOT NULL,
    incoming_affiliation_id uuid NOT NULL,
    requested_early_start_at timestamp with time zone NOT NULL,
    paid_cutover_at timestamp with time zone NOT NULL,
    normal_renewal_at timestamp with time zone NOT NULL,
    overlap_disposition text NOT NULL,
    member_allowance integer NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    digest text NOT NULL,
    permit_count integer NOT NULL,
    max_permit_valid_until timestamp with time zone,
    permit_digest text NOT NULL,
    fence_hash text NOT NULL,
    outcome text NOT NULL,
    reason text,
    command bytea NOT NULL,
    assertion text NOT NULL,
    public_key text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT merchant_coverage_exception_deci_requested_early_start_at_check CHECK (isfinite(requested_early_start_at)),
    CONSTRAINT merchant_coverage_exception_decisi_max_permit_valid_until_check CHECK (((max_permit_valid_until IS NULL) OR isfinite(max_permit_valid_until))),
    CONSTRAINT merchant_coverage_exception_decisions_check CHECK ((outgoing_shop_id <> incoming_shop_id)),
    CONSTRAINT merchant_coverage_exception_decisions_check1 CHECK (((requested_early_start_at <= paid_cutover_at) AND (paid_cutover_at < normal_renewal_at))),
    CONSTRAINT merchant_coverage_exception_decisions_check2 CHECK ((((phase = 'CoverageExceptionPrepare'::text) OR (outcome <> 'Prepared'::text)) AND ((phase = 'CoverageExceptionConsume'::text) OR (outcome <> 'Consumed'::text)) AND ((phase = 'CoverageExceptionAbort'::text) OR (outcome <> 'Aborted'::text)))),
    CONSTRAINT merchant_coverage_exception_decisions_command_check CHECK (((octet_length(command) >= 1) AND (octet_length(command) <= 8192))),
    CONSTRAINT merchant_coverage_exception_decisions_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT merchant_coverage_exception_decisions_expires_at_check CHECK (isfinite(expires_at)),
    CONSTRAINT merchant_coverage_exception_decisions_fence_hash_check CHECK ((fence_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT merchant_coverage_exception_decisions_generation_check CHECK ((generation > 0)),
    CONSTRAINT merchant_coverage_exception_decisions_member_allowance_check CHECK ((member_allowance > 0)),
    CONSTRAINT merchant_coverage_exception_decisions_normal_renewal_at_check CHECK (isfinite(normal_renewal_at)),
    CONSTRAINT merchant_coverage_exception_decisions_outcome_check CHECK ((outcome = ANY (ARRAY['Prepared'::text, 'Consumed'::text, 'Aborted'::text, 'Refused'::text]))),
    CONSTRAINT merchant_coverage_exception_decisions_overlap_disposition_check CHECK ((overlap_disposition = ANY (ARRAY['Wait'::text, 'CourtesyBridge'::text]))),
    CONSTRAINT merchant_coverage_exception_decisions_paid_cutover_at_check CHECK (isfinite(paid_cutover_at)),
    CONSTRAINT merchant_coverage_exception_decisions_permit_count_check CHECK ((permit_count >= 0)),
    CONSTRAINT merchant_coverage_exception_decisions_permit_digest_check CHECK ((permit_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT merchant_coverage_exception_decisions_phase_check CHECK ((phase = ANY (ARRAY['CoverageExceptionPrepare'::text, 'CoverageExceptionConsume'::text, 'CoverageExceptionAbort'::text]))),
    CONSTRAINT merchant_coverage_exception_decisions_reason_check CHECK (((reason IS NULL) OR ((length(reason) >= 1) AND (length(reason) <= 80)))),
    CONSTRAINT merchant_coverage_exception_decisions_recorded_at_check CHECK (isfinite(recorded_at))
);

ALTER TABLE ONLY public.merchant_coverage_exception_decisions FORCE ROW LEVEL SECURITY;


--
-- Name: merchant_transfer_decisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merchant_transfer_decisions (
    event_id uuid NOT NULL,
    transfer_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    source_account_id uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    affiliation_id uuid NOT NULL,
    cutover_at timestamp with time zone NOT NULL,
    paid_through_at timestamp with time zone NOT NULL,
    phase text NOT NULL,
    outcome text NOT NULL,
    digest text NOT NULL,
    command bytea NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT merchant_transfer_decisions_check CHECK ((destination_account_id <> source_account_id)),
    CONSTRAINT merchant_transfer_decisions_check1 CHECK ((paid_through_at >= cutover_at)),
    CONSTRAINT merchant_transfer_decisions_outcome_check CHECK ((outcome = ANY (ARRAY['Prepared'::text, 'Consumed'::text, 'Aborted'::text, 'Refused'::text]))),
    CONSTRAINT merchant_transfer_decisions_phase_check CHECK ((phase = ANY (ARRAY['TransferPrepare'::text, 'TransferConsume'::text, 'TransferAbort'::text])))
);

ALTER TABLE ONLY public.merchant_transfer_decisions FORCE ROW LEVEL SECURITY;


--
-- Name: shop_affiliation_consent_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_affiliation_consent_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    request_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    operation_key uuid NOT NULL,
    kind text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    term_id uuid,
    shop_name text NOT NULL,
    owner_name text NOT NULL,
    request_expires_at timestamp with time zone NOT NULL,
    CONSTRAINT shop_affiliation_consent_events_authority_version_check CHECK ((authority_version >= 0)),
    CONSTRAINT shop_affiliation_consent_events_check CHECK (((recorded_at < request_expires_at) OR (kind = 'Revoked'::text))),
    CONSTRAINT shop_affiliation_consent_events_kind_check CHECK ((kind = ANY (ARRAY['Granted'::text, 'Revoked'::text, 'Accepted'::text])))
);

ALTER TABLE ONLY public.shop_affiliation_consent_events FORCE ROW LEVEL SECURITY;


--
-- Name: shop_license_states; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_license_states (
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    enforcement_activated_at timestamp with time zone,
    revision bigint NOT NULL,
    upstream_verified_at timestamp with time zone NOT NULL,
    state text NOT NULL,
    term_id uuid,
    basis text,
    starts_at timestamp with time zone,
    access_ends_at timestamp with time zone,
    next_boundary timestamp with time zone,
    shop_capacity integer,
    member_allowance integer,
    evidence_id uuid NOT NULL,
    suspension_mode text,
    suspension_effective_at timestamp with time zone,
    suspension_explanation text,
    released_revision bigint,
    reopened_revision bigint,
    closure_revision bigint,
    CONSTRAINT coherent_shop_closure_revision CHECK (((closure_revision IS NULL) OR ((closure_revision > 1) AND (closure_revision <= revision) AND ((reopened_revision IS NULL) OR (closure_revision > reopened_revision))))),
    CONSTRAINT coherent_shop_release CHECK (((released_revision IS NULL) OR ((released_revision > 0) AND (released_revision < revision)))),
    CONSTRAINT coherent_shop_reopening CHECK (((reopened_revision IS NULL) OR ((reopened_revision > 0) AND (reopened_revision < revision)))),
    CONSTRAINT coherent_shop_suspension CHECK ((((suspension_mode IS NULL) AND (suspension_effective_at IS NULL) AND (suspension_explanation IS NULL)) OR ((suspension_mode IS NOT NULL) AND (suspension_mode = ANY (ARRAY['Trading'::text, 'Security'::text])) AND (suspension_effective_at IS NOT NULL) AND isfinite(suspension_effective_at) AND (suspension_explanation IS NOT NULL) AND ((length(suspension_explanation) >= 1) AND (length(suspension_explanation) <= 500))))),
    CONSTRAINT valid_shop_license_state CHECK (((revision > 0) AND isfinite(upstream_verified_at) AND isfinite(starts_at) AND isfinite(access_ends_at) AND isfinite(next_boundary) AND isfinite(enforcement_activated_at) AND (next_boundary > upstream_verified_at) AND (shop_capacity > 0) AND (member_allowance > 0) AND (state = ANY (ARRAY['PendingActivation'::text, 'Scheduled'::text, 'Active'::text, 'Grace'::text, 'Expired'::text, 'Suspended'::text, 'EvidenceIncomplete'::text, 'NeedsReview'::text, 'Closed'::text])) AND ((state <> ALL (ARRAY['Active'::text, 'Grace'::text])) OR ((term_id IS NOT NULL) AND (basis IS NOT NULL) AND (starts_at IS NOT NULL) AND (access_ends_at IS NOT NULL) AND (starts_at <= upstream_verified_at) AND (access_ends_at > upstream_verified_at)))))
);

ALTER TABLE ONLY public.shop_license_states FORCE ROW LEVEL SECURITY;


--
-- Name: merchant_entitlement_bindings; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.merchant_entitlement_bindings WITH (security_invoker='true') AS
 SELECT e.id,
    e.shop_id,
    e.account_id,
    e.request_id,
    e.kind
   FROM public.shop_affiliation_consent_events e
  WHERE (NOT (EXISTS ( SELECT
           FROM public.shop_license_states s
          WHERE ((s.shop_id = e.shop_id) AND (s.affiliation_request_id <> e.request_id)))))
UNION ALL
 SELECT d.transfer_id AS id,
    d.shop_id,
    d.destination_account_id AS account_id,
    d.transfer_id AS request_id,
    'Accepted'::text AS kind
   FROM (public.merchant_transfer_decisions d
     JOIN public.shop_license_states s ON (((s.shop_id = d.shop_id) AND (s.account_id = d.destination_account_id) AND (s.affiliation_request_id = d.transfer_id))))
  WHERE (d.outcome = 'Consumed'::text);


--
-- Name: offline_expiry_evidence; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.offline_expiry_evidence (
    id uuid NOT NULL,
    snapshot_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    catalog_version bigint NOT NULL,
    effective_policy jsonb NOT NULL,
    batch_facts jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_offline_expiry_evidence_version CHECK ((catalog_version > 0)),
    CONSTRAINT ck_offline_expiry_evidence_window CHECK ((expires_at = (captured_at + '7 days'::interval)))
);

ALTER TABLE ONLY public.offline_expiry_evidence FORCE ROW LEVEL SECURITY;


--
-- Name: offline_license_grants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.offline_license_grants (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    lease_id uuid NOT NULL,
    account_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    account_revision bigint NOT NULL,
    shop_revision bigint NOT NULL,
    account_evidence_id uuid NOT NULL,
    shop_evidence_id uuid NOT NULL,
    authority_verified_at timestamp with time zone NOT NULL,
    authority_valid_until timestamp with time zone NOT NULL,
    user_id uuid NOT NULL,
    session_id uuid NOT NULL,
    device_id character varying(120) NOT NULL,
    membership_authority_version bigint NOT NULL,
    session_scope_version bigint NOT NULL,
    device_verified_at timestamp with time zone NOT NULL,
    auth_deadline timestamp with time zone NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    key_id character varying(128) NOT NULL,
    signed_grant character varying(8192) NOT NULL,
    CONSTRAINT valid_offline_license_grant CHECK (((account_revision > 0) AND (shop_revision > 0) AND (membership_authority_version >= 0) AND (session_scope_version >= 0) AND isfinite(authority_verified_at) AND isfinite(authority_valid_until) AND isfinite(device_verified_at) AND isfinite(auth_deadline) AND isfinite(captured_at) AND (authority_verified_at <= device_verified_at) AND (device_verified_at <= captured_at) AND (captured_at < authority_valid_until) AND (captured_at < auth_deadline) AND (btrim((device_id)::text) <> ''::text) AND ((key_id)::text ~ '^[A-Za-z0-9_-]{1,128}$'::text) AND ((signed_grant)::text ~ '^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$'::text)))
);

ALTER TABLE ONLY public.offline_license_grants FORCE ROW LEVEL SECURITY;


--
-- Name: offline_license_permits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.offline_license_permits (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    grant_id uuid NOT NULL,
    lease_id uuid NOT NULL,
    snapshot_id uuid NOT NULL,
    user_id uuid NOT NULL,
    session_id uuid NOT NULL,
    device_id character varying(120) NOT NULL,
    till_session_id uuid,
    batch_count integer NOT NULL,
    catalog_version bigint NOT NULL,
    current_account_revision bigint NOT NULL,
    current_shop_revision bigint NOT NULL,
    completed_at timestamp with time zone NOT NULL,
    not_before timestamp with time zone NOT NULL,
    valid_until timestamp with time zone NOT NULL,
    limiting_reason character varying(32) NOT NULL,
    key_id character varying(128) NOT NULL,
    signed_permit character varying(8192) NOT NULL,
    CONSTRAINT valid_offline_license_permit CHECK (((current_account_revision > 0) AND (current_shop_revision > 0) AND (batch_count >= 0) AND (catalog_version >= 0) AND isfinite(completed_at) AND isfinite(not_before) AND isfinite(valid_until) AND (completed_at <= not_before) AND (not_before < valid_until) AND (btrim((device_id)::text) <> ''::text) AND ((limiting_reason)::text ~ '^[A-Za-z]{1,32}$'::text) AND ((key_id)::text ~ '^[A-Za-z0-9_-]{1,128}$'::text) AND ((signed_permit)::text ~ '^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$'::text)))
);

ALTER TABLE ONLY public.offline_license_permits FORCE ROW LEVEL SECURITY;


--
-- Name: offline_license_verification_keys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.offline_license_verification_keys (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    key_id character varying(128) NOT NULL,
    public_key bytea NOT NULL,
    fingerprint character varying(64) NOT NULL,
    first_used_at timestamp with time zone NOT NULL,
    CONSTRAINT valid_offline_license_verification_key CHECK ((((key_id)::text ~ '^[A-Za-z0-9_-]{1,128}$'::text) AND isfinite(first_used_at) AND ((octet_length(public_key) >= 256) AND (octet_length(public_key) <= 2048)) AND ((fingerprint)::text = encode(sha256(public_key), 'hex'::text))))
);

ALTER TABLE ONLY public.offline_license_verification_keys FORCE ROW LEVEL SECURITY;


--
-- Name: offline_submissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.offline_submissions (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    device_id character varying(120) NOT NULL,
    client_sale_id character varying(120) NOT NULL,
    invoice_number character varying(40) NOT NULL,
    payload_hash character varying(64) NOT NULL,
    payload_json jsonb NOT NULL,
    claimed_sold_at timestamp with time zone NOT NULL,
    received_at timestamp with time zone NOT NULL,
    source integer NOT NULL,
    submitted_by_user_id uuid NOT NULL,
    asserted_issuer_user_id uuid,
    original_issuer_user_id uuid,
    original_lease_id uuid,
    recovery_operation_id character varying(128),
    sale_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    licence_grant_id uuid,
    licence_permit_id uuid
);

ALTER TABLE ONLY public.offline_submissions FORCE ROW LEVEL SECURITY;


--
-- Name: operation_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operation_receipts (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    operation_type integer NOT NULL,
    operation_key character varying(128) NOT NULL,
    request_hash character varying(64) NOT NULL,
    hash_version integer NOT NULL,
    actor_user_id uuid NOT NULL,
    target_id uuid NOT NULL,
    status integer NOT NULL,
    response_version integer NOT NULL,
    response_body jsonb,
    completed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.operation_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: password_reset_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.password_reset_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(128) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    is_used boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    destination character varying(256) DEFAULT ''::character varying NOT NULL,
    security_version bigint DEFAULT 0 NOT NULL
);


--
-- Name: payment_allocations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_allocations (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    receipt_id uuid NOT NULL,
    target_type character varying(30) NOT NULL,
    target_id uuid NOT NULL,
    amount numeric(18,2) NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_payment_allocations_amount_positive CHECK ((amount > (0)::numeric))
);

ALTER TABLE ONLY public.payment_allocations FORCE ROW LEVEL SECURITY;


--
-- Name: payment_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_receipts (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    party_type character varying(20) NOT NULL,
    party_id uuid NOT NULL,
    amount numeric(18,2) NOT NULL,
    method character varying(30) NOT NULL,
    bank_account_id uuid,
    external_reference character varying(80),
    business_date date NOT NULL,
    received_at timestamp with time zone NOT NULL,
    notes character varying(500),
    ledger_entry_id uuid NOT NULL,
    expense_id uuid,
    is_legacy boolean NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_payment_receipts_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT ck_payment_receipts_expense_supplier_only CHECK (((expense_id IS NULL) OR ((party_type)::text = 'SUPPLIER'::text)))
);

ALTER TABLE ONLY public.payment_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: payment_reversals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_reversals (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    receipt_id uuid NOT NULL,
    amount numeric(18,2) NOT NULL,
    reason character varying(255) NOT NULL,
    ledger_entry_id uuid NOT NULL,
    business_date date NOT NULL,
    reversed_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    settlement_evidence jsonb,
    CONSTRAINT ck_payment_reversals_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT ck_payment_reversals_reason_required CHECK ((length(btrim((reason)::text)) >= 3))
);

ALTER TABLE ONLY public.payment_reversals FORCE ROW LEVEL SECURITY;


--
-- Name: phone_challenges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.phone_challenges (
    id uuid NOT NULL,
    purpose character varying(32) NOT NULL,
    destination character varying(16) NOT NULL,
    code_hash character varying(64) NOT NULL,
    key_id character varying(32) NOT NULL,
    user_id uuid,
    security_version bigint NOT NULL,
    attempts integer NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    resend_allowed_at timestamp with time zone NOT NULL,
    consumed_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: posting_period_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.posting_period_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    posting_period_id uuid NOT NULL,
    action integer NOT NULL,
    reason character varying(500) NOT NULL,
    actor_user_id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.posting_period_events FORCE ROW LEVEL SECURITY;


--
-- Name: posting_periods; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.posting_periods (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    state integer NOT NULL,
    closed_at timestamp with time zone,
    closed_by_user_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_posting_periods_range CHECK ((end_date >= start_date))
);

ALTER TABLE ONLY public.posting_periods FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_order_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_order_lines (
    id uuid NOT NULL,
    purchase_order_id uuid NOT NULL,
    item_id uuid NOT NULL,
    description character varying(500) NOT NULL,
    expected_quantity integer NOT NULL,
    unit_cost numeric(18,2) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    received_quantity integer DEFAULT 0 NOT NULL
);

ALTER TABLE ONLY public.purchase_order_lines FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_order_receipt_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_order_receipt_lines (
    id uuid NOT NULL,
    purchase_order_receipt_id uuid NOT NULL,
    purchase_order_line_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    stock_transaction_id uuid NOT NULL,
    quantity numeric(18,2) NOT NULL,
    total_purchase_cost numeric(18,2) NOT NULL,
    unit_cost numeric(18,2) NOT NULL,
    mrp numeric(18,2) NOT NULL,
    sales_price numeric(18,2) NOT NULL,
    tax_rate_percent numeric(5,2) NOT NULL,
    tax_included boolean NOT NULL,
    purchase_tax_included boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    client_line_id uuid
);

ALTER TABLE ONLY public.purchase_order_receipt_lines FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_order_receipt_sequences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_order_receipt_sequences (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    year integer NOT NULL,
    next_number integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.purchase_order_receipt_sequences FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_order_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_order_receipts (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    purchase_order_id uuid NOT NULL,
    receipt_number character varying(32) NOT NULL,
    received_at timestamp with time zone NOT NULL,
    reference_number character varying(100),
    notes character varying(1000),
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.purchase_order_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_order_sequences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_order_sequences (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    year integer NOT NULL,
    next_number integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.purchase_order_sequences FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_orders (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    supplier_id uuid,
    purchase_order_number character varying(32) NOT NULL,
    order_date date,
    expected_delivery_date date,
    supplier_reference_number character varying(100),
    notes character varying(1000),
    status integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    supplier_name character varying(200),
    supplier_reference character varying(120),
    cancellation_reason character varying(500),
    close_reason character varying(500),
    closed_at timestamp with time zone,
    closed_by uuid
);

ALTER TABLE ONLY public.purchase_orders FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_receipt_cost_adjustments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_receipt_cost_adjustments (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    purchase_receipt_value_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    prior_total_cost numeric(18,2) NOT NULL,
    revised_total_cost numeric(18,2) NOT NULL,
    delta numeric(18,2) NOT NULL,
    reason character varying(500) NOT NULL,
    supplier_ledger_entry_id uuid NOT NULL,
    performed_at timestamp with time zone NOT NULL,
    performed_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_purchase_receipt_cost_adjustments_delta CHECK (((delta = (revised_total_cost - prior_total_cost)) AND (delta <> (0)::numeric))),
    CONSTRAINT ck_purchase_receipt_cost_adjustments_reason_required CHECK ((length(btrim((reason)::text)) > 0)),
    CONSTRAINT ck_purchase_receipt_cost_adjustments_totals_non_negative CHECK (((prior_total_cost >= (0)::numeric) AND (revised_total_cost >= (0)::numeric)))
);

ALTER TABLE ONLY public.purchase_receipt_cost_adjustments FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_receipt_reversals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_receipt_reversals (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    receipt_value_id uuid NOT NULL,
    purchase_order_receipt_id uuid,
    created_by uuid NOT NULL,
    reason character varying(255) NOT NULL,
    reversed_at timestamp with time zone NOT NULL,
    business_date date NOT NULL,
    business_time_zone_id character varying(80) NOT NULL,
    quantity numeric(18,3) NOT NULL,
    effective_amount numeric(10,2) NOT NULL,
    stock_transaction_id uuid NOT NULL,
    supplier_ledger_entry_id uuid,
    reference character varying(80) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_purchase_receipt_reversals_amounts CHECK (((quantity > (0)::numeric) AND (effective_amount >= (0)::numeric) AND (((effective_amount = (0)::numeric) AND (supplier_ledger_entry_id IS NULL)) OR ((effective_amount > (0)::numeric) AND (supplier_ledger_entry_id IS NOT NULL)))))
);

ALTER TABLE ONLY public.purchase_receipt_reversals FORCE ROW LEVEL SECURITY;


--
-- Name: purchase_receipt_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_receipt_values (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    item_id uuid NOT NULL,
    supplier_id uuid,
    received_quantity numeric(18,3) NOT NULL,
    original_total_cost numeric(18,2),
    effective_total_cost numeric(18,2),
    source character varying(20) NOT NULL,
    status character varying(20) NOT NULL,
    source_reference character varying(80),
    purchase_order_receipt_line_id uuid,
    received_at timestamp with time zone NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    has_supplier_returns boolean DEFAULT false NOT NULL,
    CONSTRAINT ck_purchase_receipt_values_amounts_by_status CHECK (((((status)::text = 'RESOLVED'::text) AND (original_total_cost IS NOT NULL) AND (original_total_cost >= (0)::numeric) AND (effective_total_cost IS NOT NULL) AND (effective_total_cost >= (0)::numeric)) OR (((status)::text = 'LEGACY_UNRESOLVED'::text) AND (original_total_cost IS NULL) AND (effective_total_cost IS NULL)))),
    CONSTRAINT ck_purchase_receipt_values_quantity_positive CHECK ((received_quantity > (0)::numeric))
);

ALTER TABLE ONLY public.purchase_receipt_values FORCE ROW LEVEL SECURITY;


--
-- Name: rate_limit_buckets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.rate_limit_buckets (
    policy_id character varying(200) NOT NULL,
    bucket_key character varying(128) NOT NULL,
    window_start timestamp with time zone NOT NULL,
    window_seconds integer NOT NULL,
    count integer NOT NULL,
    blocked_until timestamp with time zone,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_rate_limit_buckets_count CHECK ((count >= 0)),
    CONSTRAINT ck_rate_limit_buckets_window_seconds CHECK ((window_seconds > 0))
);


--
-- Name: realtime_connection_leases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.realtime_connection_leases (
    id uuid NOT NULL,
    connection_id character varying(128) NOT NULL,
    instance_id character varying(128) NOT NULL,
    hub_name character varying(128) NOT NULL,
    user_id uuid NOT NULL,
    session_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    shop_role character varying(32),
    security_version bigint NOT NULL,
    scope_version bigint NOT NULL,
    connected_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: reconciliation_case_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reconciliation_case_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    case_id uuid NOT NULL,
    action integer NOT NULL,
    from_status integer NOT NULL,
    to_status integer NOT NULL,
    actor_user_id uuid NOT NULL,
    reason character varying(500) NOT NULL,
    linked_operation_id uuid,
    operation_key character varying(128),
    occurred_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.reconciliation_case_events FORCE ROW LEVEL SECURITY;


--
-- Name: reconciliation_issues; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reconciliation_issues (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_id uuid,
    client_sale_id character varying(120) NOT NULL,
    device_id character varying(120) NOT NULL,
    issue_type integer NOT NULL,
    item_id uuid,
    inventory_batch_id uuid,
    printed_quantity numeric(18,3),
    available_quantity numeric(18,3),
    consumed_quantity numeric(18,3),
    code character varying(128) NOT NULL,
    message character varying(1000) NOT NULL,
    is_resolved boolean NOT NULL,
    resolved_at timestamp with time zone,
    resolved_by uuid,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    invoice_number character varying(40),
    linked_operation_id uuid,
    offline_submission_id uuid,
    resolution integer,
    resolution_reason character varying(500),
    resolution_stock_transaction_id uuid,
    status integer DEFAULT 1 NOT NULL
);

ALTER TABLE ONLY public.reconciliation_issues FORCE ROW LEVEL SECURITY;


--
-- Name: reduction_manifest_pages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reduction_manifest_pages (
    account_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    generation bigint NOT NULL,
    page_index integer NOT NULL,
    page_count integer NOT NULL,
    event_id uuid NOT NULL,
    order_id uuid NOT NULL,
    term_id uuid NOT NULL,
    total_source_shops integer NOT NULL,
    retained_shop_count integer NOT NULL,
    source_binding_digest text NOT NULL,
    retained_binding_digest text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    digest text NOT NULL,
    payload bytea NOT NULL,
    assertion text NOT NULL,
    public_key text NOT NULL,
    CONSTRAINT reduction_manifest_pages_check CHECK (((page_count > 0) AND (page_index < page_count))),
    CONSTRAINT reduction_manifest_pages_check1 CHECK (((retained_shop_count >= 1) AND (retained_shop_count <= total_source_shops))),
    CONSTRAINT reduction_manifest_pages_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT reduction_manifest_pages_expires_at_check CHECK (isfinite(expires_at)),
    CONSTRAINT reduction_manifest_pages_generation_check CHECK ((generation > 0)),
    CONSTRAINT reduction_manifest_pages_page_index_check CHECK ((page_index >= 0)),
    CONSTRAINT reduction_manifest_pages_payload_check CHECK (((octet_length(payload) >= 1) AND (octet_length(payload) <= 8192))),
    CONSTRAINT reduction_manifest_pages_retained_binding_digest_check CHECK ((retained_binding_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT reduction_manifest_pages_source_binding_digest_check CHECK ((source_binding_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT reduction_manifest_pages_total_source_shops_check CHECK (((total_source_shops >= 1) AND (total_source_shops <= 100000)))
);

ALTER TABLE ONLY public.reduction_manifest_pages FORCE ROW LEVEL SECURITY;


--
-- Name: reduction_manifest_shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reduction_manifest_shops (
    account_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    generation bigint NOT NULL,
    page_index integer NOT NULL,
    shop_id uuid NOT NULL,
    affiliation_request_id uuid NOT NULL,
    retained boolean NOT NULL,
    expected_active_members integer NOT NULL,
    expected_seat_hash text NOT NULL,
    CONSTRAINT reduction_manifest_shops_expected_active_members_check CHECK ((expected_active_members >= 0)),
    CONSTRAINT reduction_manifest_shops_expected_seat_hash_check CHECK ((expected_seat_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT reduction_manifest_shops_generation_check CHECK ((generation > 0)),
    CONSTRAINT reduction_manifest_shops_page_index_check CHECK ((page_index >= 0))
);

ALTER TABLE ONLY public.reduction_manifest_shops FORCE ROW LEVEL SECURITY;


--
-- Name: reduction_reservation_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reduction_reservation_receipts (
    account_id uuid NOT NULL,
    term_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    order_id uuid NOT NULL,
    generation bigint NOT NULL,
    event_id uuid NOT NULL,
    phase text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    digest text NOT NULL,
    fence_hash text,
    outcome text NOT NULL,
    reason text,
    command bytea NOT NULL,
    assertion text NOT NULL,
    public_key text NOT NULL,
    CONSTRAINT reduction_reservation_receipts_check CHECK ((((phase = 'ReductionReserve'::text) OR (outcome <> 'Reserved'::text)) AND ((phase = 'ReductionConsume'::text) OR (outcome <> 'Applied'::text)))),
    CONSTRAINT reduction_reservation_receipts_command_check CHECK (((octet_length(command) >= 1) AND (octet_length(command) <= 8192))),
    CONSTRAINT reduction_reservation_receipts_digest_check CHECK ((digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT reduction_reservation_receipts_expires_at_check CHECK (isfinite(expires_at)),
    CONSTRAINT reduction_reservation_receipts_generation_check CHECK ((generation > 0)),
    CONSTRAINT reduction_reservation_receipts_outcome_check CHECK ((outcome = ANY (ARRAY['Reserved'::text, 'Applied'::text, 'NeedsResolution'::text, 'NeedsReview'::text]))),
    CONSTRAINT reduction_reservation_receipts_phase_check CHECK ((phase = ANY (ARRAY['ReductionReserve'::text, 'ReductionConsume'::text])))
);

ALTER TABLE ONLY public.reduction_reservation_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refresh_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token character varying(128),
    expires_at timestamp with time zone NOT NULL,
    is_revoked boolean NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    security_version bigint DEFAULT 0 NOT NULL,
    auth_session_id uuid,
    consumed_at timestamp with time zone,
    parent_token_id uuid,
    revocation_reason character varying(64),
    token_hash character varying(64)
);


--
-- Name: sale_cancellation_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_cancellation_lines (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_cancellation_id uuid NOT NULL,
    sale_id uuid NOT NULL,
    sale_item_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    stock_transaction_id uuid NOT NULL,
    quantity numeric(18,3) NOT NULL,
    description character varying(180) NOT NULL,
    uqc character varying(80) NOT NULL,
    original_cost_price numeric(28,10) NOT NULL,
    revenue_amount numeric(18,2) NOT NULL,
    recorded_cost_amount numeric(18,2) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    disposition_reason_code character varying(80),
    quarantine_change_id uuid,
    quarantined_quantity numeric(18,3) DEFAULT 0.0 NOT NULL,
    CONSTRAINT ck_sale_cancellation_lines_amounts CHECK (((quantity > (0)::numeric) AND (original_cost_price >= (0)::numeric) AND (revenue_amount >= (0)::numeric) AND (recorded_cost_amount >= (0)::numeric)))
);

ALTER TABLE ONLY public.sale_cancellation_lines FORCE ROW LEVEL SECURITY;


--
-- Name: sale_cancellations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_cancellations (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_id uuid NOT NULL,
    actor_user_id uuid NOT NULL,
    operation_key character varying(128) NOT NULL,
    reference_number character varying(35) NOT NULL,
    reason character varying(255) NOT NULL,
    cancelled_at timestamp with time zone NOT NULL,
    business_date date NOT NULL,
    business_time_zone_id character varying(64) NOT NULL,
    policy_version character varying(40) NOT NULL,
    original_invoice_number character varying(40) NOT NULL,
    original_business_date date NOT NULL,
    total_amount numeric(18,2) NOT NULL,
    revenue_amount numeric(18,2) NOT NULL,
    tax_amount numeric(18,2) NOT NULL,
    due_reduction_amount numeric(18,2) NOT NULL,
    cash_refund_amount numeric(18,2) NOT NULL,
    customer_ledger_entry_id uuid,
    seller jsonb NOT NULL,
    buyer jsonb NOT NULL,
    goods_recovered_confirmed boolean NOT NULL,
    cash_refund_confirmed boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_sale_cancellations_amounts CHECK (((total_amount >= (0)::numeric) AND (revenue_amount >= (0)::numeric) AND (tax_amount = (0)::numeric) AND (due_reduction_amount >= (0)::numeric) AND (cash_refund_amount >= (0)::numeric) AND (total_amount = (due_reduction_amount + cash_refund_amount)) AND (revenue_amount = total_amount))),
    CONSTRAINT ck_sale_cancellations_reason CHECK ((((length(btrim((reason)::text)) >= 3) AND (length(btrim((reason)::text)) <= 255)) AND ((length(btrim((operation_key)::text)) >= 1) AND (length(btrim((operation_key)::text)) <= 128)))),
    CONSTRAINT ck_sale_cancellations_settlement CHECK (((((due_reduction_amount > (0)::numeric) AND (customer_ledger_entry_id IS NOT NULL)) OR ((due_reduction_amount = (0)::numeric) AND (customer_ledger_entry_id IS NULL))) AND (cash_refund_confirmed = (cash_refund_amount > (0)::numeric)) AND goods_recovered_confirmed))
);

ALTER TABLE ONLY public.sale_cancellations FORCE ROW LEVEL SECURITY;


--
-- Name: sale_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_items (
    id uuid NOT NULL,
    sale_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid,
    inventory_batch_id uuid,
    quantity numeric(18,3) NOT NULL,
    cost_price numeric(18,2) NOT NULL,
    sales_price numeric(18,2) NOT NULL,
    mrp numeric(18,2) NOT NULL,
    tax_rate_percent numeric(5,2) NOT NULL,
    is_price_including_tax boolean NOT NULL,
    has_price_mismatch boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    configured_batch_rule_id uuid,
    configured_batch_rule_percentage numeric(5,2),
    final_sales_price numeric(18,2) DEFAULT 0.0 NOT NULL,
    item_discount_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    item_discount_override_type integer DEFAULT 0 NOT NULL,
    item_discount_override_value numeric(18,2) DEFAULT 0.0 NOT NULL,
    original_sales_price numeric(18,2) DEFAULT 0.0 NOT NULL,
    pre_tax_amount_before_discount numeric(18,2) DEFAULT 0.0 NOT NULL,
    sale_discount_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    tax_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    taxable_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    total_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    hsn_code character varying(20),
    line_code character varying(128) DEFAULT 'UNKNOWN'::character varying NOT NULL,
    line_name character varying(180) DEFAULT 'UNKNOWN'::character varying NOT NULL,
    line_type character varying(16) DEFAULT 'GOODS'::character varying NOT NULL,
    service_id uuid,
    CONSTRAINT ck_sale_items_line_snapshots_required CHECK (((line_name IS NOT NULL) AND (length(btrim((line_name)::text)) > 0) AND (line_code IS NOT NULL) AND (length(btrim((line_code)::text)) > 0))),
    CONSTRAINT ck_sale_items_line_type_refs CHECK (((((line_type)::text = 'GOODS'::text) AND (item_id IS NOT NULL) AND (inventory_batch_id IS NOT NULL) AND (service_id IS NULL)) OR (((line_type)::text = 'SERVICE'::text) AND (service_id IS NOT NULL) AND (item_id IS NULL) AND (inventory_batch_id IS NULL))))
);

ALTER TABLE ONLY public.sale_items FORCE ROW LEVEL SECURITY;


--
-- Name: sale_return_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_return_items (
    id uuid NOT NULL,
    sale_return_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_id uuid NOT NULL,
    sale_item_id uuid NOT NULL,
    quantity numeric(18,3) NOT NULL,
    condition character varying(20),
    original_cost_price numeric(18,2) NOT NULL,
    original_sales_price numeric(18,2) NOT NULL,
    original_tax_rate_percent numeric(5,2) NOT NULL,
    original_is_price_including_tax boolean NOT NULL,
    max_refund_amount numeric(18,2) NOT NULL,
    approved_refund_amount numeric(18,2) NOT NULL,
    taxable_amount numeric(18,2) NOT NULL,
    tax_amount numeric(18,2) NOT NULL,
    notes character varying(1000),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    adjustment_non_tax_amount numeric(18,2),
    adjustment_tax_amount numeric(18,2),
    adjustment_taxable_amount numeric(18,2),
    disposition_reason_code character varying(80),
    quarantine_change_id uuid,
    quarantined_quantity numeric(18,3) DEFAULT 0.0 NOT NULL,
    CONSTRAINT ck_sale_return_items_adjustment_split CHECK ((((adjustment_taxable_amount IS NULL) AND (adjustment_tax_amount IS NULL) AND (adjustment_non_tax_amount IS NULL)) OR ((adjustment_taxable_amount >= (0)::numeric) AND (adjustment_tax_amount >= (0)::numeric) AND (adjustment_non_tax_amount >= (0)::numeric) AND (((adjustment_taxable_amount + adjustment_tax_amount) + adjustment_non_tax_amount) = approved_refund_amount)))),
    CONSTRAINT ck_sale_return_items_amounts_non_negative CHECK (((original_cost_price >= (0)::numeric) AND (original_sales_price >= (0)::numeric) AND (max_refund_amount >= (0)::numeric) AND (approved_refund_amount >= (0)::numeric) AND (taxable_amount >= (0)::numeric) AND (tax_amount >= (0)::numeric))),
    CONSTRAINT ck_sale_return_items_quantity_positive CHECK ((quantity > (0)::numeric)),
    CONSTRAINT ck_sale_return_items_refund_cap CHECK (((approved_refund_amount <= max_refund_amount) OR (notes IS NOT NULL))),
    CONSTRAINT ck_sale_return_items_tax_rate_range CHECK (((original_tax_rate_percent >= (0)::numeric) AND (original_tax_rate_percent <= (100)::numeric)))
);

ALTER TABLE ONLY public.sale_return_items FORCE ROW LEVEL SECURITY;


--
-- Name: sale_return_settlements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_return_settlements (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_return_id uuid NOT NULL,
    sequence integer NOT NULL,
    kind character varying(30) NOT NULL,
    amount numeric(18,2) NOT NULL,
    customer_id uuid,
    credit_note_id uuid,
    customer_ledger_entry_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_sale_return_settlements_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT ck_sale_return_settlements_references_by_kind CHECK (((((kind)::text = 'STORE_CREDIT_ISSUED'::text) AND (credit_note_id IS NOT NULL) AND (customer_ledger_entry_id IS NULL)) OR (((kind)::text = 'RECEIVABLE_REDUCTION'::text) AND (credit_note_id IS NULL)) OR (((kind)::text = 'EXTERNAL_REFUND'::text) AND (credit_note_id IS NULL) AND (customer_ledger_entry_id IS NULL))))
);

ALTER TABLE ONLY public.sale_return_settlements FORCE ROW LEVEL SECURITY;


--
-- Name: sale_returns; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_returns (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    sale_id uuid NOT NULL,
    return_number character varying(40) NOT NULL,
    processed_at timestamp with time zone NOT NULL,
    processed_by uuid NOT NULL,
    notes character varying(1000),
    total_refund_amount numeric(18,2) NOT NULL,
    due_reduction_amount numeric(18,2) NOT NULL,
    payout_amount numeric(18,2) NOT NULL,
    total_taxable_amount numeric(18,2) NOT NULL,
    total_tax_amount numeric(18,2) NOT NULL,
    customer_balance_before numeric(18,2),
    customer_balance_after numeric(18,2),
    is_voided boolean DEFAULT false NOT NULL,
    voided_at timestamp with time zone,
    voided_by uuid,
    void_reason character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    payout_destination integer,
    accounting_policy_version character varying(40),
    accounting_review_reason character varying(200),
    accounting_status character varying(20),
    adjustment_non_tax_amount numeric(18,2),
    adjustment_tax_amount numeric(18,2),
    adjustment_taxable_amount numeric(18,2),
    business_date date NOT NULL,
    business_time_zone character varying(64) NOT NULL,
    voided_business_date date,
    refund_confirmed boolean,
    refund_tender jsonb,
    void_settlement_evidence jsonb,
    CONSTRAINT ck_sale_returns_accounting_balanced CHECK ((((accounting_status)::text IS DISTINCT FROM 'BALANCED'::text) OR ((accounting_policy_version IS NOT NULL) AND (adjustment_taxable_amount IS NOT NULL) AND (adjustment_tax_amount IS NOT NULL) AND (adjustment_non_tax_amount IS NOT NULL) AND (adjustment_taxable_amount >= (0)::numeric) AND (adjustment_tax_amount >= (0)::numeric) AND (adjustment_non_tax_amount >= (0)::numeric) AND (((adjustment_taxable_amount + adjustment_tax_amount) + adjustment_non_tax_amount) = total_refund_amount)))),
    CONSTRAINT ck_sale_returns_accounting_review_reason CHECK ((((accounting_status)::text IS DISTINCT FROM 'NEEDS_REVIEW'::text) OR (accounting_review_reason IS NOT NULL))),
    CONSTRAINT ck_sale_returns_refund_amounts_non_negative CHECK (((total_refund_amount >= (0)::numeric) AND (due_reduction_amount >= (0)::numeric) AND (payout_amount >= (0)::numeric) AND (total_taxable_amount >= (0)::numeric) AND (total_tax_amount >= (0)::numeric))),
    CONSTRAINT ck_sale_returns_refund_split CHECK (((due_reduction_amount + payout_amount) = total_refund_amount)),
    CONSTRAINT ck_sale_returns_void_audit CHECK ((((is_voided = false) AND (voided_at IS NULL) AND (voided_by IS NULL) AND (void_reason IS NULL)) OR ((is_voided = true) AND (voided_at IS NOT NULL) AND (voided_by IS NOT NULL) AND (void_reason IS NOT NULL))))
);

ALTER TABLE ONLY public.sale_returns FORCE ROW LEVEL SECURITY;


--
-- Name: sales; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sales (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    invoice_number character varying(40) NOT NULL,
    customer_id uuid,
    customer_name character varying(180),
    customer_phone character varying(32),
    payment_method integer NOT NULL,
    sold_at timestamp with time zone NOT NULL,
    paid_amount numeric(18,2) NOT NULL,
    due_amount numeric(18,2) NOT NULL,
    total_amount numeric(18,2) NOT NULL,
    total_tax_amount numeric(18,2) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    configured_sale_rule_id uuid,
    configured_sale_rule_percentage numeric(5,2),
    configured_sale_rule_threshold_amount numeric(18,2),
    configured_sale_rule_type integer,
    sale_discount_override_type integer DEFAULT 0 NOT NULL,
    sale_discount_override_value numeric(18,2) DEFAULT 0.0 NOT NULL,
    subtotal_before_discount numeric(18,2) DEFAULT 0.0 NOT NULL,
    total_before_discount numeric(18,2) DEFAULT 0.0 NOT NULL,
    total_discount_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    actor_user_id uuid NOT NULL,
    idempotency_key character varying(128) NOT NULL,
    request_hash character varying(64) NOT NULL,
    warnings text[] NOT NULL,
    client_sale_id character varying(120),
    device_id character varying(120),
    source integer DEFAULT 1 NOT NULL,
    synced_at timestamp with time zone,
    credit_note_applied_amount numeric(18,2) DEFAULT 0.0 NOT NULL,
    invoice_document jsonb,
    credit_override_by_user_id uuid,
    credit_override_limit numeric(18,2),
    credit_override_observed_exposure numeric(18,2),
    credit_override_reason character varying(500),
    due_date date,
    business_date date NOT NULL,
    business_time_zone character varying(64) NOT NULL,
    status integer DEFAULT 0 NOT NULL,
    tender_lines jsonb,
    till_override_reason character varying(500),
    till_session_id uuid,
    expiry_decisions jsonb,
    CONSTRAINT ck_sales_status CHECK ((status = ANY (ARRAY[0, 1]))),
    CONSTRAINT ck_sales_till_evidence CHECK ((((till_session_id IS NULL) AND (tender_lines IS NULL) AND (till_override_reason IS NULL)) OR ((till_session_id IS NOT NULL) AND (tender_lines IS NOT NULL) AND (jsonb_typeof(tender_lines) = 'array'::text) AND (jsonb_array_length(tender_lines) <= 1))))
);

ALTER TABLE ONLY public.sales FORCE ROW LEVEL SECURITY;


--
-- Name: service_code_sequences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.service_code_sequences (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    next_number integer NOT NULL,
    prefix character varying(32) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.service_code_sequences FORCE ROW LEVEL SECURITY;


--
-- Name: services; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.services (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    code character varying(24) NOT NULL,
    name character varying(180) NOT NULL,
    description character varying(1000),
    price numeric(18,2) NOT NULL,
    hsn_code character varying(20),
    tax_rate_percent numeric(5,2) DEFAULT 0.0 NOT NULL,
    tax_included boolean DEFAULT false NOT NULL,
    is_active boolean NOT NULL,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.services FORCE ROW LEVEL SECURITY;


--
-- Name: shop_affiliation_delivery_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_affiliation_delivery_receipts (
    event_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    status text NOT NULL,
    CONSTRAINT shop_affiliation_delivery_receipts_status_check CHECK ((status = ANY (ARRAY['Applied'::text, 'NeedsReview'::text])))
);

ALTER TABLE ONLY public.shop_affiliation_delivery_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: shop_closure_consents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_closure_consents (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    account_id uuid NOT NULL,
    affiliation_id uuid NOT NULL,
    proposal_id uuid NOT NULL,
    is_account_closure boolean NOT NULL,
    operation_key uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    owner_version bigint NOT NULL,
    closure_version bigint NOT NULL,
    scope_digest text NOT NULL,
    work_digest text NOT NULL,
    evidence jsonb NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT shop_closure_consents_closure_version_check CHECK ((closure_version >= 0)),
    CONSTRAINT shop_closure_consents_owner_version_check CHECK ((owner_version >= 0)),
    CONSTRAINT shop_closure_consents_recorded_at_check CHECK (isfinite(recorded_at)),
    CONSTRAINT shop_closure_consents_scope_digest_check CHECK ((scope_digest ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT shop_closure_consents_work_digest_check CHECK ((work_digest ~ '^[0-9a-f]{64}$'::text))
);

ALTER TABLE ONLY public.shop_closure_consents FORCE ROW LEVEL SECURITY;


--
-- Name: shop_expiry_states; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_expiry_states (
    shop_id uuid NOT NULL,
    catalog_version bigint NOT NULL,
    default_policy_version bigint NOT NULL,
    initialized_at timestamp with time zone NOT NULL,
    pre_feature_baseline boolean NOT NULL,
    CONSTRAINT ck_shop_expiry_states_version CHECK (((catalog_version >= 1) AND (default_policy_version >= 1) AND (default_policy_version <= catalog_version)))
);

ALTER TABLE ONLY public.shop_expiry_states FORCE ROW LEVEL SECURITY;


--
-- Name: shop_invitations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_invitations (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    email character varying(256) NOT NULL,
    role character varying(32) NOT NULL,
    token_hash character varying(64) NOT NULL,
    invited_by_user_id uuid NOT NULL,
    inviter_authority_version bigint NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    accepted_at timestamp with time zone,
    revoked_at timestamp with time zone,
    accepted_by_user_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: shop_memberships; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_memberships (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role character varying(32) NOT NULL,
    is_default boolean NOT NULL,
    last_used_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    authority_version bigint DEFAULT 0 NOT NULL,
    status character varying(32) DEFAULT 'Active'::character varying NOT NULL
);


--
-- Name: shop_setup_preferences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_setup_preferences (
    shop_id uuid NOT NULL,
    version integer NOT NULL,
    show_on_startup boolean NOT NULL,
    sample_receipt_acknowledged_at timestamp with time zone,
    sample_receipt_acknowledged_by_user_id uuid,
    CONSTRAINT ck_shop_setup_preferences_acknowledgement CHECK (((sample_receipt_acknowledged_at IS NULL) = (sample_receipt_acknowledged_by_user_id IS NULL))),
    CONSTRAINT ck_shop_setup_preferences_version CHECK ((version >= 0))
);

ALTER TABLE ONLY public.shop_setup_preferences FORCE ROW LEVEL SECURITY;


--
-- Name: shop_till_setting_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_till_setting_events (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    previous_enabled boolean NOT NULL,
    enabled boolean NOT NULL,
    version integer NOT NULL,
    actor_user_id uuid NOT NULL,
    reason character varying(500) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_shop_till_setting_events_reason CHECK (((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500))),
    CONSTRAINT ck_shop_till_setting_events_version CHECK (((version > 0) AND (previous_enabled <> enabled)))
);

ALTER TABLE ONLY public.shop_till_setting_events FORCE ROW LEVEL SECURITY;


--
-- Name: shop_till_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_till_settings (
    shop_id uuid NOT NULL,
    enabled boolean NOT NULL,
    version integer NOT NULL,
    CONSTRAINT ck_shop_till_settings_version CHECK ((version >= 0))
);

ALTER TABLE ONLY public.shop_till_settings FORCE ROW LEVEL SECURITY;


--
-- Name: shop_trial_consents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_trial_consents (
    event_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    membership_id uuid NOT NULL,
    authority_version bigint NOT NULL,
    operation_key uuid NOT NULL,
    accepted_at timestamp with time zone NOT NULL,
    active_members integer NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT shop_trial_consents_active_members_check CHECK ((active_members > 0)),
    CONSTRAINT shop_trial_consents_authority_version_check CHECK ((authority_version >= 0)),
    CONSTRAINT shop_trial_consents_check CHECK (((NOT (((payload ->> 'eventId'::text))::uuid IS DISTINCT FROM event_id)) AND (NOT (((payload ->> 'shopId'::text))::uuid IS DISTINCT FROM shop_id)) AND (NOT (((payload ->> 'actorId'::text))::uuid IS DISTINCT FROM actor_id)) AND (NOT (((payload ->> 'membershipId'::text))::uuid IS DISTINCT FROM membership_id)) AND (NOT (((payload ->> 'authorityVersion'::text))::bigint IS DISTINCT FROM authority_version)) AND (NOT (((payload ->> 'activeMembers'::text))::integer IS DISTINCT FROM active_members)) AND (NOT (((payload ->> 'acceptedAt'::text))::timestamp with time zone IS DISTINCT FROM accepted_at)) AND (NOT ((((payload -> 'acceptance'::text) ->> 'operationKey'::text))::uuid IS DISTINCT FROM operation_key)) AND (NOT (((payload -> 'acceptance'::text) -> 'termsAccepted'::text) IS DISTINCT FROM 'true'::jsonb))))
);

ALTER TABLE ONLY public.shop_trial_consents FORCE ROW LEVEL SECURITY;


--
-- Name: shop_trial_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_trial_receipts (
    event_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    status text NOT NULL,
    reason character varying(40),
    CONSTRAINT shop_trial_receipts_check CHECK ((((status = 'Applied'::text) AND (reason IS NULL)) OR ((status = 'NeedsReview'::text) AND (reason IS NOT NULL))))
);

ALTER TABLE ONLY public.shop_trial_receipts FORCE ROW LEVEL SECURITY;


--
-- Name: shops; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shops (
    id uuid NOT NULL,
    name character varying(160) NOT NULL,
    address character varying(320) NOT NULL,
    city character varying(120) NOT NULL,
    state character varying(120) NOT NULL,
    pincode character varying(16) NOT NULL,
    contact_person character varying(120),
    mobile_number character varying(32),
    gst_number character varying(20),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    e_invoice_attested_at timestamp with time zone,
    e_invoice_attested_by_user_id uuid,
    e_invoice_status integer DEFAULT 0 NOT NULL,
    invoice_series_code character varying(4) DEFAULT 'A'::character varying NOT NULL,
    legal_name character varying(200),
    state_code character varying(2),
    tax_regime integer DEFAULT 0 NOT NULL,
    business_time_zone character varying(64) DEFAULT 'Asia/Kolkata'::character varying NOT NULL
);


--
-- Name: stock_counts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.stock_counts (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    expected_batch_version bigint NOT NULL,
    observed_quantity numeric(18,3) NOT NULL,
    counted_quantity numeric(18,3) NOT NULL,
    delta numeric(18,3) NOT NULL,
    reason character varying(500) NOT NULL,
    performed_at timestamp with time zone NOT NULL,
    performed_by uuid NOT NULL,
    inventory_adjustment_id uuid,
    stock_transaction_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_stock_counts_delta CHECK ((delta = (counted_quantity - observed_quantity))),
    CONSTRAINT ck_stock_counts_movement_links CHECK ((((delta = (0)::numeric) AND (inventory_adjustment_id IS NULL) AND (stock_transaction_id IS NULL)) OR ((delta <> (0)::numeric) AND (inventory_adjustment_id IS NOT NULL) AND (stock_transaction_id IS NOT NULL)))),
    CONSTRAINT ck_stock_counts_quantities_non_negative CHECK (((observed_quantity >= (0)::numeric) AND (counted_quantity >= (0)::numeric))),
    CONSTRAINT ck_stock_counts_reason_required CHECK ((length(btrim((reason)::text)) > 0))
);

ALTER TABLE ONLY public.stock_counts FORCE ROW LEVEL SECURITY;


--
-- Name: stock_transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.stock_transactions (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    item_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    transaction_type character varying(24) NOT NULL,
    quantity numeric(18,3) NOT NULL,
    reference_number character varying(120),
    notes character varying(1000),
    performed_at timestamp with time zone NOT NULL,
    performed_by uuid NOT NULL,
    created_by uuid NOT NULL,
    updated_by uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_stock_transactions_quantity_non_zero CHECK ((quantity <> (0)::numeric)),
    CONSTRAINT ck_stock_transactions_quantity_sign_by_type CHECK (((((transaction_type)::text = ANY ((ARRAY['IN'::character varying, 'RET'::character varying, 'SALE_CANCELLATION'::character varying])::text[])) AND (quantity > (0)::numeric)) OR (((transaction_type)::text = ANY ((ARRAY['OUT'::character varying, 'REJ'::character varying, 'DMG'::character varying, 'STOL'::character varying, 'SUPPLIER_RETURN'::character varying])::text[])) AND (quantity < (0)::numeric)) OR ((transaction_type)::text = ANY ((ARRAY['ADJ'::character varying, 'REV'::character varying])::text[]))))
);

ALTER TABLE ONLY public.stock_transactions FORCE ROW LEVEL SECURITY;


--
-- Name: supplier_ledger_entries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.supplier_ledger_entries (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    supplier_id uuid NOT NULL,
    batch_id uuid,
    entry_type character varying(24) NOT NULL,
    amount numeric(10,2) NOT NULL,
    entry_date date NOT NULL,
    notes character varying(255),
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_supplier_ledger_entries_amount_non_zero CHECK ((amount <> (0)::numeric)),
    CONSTRAINT ck_supplier_ledger_entries_batch_by_type CHECK (((((entry_type)::text = ANY ((ARRAY['GOODS_RECEIVED'::character varying, 'COST_ADJUSTED'::character varying, 'SUPPLIER_RETURN_CREDIT'::character varying])::text[])) AND (batch_id IS NOT NULL)) OR (((entry_type)::text = ANY ((ARRAY['PAYMENT_MADE'::character varying, 'RECORD_ADJUSTED'::character varying])::text[])) AND (batch_id IS NULL)) OR ((entry_type)::text = 'REVERSAL'::text)))
);

ALTER TABLE ONLY public.supplier_ledger_entries FORCE ROW LEVEL SECURITY;


--
-- Name: supplier_return_credit_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.supplier_return_credit_lines (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    supplier_return_credit_id uuid NOT NULL,
    return_line_id uuid NOT NULL,
    receipt_value_id uuid NOT NULL,
    accepted_amount numeric(10,2) NOT NULL,
    supplier_ledger_entry_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_supplier_return_credit_lines_amounts CHECK (((accepted_amount >= (0)::numeric) AND (((accepted_amount = (0)::numeric) AND (supplier_ledger_entry_id IS NULL)) OR ((accepted_amount > (0)::numeric) AND (supplier_ledger_entry_id IS NOT NULL)))))
);

ALTER TABLE ONLY public.supplier_return_credit_lines FORCE ROW LEVEL SECURITY;


--
-- Name: supplier_return_credits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.supplier_return_credits (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    supplier_return_id uuid NOT NULL,
    created_by uuid NOT NULL,
    accepted_at timestamp with time zone NOT NULL,
    business_date date NOT NULL,
    business_time_zone_id character varying(80) NOT NULL,
    reason character varying(255) NOT NULL,
    supplier_reference character varying(255),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.supplier_return_credits FORCE ROW LEVEL SECURITY;


--
-- Name: supplier_return_lines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.supplier_return_lines (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    supplier_return_id uuid NOT NULL,
    receipt_value_id uuid NOT NULL,
    inventory_batch_id uuid NOT NULL,
    item_id uuid NOT NULL,
    purchase_order_receipt_line_id uuid,
    quantity numeric(18,3) NOT NULL,
    proposed_credit_amount numeric(10,2) NOT NULL,
    received_quantity numeric(18,3) NOT NULL,
    effective_total_cost numeric(10,2) NOT NULL,
    item_name character varying(255) NOT NULL,
    uqc character varying(80) NOT NULL,
    source_reference character varying(80),
    stock_transaction_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    batch_number_snapshot character varying(100),
    CONSTRAINT ck_supplier_return_lines_amounts CHECK (((quantity > (0)::numeric) AND (quantity <= received_quantity) AND (proposed_credit_amount >= (0)::numeric) AND (effective_total_cost >= (0)::numeric)))
);

ALTER TABLE ONLY public.supplier_return_lines FORCE ROW LEVEL SECURITY;


--
-- Name: supplier_returns; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.supplier_returns (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    supplier_id uuid NOT NULL,
    return_number character varying(80) NOT NULL,
    reason character varying(255) NOT NULL,
    created_by uuid NOT NULL,
    shipped_at timestamp with time zone NOT NULL,
    business_date date NOT NULL,
    business_time_zone_id character varying(80) NOT NULL,
    shop_name character varying(255) NOT NULL,
    supplier_name character varying(255) NOT NULL,
    status character varying(32) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.supplier_returns FORCE ROW LEVEL SECURITY;


--
-- Name: suppliers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.suppliers (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    name character varying(180) NOT NULL,
    contact_person_name character varying(120),
    contact_person_phone character varying(32),
    address character varying(320),
    city character varying(120),
    state character varying(120),
    pin character varying(16),
    is_system boolean DEFAULT false NOT NULL,
    is_active boolean NOT NULL,
    is_preferred boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone
);

ALTER TABLE ONLY public.suppliers FORCE ROW LEVEL SECURITY;


--
-- Name: till_close_reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.till_close_reviews (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    session_id uuid NOT NULL,
    close_id uuid NOT NULL,
    reviewed_by uuid NOT NULL,
    reviewed_at timestamp with time zone NOT NULL,
    reason character varying(500) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_till_reviews_reason CHECK (((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500)))
);

ALTER TABLE ONLY public.till_close_reviews FORCE ROW LEVEL SECURITY;


--
-- Name: till_closes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.till_closes (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    session_id uuid NOT NULL,
    opening_float numeric NOT NULL,
    last_movement_sequence bigint NOT NULL,
    totals jsonb NOT NULL,
    expected_cash numeric NOT NULL,
    counted_cash numeric NOT NULL,
    variance numeric NOT NULL,
    closed_by uuid NOT NULL,
    closed_at timestamp with time zone NOT NULL,
    reason character varying(500),
    override_reason character varying(500),
    is_overdue boolean NOT NULL,
    awaiting_review boolean NOT NULL,
    business_date date NOT NULL,
    till_name character varying(100) NOT NULL,
    cashier_name character varying(200) NOT NULL,
    cashier_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_till_closes_amount CHECK (((opening_float >= (0)::numeric) AND (counted_cash >= (0)::numeric) AND (opening_float <= 9999999999999999.99) AND (counted_cash <= 9999999999999999.99) AND (opening_float = round(opening_float, 2)) AND (counted_cash = round(counted_cash, 2)) AND (expected_cash = round(expected_cash, 2)) AND (variance = (counted_cash - expected_cash)))),
    CONSTRAINT ck_till_closes_override CHECK (((closed_by = cashier_id) OR ((override_reason IS NOT NULL) AND ((length(btrim((override_reason)::text)) >= 1) AND (length(btrim((override_reason)::text)) <= 500))))),
    CONSTRAINT ck_till_closes_reason CHECK ((((variance = (0)::numeric) AND (NOT is_overdue)) OR ((reason IS NOT NULL) AND ((length(btrim((reason)::text)) >= 1) AND (length(btrim((reason)::text)) <= 500))))),
    CONSTRAINT ck_till_closes_sequence CHECK (((last_movement_sequence >= 0) AND ((NOT awaiting_review) OR (variance <> (0)::numeric))))
);

ALTER TABLE ONLY public.till_closes FORCE ROW LEVEL SECURITY;


--
-- Name: till_late_postings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.till_late_postings (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    offline_submission_id uuid NOT NULL,
    sale_id uuid NOT NULL,
    case_id uuid NOT NULL,
    session_id uuid,
    close_id uuid,
    source_business_date date NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    cashier_id uuid NOT NULL,
    tender_lines jsonb NOT NULL,
    evidence_status character varying(32) NOT NULL,
    manager_reason character varying(500) NOT NULL,
    included_in_count boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    evidence_reason character varying(500),
    CONSTRAINT ck_till_late_postings_evidence CHECK (((NOT included_in_count) AND ((evidence_status)::text = ANY ((ARRAY['Unknown'::character varying, 'Recorded'::character varying])::text[])) AND ((length(btrim((manager_reason)::text)) >= 1) AND (length(btrim((manager_reason)::text)) <= 500)) AND (recorded_at >= occurred_at) AND ((close_id IS NULL) OR (session_id IS NOT NULL)) AND (jsonb_typeof(tender_lines) = 'array'::text) AND (jsonb_array_length(tender_lines) <= 1) AND (((evidence_status)::text <> 'Unknown'::text) OR (jsonb_array_length(tender_lines) = 0)))),
    CONSTRAINT ck_till_late_postings_evidence_reason CHECK (((evidence_reason IS NULL) OR ((length(btrim((evidence_reason)::text)) >= 1) AND (length(btrim((evidence_reason)::text)) <= 500))))
);

ALTER TABLE ONLY public.till_late_postings FORCE ROW LEVEL SECURITY;


--
-- Name: till_movements; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.till_movements (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    session_id uuid NOT NULL,
    source_record_id uuid NOT NULL,
    sequence bigint NOT NULL,
    source_type integer NOT NULL,
    source_id uuid NOT NULL,
    purpose character varying(100) NOT NULL,
    line_number integer NOT NULL,
    method integer NOT NULL,
    signed_amount numeric NOT NULL,
    cash_tendered numeric,
    change numeric,
    external_reference character varying(255),
    bank_account_id uuid,
    occurred_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    actor_user_id uuid NOT NULL,
    operation_key character varying(128) NOT NULL,
    reason character varying(500),
    override_reason character varying(500),
    reverses_movement_id uuid,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_till_movements_amount CHECK (((signed_amount <> (0)::numeric) AND (abs(signed_amount) <= 9999999999999999.99) AND (signed_amount = round(signed_amount, 2)))),
    CONSTRAINT ck_till_movements_cash CHECK ((((cash_tendered IS NULL) AND (change IS NULL)) OR ((method = 2) AND (cash_tendered IS NOT NULL) AND (change IS NOT NULL) AND (change >= (0)::numeric) AND (cash_tendered >= abs(signed_amount)) AND ((cash_tendered - change) = abs(signed_amount)) AND (cash_tendered <= 9999999999999999.99) AND (cash_tendered = round(cash_tendered, 2)) AND (change = round(change, 2))))),
    CONSTRAINT ck_till_movements_labels CHECK ((((length(btrim((purpose)::text)) >= 1) AND (length(btrim((purpose)::text)) <= 100)) AND ((length(btrim((operation_key)::text)) >= 1) AND (length(btrim((operation_key)::text)) <= 128)) AND (recorded_at >= occurred_at))),
    CONSTRAINT ck_till_movements_type CHECK ((((method >= 2) AND (method <= 6)) AND ((source_type >= 1) AND (source_type <= 12)) AND (sequence > 0) AND (line_number = 1)))
);

ALTER TABLE ONLY public.till_movements FORCE ROW LEVEL SECURITY;


--
-- Name: till_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.till_sessions (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    till_id uuid NOT NULL,
    cashier_id uuid NOT NULL,
    till_name character varying(100) NOT NULL,
    cashier_name character varying(200) NOT NULL,
    opened_by uuid NOT NULL,
    opened_at timestamp with time zone NOT NULL,
    business_date date NOT NULL,
    business_time_zone_id character varying(64) NOT NULL,
    opening_float numeric NOT NULL,
    state integer NOT NULL,
    last_movement_sequence bigint NOT NULL,
    source_count bigint NOT NULL,
    override_reason character varying(500),
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_till_sessions_amount CHECK (((opening_float >= (0)::numeric) AND (opening_float <= 9999999999999999.99) AND (opening_float = round(opening_float, 2)))),
    CONSTRAINT ck_till_sessions_labels CHECK ((((length(btrim((till_name)::text)) >= 1) AND (length(btrim((till_name)::text)) <= 100)) AND ((length(btrim((cashier_name)::text)) >= 1) AND (length(btrim((cashier_name)::text)) <= 200)) AND ((length(btrim((business_time_zone_id)::text)) >= 1) AND (length(btrim((business_time_zone_id)::text)) <= 64)))),
    CONSTRAINT ck_till_sessions_override CHECK (((opened_by = cashier_id) OR ((override_reason IS NOT NULL) AND ((length(btrim((override_reason)::text)) >= 1) AND (length(btrim((override_reason)::text)) <= 500))))),
    CONSTRAINT ck_till_sessions_sequence CHECK (((last_movement_sequence >= 0) AND (source_count >= 0))),
    CONSTRAINT ck_till_sessions_state CHECK ((state = ANY (ARRAY[1, 2])))
);

ALTER TABLE ONLY public.till_sessions FORCE ROW LEVEL SECURITY;


--
-- Name: till_source_records; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.till_source_records (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    session_id uuid NOT NULL,
    source_type integer NOT NULL,
    source_id uuid NOT NULL,
    purpose character varying(100) NOT NULL,
    operation_key character varying(128) NOT NULL,
    actor_user_id uuid NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    reason character varying(500),
    override_reason character varying(500),
    store_credit_issued numeric NOT NULL,
    store_credit_redeemed numeric NOT NULL,
    receivable_change numeric NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_till_sources_labels CHECK ((((length(btrim((purpose)::text)) >= 1) AND (length(btrim((purpose)::text)) <= 100)) AND ((length(btrim((operation_key)::text)) >= 1) AND (length(btrim((operation_key)::text)) <= 128)))),
    CONSTRAINT ck_till_sources_settlement CHECK (((abs(store_credit_issued) <= 9999999999999999.99) AND (store_credit_issued = round(store_credit_issued, 2)) AND (abs(store_credit_redeemed) <= 9999999999999999.99) AND (store_credit_redeemed = round(store_credit_redeemed, 2)) AND (abs(receivable_change) <= 9999999999999999.99) AND (receivable_change = round(receivable_change, 2)))),
    CONSTRAINT ck_till_sources_times CHECK ((recorded_at >= occurred_at)),
    CONSTRAINT ck_till_sources_type CHECK (((source_type >= 1) AND (source_type <= 12)))
);

ALTER TABLE ONLY public.till_source_records FORCE ROW LEVEL SECURITY;


--
-- Name: tills; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tills (
    id uuid NOT NULL,
    shop_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    normalized_name character varying(100) NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_tills_name CHECK ((((length(btrim((name)::text)) >= 1) AND (length(btrim((name)::text)) <= 100)) AND ((length(btrim((normalized_name)::text)) >= 1) AND (length(btrim((normalized_name)::text)) <= 100))))
);

ALTER TABLE ONLY public.tills FORCE ROW LEVEL SECURITY;


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id uuid NOT NULL,
    email character varying(256),
    phone_number character varying(32),
    password_hash character varying(256),
    first_name character varying(100) NOT NULL,
    last_name character varying(100) NOT NULL,
    language character varying(10) DEFAULT 'en-IN'::character varying NOT NULL,
    is_email_verified boolean NOT NULL,
    is_login_enabled boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone,
    requires_operator_recovery boolean DEFAULT false NOT NULL,
    security_version bigint DEFAULT 0 NOT NULL,
    is_phone_verified boolean DEFAULT false NOT NULL,
    phone_verified_at timestamp with time zone
);


--
-- Data for Name: account_license_restrictions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: applied_shop_capacity; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: applied_shop_reductions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: auth_sessions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: bank_accounts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: batch_disposition_changes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: batch_expiry_revisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: batch_price_revisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: cache_entries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: capacity_reservation_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_exception_consent_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_replacement_activations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_replacement_consent_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_slot_activations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_slot_consent_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: coverage_transfer_consent_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: credit_note_redemptions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: credit_notes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: customer_ledger_entries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: customers; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: discount_rules; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: email_ownership_challenges; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_messages; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_outbox; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_reconciliation_requests; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_request_uses; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: entitlement_snapshot_inbox; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: expense_categories; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: expenses; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: expiry_policy_revisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: hsn_cache; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: identity_mail; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: identity_sms; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: inventory; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: inventory_adjustments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: inventory_batches; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: invoice_leases; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: invoice_sequences; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: item_barcode_sequences; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: items; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: license_grant_evidence; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: license_history_review_observations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: merchant_coverage_exception_activations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: merchant_coverage_exception_cutoffs; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: merchant_coverage_exception_decisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: merchant_transfer_decisions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: offline_expiry_evidence; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: offline_license_grants; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: offline_license_permits; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: offline_license_verification_keys; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: offline_submissions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: operation_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: password_reset_tokens; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_allocations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: payment_reversals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: phone_challenges; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: posting_period_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: posting_periods; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_order_lines; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_order_receipt_lines; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_order_receipt_sequences; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_order_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_order_sequences; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_orders; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_receipt_cost_adjustments; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_receipt_reversals; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: purchase_receipt_values; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: rate_limit_buckets; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: realtime_connection_leases; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: reconciliation_case_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: reconciliation_issues; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: reduction_manifest_pages; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: reduction_manifest_shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: reduction_reservation_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: refresh_tokens; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sale_cancellation_lines; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sale_cancellations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sale_items; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sale_return_items; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sale_return_settlements; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sale_returns; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: sales; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: service_code_sequences; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: services; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_affiliation_consent_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_affiliation_delivery_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_closure_consents; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_expiry_states; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_invitations; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_license_states; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_memberships; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_setup_preferences; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_till_setting_events; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_till_settings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_trial_consents; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shop_trial_receipts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: shops; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: stock_counts; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: stock_transactions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: supplier_ledger_entries; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: supplier_return_credit_lines; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: supplier_return_credits; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: supplier_return_lines; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: supplier_returns; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: suppliers; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: till_close_reviews; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: till_closes; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: till_late_postings; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: till_movements; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: till_sessions; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: till_source_records; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: tills; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Data for Name: users; Type: TABLE DATA; Schema: public; Owner: -
--



--
-- Name: bank_accounts ak_bank_accounts_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bank_accounts
    ADD CONSTRAINT ak_bank_accounts_id_shop_id UNIQUE (id, shop_id);


--
-- Name: batch_disposition_changes ak_batch_disposition_changes_id_inventory_batch_id_item_id_sho; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT ak_batch_disposition_changes_id_inventory_batch_id_item_id_sho UNIQUE (id, inventory_batch_id, item_id, shop_id);


--
-- Name: batch_disposition_changes ak_batch_disposition_changes_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT ak_batch_disposition_changes_id_shop_id UNIQUE (id, shop_id);


--
-- Name: customer_ledger_entries ak_customer_ledger_entries_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_ledger_entries
    ADD CONSTRAINT ak_customer_ledger_entries_id_shop_id UNIQUE (id, shop_id);


--
-- Name: inventory_batches ak_inventory_batches_id_item_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_batches
    ADD CONSTRAINT ak_inventory_batches_id_item_id_shop_id UNIQUE (id, item_id, shop_id);


--
-- Name: invoice_leases ak_invoice_leases_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_leases
    ADD CONSTRAINT ak_invoice_leases_id_shop_id UNIQUE (id, shop_id);


--
-- Name: items ak_items_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.items
    ADD CONSTRAINT ak_items_id_shop_id UNIQUE (id, shop_id);


--
-- Name: offline_license_grants ak_offline_license_grants_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_grants
    ADD CONSTRAINT ak_offline_license_grants_id_shop_id UNIQUE (id, shop_id);


--
-- Name: offline_license_permits ak_offline_license_permits_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_permits
    ADD CONSTRAINT ak_offline_license_permits_id_shop_id UNIQUE (id, shop_id);


--
-- Name: offline_submissions ak_offline_submissions_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_submissions
    ADD CONSTRAINT ak_offline_submissions_id_shop_id UNIQUE (id, shop_id);


--
-- Name: purchase_receipt_reversals ak_purchase_receipt_reversals_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_reversals
    ADD CONSTRAINT ak_purchase_receipt_reversals_id_shop_id UNIQUE (id, shop_id);


--
-- Name: purchase_receipt_values ak_purchase_receipt_values_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_values
    ADD CONSTRAINT ak_purchase_receipt_values_id_shop_id UNIQUE (id, shop_id);


--
-- Name: reconciliation_issues ak_reconciliation_issues_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT ak_reconciliation_issues_id_shop_id UNIQUE (id, shop_id);


--
-- Name: sale_cancellations ak_sale_cancellations_id_sale_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellations
    ADD CONSTRAINT ak_sale_cancellations_id_sale_id_shop_id UNIQUE (id, sale_id, shop_id);


--
-- Name: sale_items ak_sale_items_id_sale_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT ak_sale_items_id_sale_id_shop_id UNIQUE (id, sale_id, shop_id);


--
-- Name: sales ak_sales_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT ak_sales_id_shop_id UNIQUE (id, shop_id);


--
-- Name: shop_memberships ak_shop_memberships_shop_id_user_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_memberships
    ADD CONSTRAINT ak_shop_memberships_shop_id_user_id UNIQUE (shop_id, user_id);


--
-- Name: stock_transactions ak_stock_transactions_id_item_id_inventory_batch_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_transactions
    ADD CONSTRAINT ak_stock_transactions_id_item_id_inventory_batch_id_shop_id UNIQUE (id, item_id, inventory_batch_id, shop_id);


--
-- Name: stock_transactions ak_stock_transactions_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_transactions
    ADD CONSTRAINT ak_stock_transactions_id_shop_id UNIQUE (id, shop_id);


--
-- Name: supplier_ledger_entries ak_supplier_ledger_entries_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_ledger_entries
    ADD CONSTRAINT ak_supplier_ledger_entries_id_shop_id UNIQUE (id, shop_id);


--
-- Name: supplier_return_credit_lines ak_supplier_return_credit_lines_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT ak_supplier_return_credit_lines_id_shop_id UNIQUE (id, shop_id);


--
-- Name: supplier_return_credits ak_supplier_return_credits_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credits
    ADD CONSTRAINT ak_supplier_return_credits_id_shop_id UNIQUE (id, shop_id);


--
-- Name: supplier_return_lines ak_supplier_return_lines_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT ak_supplier_return_lines_id_shop_id UNIQUE (id, shop_id);


--
-- Name: supplier_returns ak_supplier_returns_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_returns
    ADD CONSTRAINT ak_supplier_returns_id_shop_id UNIQUE (id, shop_id);


--
-- Name: suppliers ak_suppliers_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suppliers
    ADD CONSTRAINT ak_suppliers_id_shop_id UNIQUE (id, shop_id);


--
-- Name: till_closes ak_till_closes_id_session_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_closes
    ADD CONSTRAINT ak_till_closes_id_session_id_shop_id UNIQUE (id, session_id, shop_id);


--
-- Name: till_movements ak_till_movements_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT ak_till_movements_id_shop_id UNIQUE (id, shop_id);


--
-- Name: till_sessions ak_till_sessions_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_sessions
    ADD CONSTRAINT ak_till_sessions_id_shop_id UNIQUE (id, shop_id);


--
-- Name: till_source_records ak_till_source_records_id_session_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_source_records
    ADD CONSTRAINT ak_till_source_records_id_session_id_shop_id UNIQUE (id, session_id, shop_id);


--
-- Name: tills ak_tills_id_shop_id; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tills
    ADD CONSTRAINT ak_tills_id_shop_id UNIQUE (id, shop_id);


--
-- Name: applied_shop_capacity applied_shop_capacity_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.applied_shop_capacity
    ADD CONSTRAINT applied_shop_capacity_pkey PRIMARY KEY (shop_id, term_id, revision);


--
-- Name: applied_shop_reductions applied_shop_reductions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.applied_shop_reductions
    ADD CONSTRAINT applied_shop_reductions_pkey PRIMARY KEY (shop_id, predecessor_term_id, successor_term_id);


--
-- Name: cache_entries cache_entries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cache_entries
    ADD CONSTRAINT cache_entries_pkey PRIMARY KEY (id);


--
-- Name: capacity_reservation_receipts capacity_reservation_receipts_account_id_order_id_phase_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reservation_receipts
    ADD CONSTRAINT capacity_reservation_receipts_account_id_order_id_phase_key UNIQUE (account_id, order_id, phase);


--
-- Name: capacity_reservation_receipts capacity_reservation_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.capacity_reservation_receipts
    ADD CONSTRAINT capacity_reservation_receipts_pkey PRIMARY KEY (event_id);


--
-- Name: coverage_exception_consent_events coverage_exception_consent_ev_shop_id_actor_id_operation_ke_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_exception_consent_events
    ADD CONSTRAINT coverage_exception_consent_ev_shop_id_actor_id_operation_ke_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: coverage_exception_consent_events coverage_exception_consent_events_exception_id_leg_kind_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_exception_consent_events
    ADD CONSTRAINT coverage_exception_consent_events_exception_id_leg_kind_key UNIQUE (exception_id, leg, kind);


--
-- Name: coverage_exception_consent_events coverage_exception_consent_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_exception_consent_events
    ADD CONSTRAINT coverage_exception_consent_events_pkey PRIMARY KEY (id);


--
-- Name: coverage_replacement_activations coverage_replacement_activations_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_activations
    ADD CONSTRAINT coverage_replacement_activations_event_id_key UNIQUE (event_id);


--
-- Name: coverage_replacement_activations coverage_replacement_activations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_activations
    ADD CONSTRAINT coverage_replacement_activations_pkey PRIMARY KEY (replacement_id);


--
-- Name: coverage_replacement_consent_events coverage_replacement_consent__shop_id_actor_id_operation_ke_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_consent_events
    ADD CONSTRAINT coverage_replacement_consent__shop_id_actor_id_operation_ke_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: coverage_replacement_consent_events coverage_replacement_consent_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_consent_events
    ADD CONSTRAINT coverage_replacement_consent_events_pkey PRIMARY KEY (id);


--
-- Name: coverage_replacement_consent_events coverage_replacement_consent_events_replacement_id_kind_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_consent_events
    ADD CONSTRAINT coverage_replacement_consent_events_replacement_id_kind_key UNIQUE (replacement_id, kind);


--
-- Name: coverage_slot_activations coverage_slot_activations_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_activations
    ADD CONSTRAINT coverage_slot_activations_event_id_key UNIQUE (event_id);


--
-- Name: coverage_slot_activations coverage_slot_activations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_activations
    ADD CONSTRAINT coverage_slot_activations_pkey PRIMARY KEY (assignment_id);


--
-- Name: coverage_slot_consent_events coverage_slot_consent_events_assignment_id_kind_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_consent_events
    ADD CONSTRAINT coverage_slot_consent_events_assignment_id_kind_key UNIQUE (assignment_id, kind);


--
-- Name: coverage_slot_consent_events coverage_slot_consent_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_consent_events
    ADD CONSTRAINT coverage_slot_consent_events_pkey PRIMARY KEY (id);


--
-- Name: coverage_slot_consent_events coverage_slot_consent_events_shop_id_actor_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_consent_events
    ADD CONSTRAINT coverage_slot_consent_events_shop_id_actor_id_operation_key_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: coverage_transfer_consent_events coverage_transfer_consent_eve_shop_id_actor_id_operation_ke_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consent_events
    ADD CONSTRAINT coverage_transfer_consent_eve_shop_id_actor_id_operation_ke_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: coverage_transfer_consent_events coverage_transfer_consent_event_transfer_id_generation_kind_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consent_events
    ADD CONSTRAINT coverage_transfer_consent_event_transfer_id_generation_kind_key UNIQUE (transfer_id, generation, kind);


--
-- Name: coverage_transfer_consent_events coverage_transfer_consent_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consent_events
    ADD CONSTRAINT coverage_transfer_consent_events_pkey PRIMARY KEY (id);


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
-- Name: entitlement_reconciliation_requests entitlement_reconciliation_requests_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_reconciliation_requests
    ADD CONSTRAINT entitlement_reconciliation_requests_pkey PRIMARY KEY (shop_id);


--
-- Name: entitlement_request_uses entitlement_request_uses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_request_uses
    ADD CONSTRAINT entitlement_request_uses_pkey PRIMARY KEY (replay_id);


--
-- Name: entitlement_snapshot_inbox entitlement_snapshot_inbox_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_snapshot_inbox
    ADD CONSTRAINT entitlement_snapshot_inbox_pkey PRIMARY KEY (event_id);


--
-- Name: merchant_coverage_exception_activations merchant_coverage_exception_activations_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_activations
    ADD CONSTRAINT merchant_coverage_exception_activations_event_id_key UNIQUE (event_id);


--
-- Name: merchant_coverage_exception_activations merchant_coverage_exception_activations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_activations
    ADD CONSTRAINT merchant_coverage_exception_activations_pkey PRIMARY KEY (exception_id, leg);


--
-- Name: merchant_coverage_exception_cutoffs merchant_coverage_exception_cutoffs_decision_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_cutoffs
    ADD CONSTRAINT merchant_coverage_exception_cutoffs_decision_event_id_key UNIQUE (decision_event_id);


--
-- Name: merchant_coverage_exception_cutoffs merchant_coverage_exception_cutoffs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_cutoffs
    ADD CONSTRAINT merchant_coverage_exception_cutoffs_pkey PRIMARY KEY (exception_id);


--
-- Name: merchant_coverage_exception_decisions merchant_coverage_exception_d_exception_id_generation_phase_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_decisions
    ADD CONSTRAINT merchant_coverage_exception_d_exception_id_generation_phase_key UNIQUE (exception_id, generation, phase);


--
-- Name: merchant_coverage_exception_decisions merchant_coverage_exception_decisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_decisions
    ADD CONSTRAINT merchant_coverage_exception_decisions_pkey PRIMARY KEY (event_id);


--
-- Name: merchant_transfer_decisions merchant_transfer_decisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_transfer_decisions
    ADD CONSTRAINT merchant_transfer_decisions_pkey PRIMARY KEY (event_id);


--
-- Name: merchant_transfer_decisions merchant_transfer_decisions_transfer_id_phase_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_transfer_decisions
    ADD CONSTRAINT merchant_transfer_decisions_transfer_id_phase_key UNIQUE (transfer_id, phase);


--
-- Name: account_license_restrictions pk_account_license_restrictions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_license_restrictions
    ADD CONSTRAINT pk_account_license_restrictions PRIMARY KEY (account_id);


--
-- Name: auth_sessions pk_auth_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_sessions
    ADD CONSTRAINT pk_auth_sessions PRIMARY KEY (id);


--
-- Name: bank_accounts pk_bank_accounts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bank_accounts
    ADD CONSTRAINT pk_bank_accounts PRIMARY KEY (id);


--
-- Name: batch_disposition_changes pk_batch_disposition_changes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT pk_batch_disposition_changes PRIMARY KEY (id);


--
-- Name: batch_expiry_revisions pk_batch_expiry_revisions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_expiry_revisions
    ADD CONSTRAINT pk_batch_expiry_revisions PRIMARY KEY (id);


--
-- Name: batch_price_revisions pk_batch_price_revisions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_price_revisions
    ADD CONSTRAINT pk_batch_price_revisions PRIMARY KEY (id);


--
-- Name: credit_note_redemptions pk_credit_note_redemptions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_note_redemptions
    ADD CONSTRAINT pk_credit_note_redemptions PRIMARY KEY (id);


--
-- Name: credit_notes pk_credit_notes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_notes
    ADD CONSTRAINT pk_credit_notes PRIMARY KEY (id);


--
-- Name: customer_ledger_entries pk_customer_ledger_entries; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_ledger_entries
    ADD CONSTRAINT pk_customer_ledger_entries PRIMARY KEY (id);


--
-- Name: customers pk_customers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT pk_customers PRIMARY KEY (id);


--
-- Name: discount_rules pk_discount_rules; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.discount_rules
    ADD CONSTRAINT pk_discount_rules PRIMARY KEY (id);


--
-- Name: email_ownership_challenges pk_email_ownership_challenges; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.email_ownership_challenges
    ADD CONSTRAINT pk_email_ownership_challenges PRIMARY KEY (id);


--
-- Name: expense_categories pk_expense_categories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expense_categories
    ADD CONSTRAINT pk_expense_categories PRIMARY KEY (id);


--
-- Name: expenses pk_expenses; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT pk_expenses PRIMARY KEY (id);


--
-- Name: expiry_policy_revisions pk_expiry_policy_revisions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expiry_policy_revisions
    ADD CONSTRAINT pk_expiry_policy_revisions PRIMARY KEY (id);


--
-- Name: hsn_cache pk_hsn_cache; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.hsn_cache
    ADD CONSTRAINT pk_hsn_cache PRIMARY KEY (id);


--
-- Name: identity_mail pk_identity_mail; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.identity_mail
    ADD CONSTRAINT pk_identity_mail PRIMARY KEY (id);


--
-- Name: identity_sms pk_identity_sms; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.identity_sms
    ADD CONSTRAINT pk_identity_sms PRIMARY KEY (id);


--
-- Name: inventory pk_inventory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT pk_inventory PRIMARY KEY (id);


--
-- Name: inventory_adjustments pk_inventory_adjustments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_adjustments
    ADD CONSTRAINT pk_inventory_adjustments PRIMARY KEY (id);


--
-- Name: inventory_batches pk_inventory_batches; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_batches
    ADD CONSTRAINT pk_inventory_batches PRIMARY KEY (id);


--
-- Name: invoice_leases pk_invoice_leases; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_leases
    ADD CONSTRAINT pk_invoice_leases PRIMARY KEY (id);


--
-- Name: invoice_sequences pk_invoice_sequences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_sequences
    ADD CONSTRAINT pk_invoice_sequences PRIMARY KEY (id);


--
-- Name: item_barcode_sequences pk_item_barcode_sequences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.item_barcode_sequences
    ADD CONSTRAINT pk_item_barcode_sequences PRIMARY KEY (id);


--
-- Name: items pk_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.items
    ADD CONSTRAINT pk_items PRIMARY KEY (id);


--
-- Name: license_grant_evidence pk_license_grant_evidence; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.license_grant_evidence
    ADD CONSTRAINT pk_license_grant_evidence PRIMARY KEY (event_id);


--
-- Name: license_history_review_observations pk_license_history_review_observations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.license_history_review_observations
    ADD CONSTRAINT pk_license_history_review_observations PRIMARY KEY (id);


--
-- Name: offline_expiry_evidence pk_offline_expiry_evidence; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_expiry_evidence
    ADD CONSTRAINT pk_offline_expiry_evidence PRIMARY KEY (id);


--
-- Name: offline_license_grants pk_offline_license_grants; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_grants
    ADD CONSTRAINT pk_offline_license_grants PRIMARY KEY (id);


--
-- Name: offline_license_permits pk_offline_license_permits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_permits
    ADD CONSTRAINT pk_offline_license_permits PRIMARY KEY (id);


--
-- Name: offline_license_verification_keys pk_offline_license_verification_keys; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_verification_keys
    ADD CONSTRAINT pk_offline_license_verification_keys PRIMARY KEY (id);


--
-- Name: offline_submissions pk_offline_submissions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_submissions
    ADD CONSTRAINT pk_offline_submissions PRIMARY KEY (id);


--
-- Name: operation_receipts pk_operation_receipts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operation_receipts
    ADD CONSTRAINT pk_operation_receipts PRIMARY KEY (id);


--
-- Name: password_reset_tokens pk_password_reset_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.password_reset_tokens
    ADD CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id);


--
-- Name: payment_allocations pk_payment_allocations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_allocations
    ADD CONSTRAINT pk_payment_allocations PRIMARY KEY (id);


--
-- Name: payment_receipts pk_payment_receipts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_receipts
    ADD CONSTRAINT pk_payment_receipts PRIMARY KEY (id);


--
-- Name: payment_reversals pk_payment_reversals; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_reversals
    ADD CONSTRAINT pk_payment_reversals PRIMARY KEY (id);


--
-- Name: phone_challenges pk_phone_challenges; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.phone_challenges
    ADD CONSTRAINT pk_phone_challenges PRIMARY KEY (id);


--
-- Name: posting_period_events pk_posting_period_events; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.posting_period_events
    ADD CONSTRAINT pk_posting_period_events PRIMARY KEY (id);


--
-- Name: posting_periods pk_posting_periods; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.posting_periods
    ADD CONSTRAINT pk_posting_periods PRIMARY KEY (id);


--
-- Name: purchase_order_lines pk_purchase_order_lines; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_lines
    ADD CONSTRAINT pk_purchase_order_lines PRIMARY KEY (id);


--
-- Name: purchase_order_receipt_lines pk_purchase_order_receipt_lines; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_lines
    ADD CONSTRAINT pk_purchase_order_receipt_lines PRIMARY KEY (id);


--
-- Name: purchase_order_receipt_sequences pk_purchase_order_receipt_sequences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_sequences
    ADD CONSTRAINT pk_purchase_order_receipt_sequences PRIMARY KEY (id);


--
-- Name: purchase_order_receipts pk_purchase_order_receipts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipts
    ADD CONSTRAINT pk_purchase_order_receipts PRIMARY KEY (id);


--
-- Name: purchase_order_sequences pk_purchase_order_sequences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_sequences
    ADD CONSTRAINT pk_purchase_order_sequences PRIMARY KEY (id);


--
-- Name: purchase_orders pk_purchase_orders; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_orders
    ADD CONSTRAINT pk_purchase_orders PRIMARY KEY (id);


--
-- Name: purchase_receipt_cost_adjustments pk_purchase_receipt_cost_adjustments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_cost_adjustments
    ADD CONSTRAINT pk_purchase_receipt_cost_adjustments PRIMARY KEY (id);


--
-- Name: purchase_receipt_reversals pk_purchase_receipt_reversals; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_reversals
    ADD CONSTRAINT pk_purchase_receipt_reversals PRIMARY KEY (id);


--
-- Name: purchase_receipt_values pk_purchase_receipt_values; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_values
    ADD CONSTRAINT pk_purchase_receipt_values PRIMARY KEY (id);


--
-- Name: rate_limit_buckets pk_rate_limit_buckets; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.rate_limit_buckets
    ADD CONSTRAINT pk_rate_limit_buckets PRIMARY KEY (policy_id, bucket_key);


--
-- Name: realtime_connection_leases pk_realtime_connection_leases; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.realtime_connection_leases
    ADD CONSTRAINT pk_realtime_connection_leases PRIMARY KEY (id);


--
-- Name: reconciliation_case_events pk_reconciliation_case_events; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_case_events
    ADD CONSTRAINT pk_reconciliation_case_events PRIMARY KEY (id);


--
-- Name: reconciliation_issues pk_reconciliation_issues; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT pk_reconciliation_issues PRIMARY KEY (id);


--
-- Name: refresh_tokens pk_refresh_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT pk_refresh_tokens PRIMARY KEY (id);


--
-- Name: sale_cancellation_lines pk_sale_cancellation_lines; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT pk_sale_cancellation_lines PRIMARY KEY (id);


--
-- Name: sale_cancellations pk_sale_cancellations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellations
    ADD CONSTRAINT pk_sale_cancellations PRIMARY KEY (id);


--
-- Name: sale_items pk_sale_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT pk_sale_items PRIMARY KEY (id);


--
-- Name: sale_return_items pk_sale_return_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_items
    ADD CONSTRAINT pk_sale_return_items PRIMARY KEY (id);


--
-- Name: sale_return_settlements pk_sale_return_settlements; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_settlements
    ADD CONSTRAINT pk_sale_return_settlements PRIMARY KEY (id);


--
-- Name: sale_returns pk_sale_returns; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_returns
    ADD CONSTRAINT pk_sale_returns PRIMARY KEY (id);


--
-- Name: sales pk_sales; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT pk_sales PRIMARY KEY (id);


--
-- Name: service_code_sequences pk_service_code_sequences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_code_sequences
    ADD CONSTRAINT pk_service_code_sequences PRIMARY KEY (id);


--
-- Name: services pk_services; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.services
    ADD CONSTRAINT pk_services PRIMARY KEY (id);


--
-- Name: shop_expiry_states pk_shop_expiry_states; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_expiry_states
    ADD CONSTRAINT pk_shop_expiry_states PRIMARY KEY (shop_id);


--
-- Name: shop_invitations pk_shop_invitations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_invitations
    ADD CONSTRAINT pk_shop_invitations PRIMARY KEY (id);


--
-- Name: shop_license_states pk_shop_license_states; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_license_states
    ADD CONSTRAINT pk_shop_license_states PRIMARY KEY (shop_id);


--
-- Name: shop_memberships pk_shop_memberships; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_memberships
    ADD CONSTRAINT pk_shop_memberships PRIMARY KEY (id);


--
-- Name: shop_setup_preferences pk_shop_setup_preferences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_setup_preferences
    ADD CONSTRAINT pk_shop_setup_preferences PRIMARY KEY (shop_id);


--
-- Name: shop_till_setting_events pk_shop_till_setting_events; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_till_setting_events
    ADD CONSTRAINT pk_shop_till_setting_events PRIMARY KEY (id);


--
-- Name: shop_till_settings pk_shop_till_settings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_till_settings
    ADD CONSTRAINT pk_shop_till_settings PRIMARY KEY (shop_id);


--
-- Name: shops pk_shops; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shops
    ADD CONSTRAINT pk_shops PRIMARY KEY (id);


--
-- Name: stock_counts pk_stock_counts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_counts
    ADD CONSTRAINT pk_stock_counts PRIMARY KEY (id);


--
-- Name: stock_transactions pk_stock_transactions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_transactions
    ADD CONSTRAINT pk_stock_transactions PRIMARY KEY (id);


--
-- Name: supplier_ledger_entries pk_supplier_ledger_entries; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_ledger_entries
    ADD CONSTRAINT pk_supplier_ledger_entries PRIMARY KEY (id);


--
-- Name: supplier_return_credit_lines pk_supplier_return_credit_lines; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT pk_supplier_return_credit_lines PRIMARY KEY (id);


--
-- Name: supplier_return_credits pk_supplier_return_credits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credits
    ADD CONSTRAINT pk_supplier_return_credits PRIMARY KEY (id);


--
-- Name: supplier_return_lines pk_supplier_return_lines; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT pk_supplier_return_lines PRIMARY KEY (id);


--
-- Name: supplier_returns pk_supplier_returns; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_returns
    ADD CONSTRAINT pk_supplier_returns PRIMARY KEY (id);


--
-- Name: suppliers pk_suppliers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suppliers
    ADD CONSTRAINT pk_suppliers PRIMARY KEY (id);


--
-- Name: till_close_reviews pk_till_close_reviews; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_close_reviews
    ADD CONSTRAINT pk_till_close_reviews PRIMARY KEY (id);


--
-- Name: till_closes pk_till_closes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_closes
    ADD CONSTRAINT pk_till_closes PRIMARY KEY (id);


--
-- Name: till_late_postings pk_till_late_postings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT pk_till_late_postings PRIMARY KEY (id);


--
-- Name: till_movements pk_till_movements; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT pk_till_movements PRIMARY KEY (id);


--
-- Name: till_sessions pk_till_sessions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_sessions
    ADD CONSTRAINT pk_till_sessions PRIMARY KEY (id);


--
-- Name: till_source_records pk_till_source_records; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_source_records
    ADD CONSTRAINT pk_till_source_records PRIMARY KEY (id);


--
-- Name: tills pk_tills; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tills
    ADD CONSTRAINT pk_tills PRIMARY KEY (id);


--
-- Name: users pk_users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT pk_users PRIMARY KEY (id);


--
-- Name: reduction_manifest_pages reduction_manifest_pages_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reduction_manifest_pages
    ADD CONSTRAINT reduction_manifest_pages_event_id_key UNIQUE (event_id);


--
-- Name: reduction_manifest_pages reduction_manifest_pages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reduction_manifest_pages
    ADD CONSTRAINT reduction_manifest_pages_pkey PRIMARY KEY (account_id, schedule_id, generation, page_index);


--
-- Name: reduction_manifest_shops reduction_manifest_shops_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reduction_manifest_shops
    ADD CONSTRAINT reduction_manifest_shops_pkey PRIMARY KEY (account_id, schedule_id, generation, shop_id);


--
-- Name: reduction_reservation_receipts reduction_reservation_receipt_account_id_schedule_id_genera_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reduction_reservation_receipts
    ADD CONSTRAINT reduction_reservation_receipt_account_id_schedule_id_genera_key UNIQUE (account_id, schedule_id, generation, phase);


--
-- Name: reduction_reservation_receipts reduction_reservation_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reduction_reservation_receipts
    ADD CONSTRAINT reduction_reservation_receipts_pkey PRIMARY KEY (event_id);


--
-- Name: shop_affiliation_consent_events shop_affiliation_consent_even_shop_id_actor_id_operation_ke_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_consent_events
    ADD CONSTRAINT shop_affiliation_consent_even_shop_id_actor_id_operation_ke_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: shop_affiliation_consent_events shop_affiliation_consent_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_consent_events
    ADD CONSTRAINT shop_affiliation_consent_events_pkey PRIMARY KEY (id);


--
-- Name: shop_affiliation_consent_events shop_affiliation_consent_events_shop_id_request_id_kind_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_consent_events
    ADD CONSTRAINT shop_affiliation_consent_events_shop_id_request_id_kind_key UNIQUE (shop_id, request_id, kind);


--
-- Name: shop_affiliation_delivery_receipts shop_affiliation_delivery_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_delivery_receipts
    ADD CONSTRAINT shop_affiliation_delivery_receipts_pkey PRIMARY KEY (event_id);


--
-- Name: shop_closure_consents shop_closure_consents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_closure_consents
    ADD CONSTRAINT shop_closure_consents_pkey PRIMARY KEY (id);


--
-- Name: shop_closure_consents shop_closure_consents_shop_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_closure_consents
    ADD CONSTRAINT shop_closure_consents_shop_id_operation_key_key UNIQUE (shop_id, operation_key);


--
-- Name: shop_trial_consents shop_trial_consents_event_id_shop_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_consents
    ADD CONSTRAINT shop_trial_consents_event_id_shop_id_key UNIQUE (event_id, shop_id);


--
-- Name: shop_trial_consents shop_trial_consents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_consents
    ADD CONSTRAINT shop_trial_consents_pkey PRIMARY KEY (event_id);


--
-- Name: shop_trial_consents shop_trial_consents_shop_id_actor_id_operation_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_consents
    ADD CONSTRAINT shop_trial_consents_shop_id_actor_id_operation_key_key UNIQUE (shop_id, actor_id, operation_key);


--
-- Name: shop_trial_receipts shop_trial_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_receipts
    ADD CONSTRAINT shop_trial_receipts_pkey PRIMARY KEY (event_id);


--
-- Name: coverage_replacement_accepted_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX coverage_replacement_accepted_shop ON public.entitlement_messages USING btree (account_id, shop_id) WHERE (kind = 'CoverageReplacementConsent'::text);


--
-- Name: coverage_slot_accepted_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX coverage_slot_accepted_shop ON public.entitlement_messages USING btree (account_id, shop_id) WHERE (kind = 'CoverageSlotConsent'::text);


--
-- Name: entitlement_outbox_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX entitlement_outbox_due ON public.entitlement_outbox USING btree (next_attempt_at, event_id) WHERE (status = ANY (ARRAY['Pending'::text, 'Leased'::text, 'AwaitingAcknowledgement'::text]));


--
-- Name: ix_account_license_restrictions_evidence_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_account_license_restrictions_evidence_id ON public.account_license_restrictions USING btree (evidence_id);


--
-- Name: ix_auth_sessions_user_id_revoked_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_auth_sessions_user_id_revoked_at ON public.auth_sessions USING btree (user_id, revoked_at);


--
-- Name: ix_bank_accounts_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_bank_accounts_shop_id ON public.bank_accounts USING btree (shop_id);


--
-- Name: ix_batch_disposition_changes_inventory_adjustment_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_disposition_changes_inventory_adjustment_id ON public.batch_disposition_changes USING btree (inventory_adjustment_id);


--
-- Name: ix_batch_disposition_changes_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_disposition_changes_inventory_batch_id_item_id_shop_id ON public.batch_disposition_changes USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_batch_disposition_changes_root_change_id_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_disposition_changes_root_change_id_inventory_batch_id ON public.batch_disposition_changes USING btree (root_change_id, inventory_batch_id, item_id, shop_id);


--
-- Name: ix_batch_disposition_changes_shop_id_inventory_batch_id_record; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_disposition_changes_shop_id_inventory_batch_id_record ON public.batch_disposition_changes USING btree (shop_id, inventory_batch_id, recorded_at);


--
-- Name: ix_batch_disposition_changes_shop_id_source_kind_source_id_sou; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_batch_disposition_changes_shop_id_source_kind_source_id_sou ON public.batch_disposition_changes USING btree (shop_id, source_kind, source_id, source_line_id) WHERE (root_change_id IS NULL);


--
-- Name: ix_batch_disposition_changes_stock_movement_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_disposition_changes_stock_movement_id ON public.batch_disposition_changes USING btree (stock_movement_id);


--
-- Name: ix_batch_expiry_revisions_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_expiry_revisions_inventory_batch_id_item_id_shop_id ON public.batch_expiry_revisions USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_batch_expiry_revisions_shop_id_catalog_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_batch_expiry_revisions_shop_id_catalog_version ON public.batch_expiry_revisions USING btree (shop_id, catalog_version);


--
-- Name: ix_batch_expiry_revisions_shop_id_inventory_batch_id_effective; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_expiry_revisions_shop_id_inventory_batch_id_effective ON public.batch_expiry_revisions USING btree (shop_id, inventory_batch_id, effective_at);


--
-- Name: ix_batch_price_revisions_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_price_revisions_inventory_batch_id_item_id_shop_id ON public.batch_price_revisions USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_batch_price_revisions_shop_id_inventory_batch_id_performed_; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_batch_price_revisions_shop_id_inventory_batch_id_performed_ ON public.batch_price_revisions USING btree (shop_id, inventory_batch_id, performed_at);


--
-- Name: ix_credit_note_redemptions_credit_note_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_note_redemptions_credit_note_id ON public.credit_note_redemptions USING btree (credit_note_id);


--
-- Name: ix_credit_note_redemptions_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_note_redemptions_sale_id ON public.credit_note_redemptions USING btree (sale_id);


--
-- Name: ix_credit_note_redemptions_shop_id_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_note_redemptions_shop_id_sale_id ON public.credit_note_redemptions USING btree (shop_id, sale_id);


--
-- Name: ix_credit_notes_sale_return_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_notes_sale_return_id ON public.credit_notes USING btree (sale_return_id);


--
-- Name: ix_credit_notes_shop_id_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_credit_notes_shop_id_code ON public.credit_notes USING btree (shop_id, code);


--
-- Name: ix_credit_notes_shop_id_is_voided; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_notes_shop_id_is_voided ON public.credit_notes USING btree (shop_id, is_voided);


--
-- Name: ix_credit_notes_shop_id_sale_return_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_credit_notes_shop_id_sale_return_id ON public.credit_notes USING btree (shop_id, sale_return_id);


--
-- Name: ix_customer_ledger_entries_customer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customer_ledger_entries_customer_id ON public.customer_ledger_entries USING btree (customer_id);


--
-- Name: ix_customer_ledger_entries_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customer_ledger_entries_sale_id ON public.customer_ledger_entries USING btree (sale_id);


--
-- Name: ix_customer_ledger_entries_shop_id_customer_id_entry_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customer_ledger_entries_shop_id_customer_id_entry_date ON public.customer_ledger_entries USING btree (shop_id, customer_id, entry_date);


--
-- Name: ix_customer_ledger_entries_shop_id_sale_id_entry_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customer_ledger_entries_shop_id_sale_id_entry_type ON public.customer_ledger_entries USING btree (shop_id, sale_id, entry_type);


--
-- Name: ix_customers_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customers_shop_id ON public.customers USING btree (shop_id);


--
-- Name: ix_customers_shop_id_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customers_shop_id_is_active ON public.customers USING btree (shop_id, is_active);


--
-- Name: ix_customers_shop_id_phone_number; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_customers_shop_id_phone_number ON public.customers USING btree (shop_id, phone_number);


--
-- Name: ix_discount_rules_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_discount_rules_inventory_batch_id ON public.discount_rules USING btree (inventory_batch_id);


--
-- Name: ix_discount_rules_shop_id_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_discount_rules_shop_id_inventory_batch_id ON public.discount_rules USING btree (shop_id, inventory_batch_id) WHERE (inventory_batch_id IS NOT NULL);


--
-- Name: ix_discount_rules_shop_id_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_discount_rules_shop_id_is_active ON public.discount_rules USING btree (shop_id, is_active);


--
-- Name: ix_discount_rules_shop_id_rule_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_discount_rules_shop_id_rule_type ON public.discount_rules USING btree (shop_id, rule_type);


--
-- Name: ix_email_ownership_challenges_destination_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_email_ownership_challenges_destination_created_at ON public.email_ownership_challenges USING btree (destination, created_at);


--
-- Name: ix_email_ownership_challenges_user_id_purpose; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_email_ownership_challenges_user_id_purpose ON public.email_ownership_challenges USING btree (user_id, purpose);


--
-- Name: ix_expense_categories_shop_id_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_expense_categories_shop_id_name ON public.expense_categories USING btree (shop_id, name);


--
-- Name: ix_expenses_category_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expenses_category_id ON public.expenses USING btree (category_id);


--
-- Name: ix_expenses_original_expense_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expenses_original_expense_id ON public.expenses USING btree (original_expense_id);


--
-- Name: ix_expenses_shop_id_expense_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expenses_shop_id_expense_date ON public.expenses USING btree (shop_id, expense_date);


--
-- Name: ix_expenses_shop_id_is_voided; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expenses_shop_id_is_voided ON public.expenses USING btree (shop_id, is_voided);


--
-- Name: ix_expenses_shop_id_paid_to; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expenses_shop_id_paid_to ON public.expenses USING btree (shop_id, paid_to);


--
-- Name: ix_expenses_supplier_ledger_entry_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expenses_supplier_ledger_entry_id ON public.expenses USING btree (supplier_ledger_entry_id);


--
-- Name: ix_expiresattime; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expiresattime ON public.cache_entries USING btree (expiresattime) WITH (deduplicate_items='true');


--
-- Name: ix_expiry_policy_revisions_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expiry_policy_revisions_item_id_shop_id ON public.expiry_policy_revisions USING btree (item_id, shop_id);


--
-- Name: ix_expiry_policy_revisions_shop_id_catalog_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_expiry_policy_revisions_shop_id_catalog_version ON public.expiry_policy_revisions USING btree (shop_id, catalog_version);


--
-- Name: ix_expiry_policy_revisions_shop_id_item_id_effective_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_expiry_policy_revisions_shop_id_item_id_effective_at ON public.expiry_policy_revisions USING btree (shop_id, item_id, effective_at);


--
-- Name: ix_hsn_cache_product_name; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_hsn_cache_product_name ON public.hsn_cache USING btree (product_name);


--
-- Name: ix_identity_mail_completed_at_next_attempt_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_mail_completed_at_next_attempt_at ON public.identity_mail USING btree (completed_at, next_attempt_at);


--
-- Name: ix_identity_mail_destination_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_mail_destination_created_at ON public.identity_mail USING btree (destination, created_at);


--
-- Name: ix_identity_mail_invitation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_mail_invitation_id ON public.identity_mail USING btree (invitation_id);


--
-- Name: ix_identity_mail_password_reset_token_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_mail_password_reset_token_id ON public.identity_mail USING btree (password_reset_token_id);


--
-- Name: ix_identity_sms_challenge_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_sms_challenge_id ON public.identity_sms USING btree (challenge_id);


--
-- Name: ix_identity_sms_completed_at_next_attempt_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_sms_completed_at_next_attempt_at ON public.identity_sms USING btree (completed_at, next_attempt_at);


--
-- Name: ix_identity_sms_destination_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_identity_sms_destination_created_at ON public.identity_sms USING btree (destination, created_at);


--
-- Name: ix_inventory_adjustments_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_adjustments_inventory_batch_id_item_id_shop_id ON public.inventory_adjustments USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_inventory_adjustments_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_adjustments_item_id_shop_id ON public.inventory_adjustments USING btree (item_id, shop_id);


--
-- Name: ix_inventory_adjustments_reversal_stock_transaction_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_adjustments_reversal_stock_transaction_id ON public.inventory_adjustments USING btree (reversal_stock_transaction_id);


--
-- Name: ix_inventory_adjustments_shop_id_adjustment_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_inventory_adjustments_shop_id_adjustment_number ON public.inventory_adjustments USING btree (shop_id, adjustment_number);


--
-- Name: ix_inventory_adjustments_shop_id_inventory_batch_id_performed_; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_adjustments_shop_id_inventory_batch_id_performed_ ON public.inventory_adjustments USING btree (shop_id, inventory_batch_id, performed_at);


--
-- Name: ix_inventory_adjustments_shop_id_is_voided; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_adjustments_shop_id_is_voided ON public.inventory_adjustments USING btree (shop_id, is_voided);


--
-- Name: ix_inventory_adjustments_shop_id_item_id_performed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_adjustments_shop_id_item_id_performed_at ON public.inventory_adjustments USING btree (shop_id, item_id, performed_at);


--
-- Name: ix_inventory_batches_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_inventory_batches_id_item_id_shop_id ON public.inventory_batches USING btree (id, item_id, shop_id);


--
-- Name: ix_inventory_batches_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_batches_item_id_shop_id ON public.inventory_batches USING btree (item_id, shop_id);


--
-- Name: ix_inventory_batches_shop_id_expiry_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_batches_shop_id_expiry_date ON public.inventory_batches USING btree (shop_id, expiry_date);


--
-- Name: ix_inventory_batches_shop_id_item_id_batch_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_inventory_batches_shop_id_item_id_batch_number ON public.inventory_batches USING btree (shop_id, item_id, batch_number) WHERE (is_voided = false);


--
-- Name: ix_inventory_batches_shop_id_supplier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_batches_shop_id_supplier_id ON public.inventory_batches USING btree (shop_id, supplier_id);


--
-- Name: ix_inventory_batches_supplier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_inventory_batches_supplier_id ON public.inventory_batches USING btree (supplier_id);


--
-- Name: ix_inventory_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_inventory_item_id_shop_id ON public.inventory USING btree (item_id, shop_id);


--
-- Name: ix_inventory_shop_id_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_inventory_shop_id_item_id ON public.inventory USING btree (shop_id, item_id);


--
-- Name: ix_invoice_leases_invoice_sequence_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_invoice_leases_invoice_sequence_id ON public.invoice_leases USING btree (invoice_sequence_id);


--
-- Name: ix_invoice_leases_shop_id_device_id_fiscal_year_start; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_invoice_leases_shop_id_device_id_fiscal_year_start ON public.invoice_leases USING btree (shop_id, device_id, fiscal_year_start);


--
-- Name: ix_invoice_leases_shop_id_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_invoice_leases_shop_id_expires_at ON public.invoice_leases USING btree (shop_id, expires_at);


--
-- Name: ix_invoice_sequences_seller_registration_key_fiscal_year_start; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_invoice_sequences_seller_registration_key_fiscal_year_start ON public.invoice_sequences USING btree (seller_registration_key, fiscal_year_start, series_code);


--
-- Name: ix_invoice_sequences_shop_id_fiscal_year_start_series_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_invoice_sequences_shop_id_fiscal_year_start_series_code ON public.invoice_sequences USING btree (shop_id, fiscal_year_start, series_code);


--
-- Name: ix_item_barcode_sequences_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_item_barcode_sequences_shop_id ON public.item_barcode_sequences USING btree (shop_id);


--
-- Name: ix_items_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_items_id_shop_id ON public.items USING btree (id, shop_id);


--
-- Name: ix_items_shop_id_barcode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_items_shop_id_barcode ON public.items USING btree (shop_id, barcode);


--
-- Name: ix_items_shop_id_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_items_shop_id_is_active ON public.items USING btree (shop_id, is_active);


--
-- Name: ix_items_shop_id_name; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_items_shop_id_name ON public.items USING btree (shop_id, name);


--
-- Name: ix_license_grant_evidence_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_license_grant_evidence_shop_id ON public.license_grant_evidence USING btree (shop_id);


--
-- Name: ix_license_history_review_observations_case_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_license_history_review_observations_case_id_shop_id ON public.license_history_review_observations USING btree (case_id, shop_id);


--
-- Name: ix_license_history_review_observations_competing_submission_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_license_history_review_observations_competing_submission_id ON public.license_history_review_observations USING btree (competing_submission_id, shop_id);


--
-- Name: ix_license_history_review_observations_offline_submission_id_s; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_license_history_review_observations_offline_submission_id_s ON public.license_history_review_observations USING btree (offline_submission_id, shop_id);


--
-- Name: ix_license_history_review_observations_shop_id_case_id_fingerp; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_license_history_review_observations_shop_id_case_id_fingerp ON public.license_history_review_observations USING btree (shop_id, case_id, fingerprint);


--
-- Name: ix_license_history_review_observations_shop_id_case_id_observe; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_license_history_review_observations_shop_id_case_id_observe ON public.license_history_review_observations USING btree (shop_id, case_id, observed_at, id);


--
-- Name: ix_license_history_review_observations_shop_id_offline_submiss; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_license_history_review_observations_shop_id_offline_submiss ON public.license_history_review_observations USING btree (shop_id, offline_submission_id);


--
-- Name: ix_offline_expiry_evidence_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_expiry_evidence_batch_id_item_id_shop_id ON public.offline_expiry_evidence USING btree (batch_id, item_id, shop_id);


--
-- Name: ix_offline_expiry_evidence_shop_id_batch_id_captured_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_expiry_evidence_shop_id_batch_id_captured_at ON public.offline_expiry_evidence USING btree (shop_id, batch_id, captured_at);


--
-- Name: ix_offline_expiry_evidence_shop_id_snapshot_id_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_offline_expiry_evidence_shop_id_snapshot_id_batch_id ON public.offline_expiry_evidence USING btree (shop_id, snapshot_id, batch_id);


--
-- Name: ix_offline_license_grants_lease_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_license_grants_lease_id_shop_id ON public.offline_license_grants USING btree (lease_id, shop_id);


--
-- Name: ix_offline_license_grants_shop_id_lease_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_offline_license_grants_shop_id_lease_id ON public.offline_license_grants USING btree (shop_id, lease_id);


--
-- Name: ix_offline_license_permits_grant_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_license_permits_grant_id_shop_id ON public.offline_license_permits USING btree (grant_id, shop_id);


--
-- Name: ix_offline_license_permits_shop_id_lease_id_valid_until; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_license_permits_shop_id_lease_id_valid_until ON public.offline_license_permits USING btree (shop_id, lease_id, valid_until);


--
-- Name: ix_offline_license_permits_shop_id_snapshot_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_offline_license_permits_shop_id_snapshot_id ON public.offline_license_permits USING btree (shop_id, snapshot_id);


--
-- Name: ix_offline_license_verification_keys_shop_id_key_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_offline_license_verification_keys_shop_id_key_id ON public.offline_license_verification_keys USING btree (shop_id, key_id);


--
-- Name: ix_offline_submissions_licence_grant_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_submissions_licence_grant_id_shop_id ON public.offline_submissions USING btree (licence_grant_id, shop_id);


--
-- Name: ix_offline_submissions_licence_permit_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_submissions_licence_permit_id_shop_id ON public.offline_submissions USING btree (licence_permit_id, shop_id);


--
-- Name: ix_offline_submissions_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_submissions_sale_id ON public.offline_submissions USING btree (sale_id);


--
-- Name: ix_offline_submissions_shop_id_device_id_client_sale_id_payloa; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_offline_submissions_shop_id_device_id_client_sale_id_payloa ON public.offline_submissions USING btree (shop_id, device_id, client_sale_id, payload_hash);


--
-- Name: ix_offline_submissions_shop_id_device_id_submitted_by_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_offline_submissions_shop_id_device_id_submitted_by_user_id ON public.offline_submissions USING btree (shop_id, device_id, submitted_by_user_id);


--
-- Name: ix_operation_receipts_shop_id_operation_type_operation_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_operation_receipts_shop_id_operation_type_operation_key ON public.operation_receipts USING btree (shop_id, operation_type, operation_key);


--
-- Name: ix_password_reset_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_password_reset_tokens_user_id ON public.password_reset_tokens USING btree (user_id);


--
-- Name: ix_payment_allocations_receipt_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_allocations_receipt_id ON public.payment_allocations USING btree (receipt_id);


--
-- Name: ix_payment_allocations_shop_id_receipt_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_allocations_shop_id_receipt_id ON public.payment_allocations USING btree (shop_id, receipt_id);


--
-- Name: ix_payment_allocations_shop_id_target_type_target_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_allocations_shop_id_target_type_target_id ON public.payment_allocations USING btree (shop_id, target_type, target_id);


--
-- Name: ix_payment_receipts_bank_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_receipts_bank_account_id ON public.payment_receipts USING btree (bank_account_id);


--
-- Name: ix_payment_receipts_expense_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_receipts_expense_id ON public.payment_receipts USING btree (expense_id);


--
-- Name: ix_payment_receipts_party_type_ledger_entry_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_payment_receipts_party_type_ledger_entry_id ON public.payment_receipts USING btree (party_type, ledger_entry_id);


--
-- Name: ix_payment_receipts_shop_id_party_type_party_id_business_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_receipts_shop_id_party_type_party_id_business_date ON public.payment_receipts USING btree (shop_id, party_type, party_id, business_date);


--
-- Name: ix_payment_reversals_receipt_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_payment_reversals_receipt_id ON public.payment_reversals USING btree (receipt_id);


--
-- Name: ix_payment_reversals_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_payment_reversals_shop_id ON public.payment_reversals USING btree (shop_id);


--
-- Name: ix_phone_challenges_destination_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_phone_challenges_destination_created_at ON public.phone_challenges USING btree (destination, created_at);


--
-- Name: ix_phone_challenges_user_id_purpose; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_phone_challenges_user_id_purpose ON public.phone_challenges USING btree (user_id, purpose);


--
-- Name: ix_posting_period_events_posting_period_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_posting_period_events_posting_period_id ON public.posting_period_events USING btree (posting_period_id);


--
-- Name: ix_posting_period_events_shop_id_posting_period_id_occurred_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_posting_period_events_shop_id_posting_period_id_occurred_at ON public.posting_period_events USING btree (shop_id, posting_period_id, occurred_at);


--
-- Name: ix_posting_periods_shop_id_start_date; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_posting_periods_shop_id_start_date ON public.posting_periods USING btree (shop_id, start_date);


--
-- Name: ix_purchase_order_lines_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_lines_item_id ON public.purchase_order_lines USING btree (item_id);


--
-- Name: ix_purchase_order_lines_purchase_order_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_lines_purchase_order_id ON public.purchase_order_lines USING btree (purchase_order_id);


--
-- Name: ix_purchase_order_receipt_lines_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_order_receipt_lines_inventory_batch_id ON public.purchase_order_receipt_lines USING btree (inventory_batch_id);


--
-- Name: ix_purchase_order_receipt_lines_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_receipt_lines_item_id ON public.purchase_order_receipt_lines USING btree (item_id);


--
-- Name: ix_purchase_order_receipt_lines_purchase_order_line_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_receipt_lines_purchase_order_line_id ON public.purchase_order_receipt_lines USING btree (purchase_order_line_id);


--
-- Name: ix_purchase_order_receipt_lines_purchase_order_receipt_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_receipt_lines_purchase_order_receipt_id ON public.purchase_order_receipt_lines USING btree (purchase_order_receipt_id);


--
-- Name: ix_purchase_order_receipt_lines_purchase_order_receipt_id_clie; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_order_receipt_lines_purchase_order_receipt_id_clie ON public.purchase_order_receipt_lines USING btree (purchase_order_receipt_id, client_line_id) WHERE (client_line_id IS NOT NULL);


--
-- Name: ix_purchase_order_receipt_lines_stock_transaction_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_order_receipt_lines_stock_transaction_id ON public.purchase_order_receipt_lines USING btree (stock_transaction_id);


--
-- Name: ix_purchase_order_receipt_sequences_shop_id_year; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_order_receipt_sequences_shop_id_year ON public.purchase_order_receipt_sequences USING btree (shop_id, year);


--
-- Name: ix_purchase_order_receipts_purchase_order_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_receipts_purchase_order_id ON public.purchase_order_receipts USING btree (purchase_order_id);


--
-- Name: ix_purchase_order_receipts_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_order_receipts_shop_id ON public.purchase_order_receipts USING btree (shop_id);


--
-- Name: ix_purchase_order_receipts_shop_id_receipt_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_order_receipts_shop_id_receipt_number ON public.purchase_order_receipts USING btree (shop_id, receipt_number);


--
-- Name: ix_purchase_order_sequences_shop_id_year; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_order_sequences_shop_id_year ON public.purchase_order_sequences USING btree (shop_id, year);


--
-- Name: ix_purchase_orders_closed_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_orders_closed_by ON public.purchase_orders USING btree (closed_by);


--
-- Name: ix_purchase_orders_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_orders_shop_id ON public.purchase_orders USING btree (shop_id);


--
-- Name: ix_purchase_orders_shop_id_purchase_order_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_orders_shop_id_purchase_order_number ON public.purchase_orders USING btree (shop_id, purchase_order_number);


--
-- Name: ix_purchase_orders_supplier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_orders_supplier_id ON public.purchase_orders USING btree (supplier_id);


--
-- Name: ix_purchase_receipt_cost_adjustments_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_cost_adjustments_inventory_batch_id ON public.purchase_receipt_cost_adjustments USING btree (inventory_batch_id);


--
-- Name: ix_purchase_receipt_cost_adjustments_purchase_receipt_value_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_cost_adjustments_purchase_receipt_value_id ON public.purchase_receipt_cost_adjustments USING btree (purchase_receipt_value_id);


--
-- Name: ix_purchase_receipt_cost_adjustments_shop_id_purchase_receipt_; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_cost_adjustments_shop_id_purchase_receipt_ ON public.purchase_receipt_cost_adjustments USING btree (shop_id, purchase_receipt_value_id, performed_at);


--
-- Name: ix_purchase_receipt_cost_adjustments_supplier_ledger_entry_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_receipt_cost_adjustments_supplier_ledger_entry_id ON public.purchase_receipt_cost_adjustments USING btree (supplier_ledger_entry_id);


--
-- Name: ix_purchase_receipt_reversals_receipt_value_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_receipt_reversals_receipt_value_id ON public.purchase_receipt_reversals USING btree (receipt_value_id);


--
-- Name: ix_purchase_receipt_reversals_receipt_value_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_reversals_receipt_value_id_shop_id ON public.purchase_receipt_reversals USING btree (receipt_value_id, shop_id);


--
-- Name: ix_purchase_receipt_reversals_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_reversals_shop_id ON public.purchase_receipt_reversals USING btree (shop_id);


--
-- Name: ix_purchase_receipt_reversals_stock_transaction_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_reversals_stock_transaction_id_shop_id ON public.purchase_receipt_reversals USING btree (stock_transaction_id, shop_id);


--
-- Name: ix_purchase_receipt_reversals_supplier_ledger_entry_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_reversals_supplier_ledger_entry_id_shop_id ON public.purchase_receipt_reversals USING btree (supplier_ledger_entry_id, shop_id);


--
-- Name: ix_purchase_receipt_values_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_values_inventory_batch_id_item_id_shop_id ON public.purchase_receipt_values USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_purchase_receipt_values_purchase_order_receipt_line_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_values_purchase_order_receipt_line_id ON public.purchase_receipt_values USING btree (purchase_order_receipt_line_id);


--
-- Name: ix_purchase_receipt_values_shop_id_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_purchase_receipt_values_shop_id_inventory_batch_id ON public.purchase_receipt_values USING btree (shop_id, inventory_batch_id);


--
-- Name: ix_purchase_receipt_values_shop_id_supplier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_values_shop_id_supplier_id ON public.purchase_receipt_values USING btree (shop_id, supplier_id);


--
-- Name: ix_purchase_receipt_values_supplier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_purchase_receipt_values_supplier_id ON public.purchase_receipt_values USING btree (supplier_id);


--
-- Name: ix_rate_limit_buckets_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_rate_limit_buckets_expires_at ON public.rate_limit_buckets USING btree (expires_at);


--
-- Name: ix_realtime_connection_leases_connection_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_realtime_connection_leases_connection_id ON public.realtime_connection_leases USING btree (connection_id);


--
-- Name: ix_realtime_connection_leases_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_realtime_connection_leases_expires_at ON public.realtime_connection_leases USING btree (expires_at);


--
-- Name: ix_realtime_connection_leases_hub_name_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_realtime_connection_leases_hub_name_shop_id ON public.realtime_connection_leases USING btree (hub_name, shop_id);


--
-- Name: ix_realtime_connection_leases_instance_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_realtime_connection_leases_instance_id ON public.realtime_connection_leases USING btree (instance_id);


--
-- Name: ix_reconciliation_case_events_case_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_case_events_case_id ON public.reconciliation_case_events USING btree (case_id);


--
-- Name: ix_reconciliation_case_events_shop_id_case_id_occurred_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_case_events_shop_id_case_id_occurred_at ON public.reconciliation_case_events USING btree (shop_id, case_id, occurred_at);


--
-- Name: ix_reconciliation_issues_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_inventory_batch_id ON public.reconciliation_issues USING btree (inventory_batch_id);


--
-- Name: ix_reconciliation_issues_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_item_id ON public.reconciliation_issues USING btree (item_id);


--
-- Name: ix_reconciliation_issues_offline_submission_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_offline_submission_id ON public.reconciliation_issues USING btree (offline_submission_id);


--
-- Name: ix_reconciliation_issues_resolution_stock_transaction_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_resolution_stock_transaction_id ON public.reconciliation_issues USING btree (resolution_stock_transaction_id);


--
-- Name: ix_reconciliation_issues_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_sale_id ON public.reconciliation_issues USING btree (sale_id);


--
-- Name: ix_reconciliation_issues_shop_id_client_sale_id_device_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_shop_id_client_sale_id_device_id ON public.reconciliation_issues USING btree (shop_id, client_sale_id, device_id);


--
-- Name: ix_reconciliation_issues_shop_id_is_resolved; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_shop_id_is_resolved ON public.reconciliation_issues USING btree (shop_id, is_resolved);


--
-- Name: ix_reconciliation_issues_shop_id_linked_operation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_reconciliation_issues_shop_id_linked_operation_id ON public.reconciliation_issues USING btree (shop_id, linked_operation_id) WHERE (resolution = 4);


--
-- Name: ix_reconciliation_issues_shop_id_offline_submission_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_reconciliation_issues_shop_id_offline_submission_id ON public.reconciliation_issues USING btree (shop_id, offline_submission_id) WHERE ((issue_type = 15) AND (offline_submission_id IS NOT NULL));


--
-- Name: ix_reconciliation_issues_shop_id_status_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reconciliation_issues_shop_id_status_created_at ON public.reconciliation_issues USING btree (shop_id, status, created_at);


--
-- Name: ix_refresh_tokens_auth_session_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_refresh_tokens_auth_session_id ON public.refresh_tokens USING btree (auth_session_id);


--
-- Name: ix_refresh_tokens_parent_token_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_refresh_tokens_parent_token_id ON public.refresh_tokens USING btree (parent_token_id) WHERE (parent_token_id IS NOT NULL);


--
-- Name: ix_refresh_tokens_token; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_refresh_tokens_token ON public.refresh_tokens USING btree (token);


--
-- Name: ix_refresh_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON public.refresh_tokens USING btree (token_hash);


--
-- Name: ix_refresh_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_refresh_tokens_user_id ON public.refresh_tokens USING btree (user_id);


--
-- Name: ix_sale_cancellation_lines_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_inventory_batch_id_item_id_shop_id ON public.sale_cancellation_lines USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_sale_cancellation_lines_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_item_id_shop_id ON public.sale_cancellation_lines USING btree (item_id, shop_id);


--
-- Name: ix_sale_cancellation_lines_quarantine_change_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_quarantine_change_id_shop_id ON public.sale_cancellation_lines USING btree (quarantine_change_id, shop_id);


--
-- Name: ix_sale_cancellation_lines_sale_cancellation_id_sale_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_sale_cancellation_id_sale_id_shop_id ON public.sale_cancellation_lines USING btree (sale_cancellation_id, sale_id, shop_id);


--
-- Name: ix_sale_cancellation_lines_sale_cancellation_id_sale_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sale_cancellation_lines_sale_cancellation_id_sale_item_id ON public.sale_cancellation_lines USING btree (sale_cancellation_id, sale_item_id);


--
-- Name: ix_sale_cancellation_lines_sale_item_id_sale_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_sale_item_id_sale_id_shop_id ON public.sale_cancellation_lines USING btree (sale_item_id, sale_id, shop_id);


--
-- Name: ix_sale_cancellation_lines_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_shop_id ON public.sale_cancellation_lines USING btree (shop_id);


--
-- Name: ix_sale_cancellation_lines_stock_transaction_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sale_cancellation_lines_stock_transaction_id ON public.sale_cancellation_lines USING btree (stock_transaction_id);


--
-- Name: ix_sale_cancellation_lines_stock_transaction_id_item_id_invent; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellation_lines_stock_transaction_id_item_id_invent ON public.sale_cancellation_lines USING btree (stock_transaction_id, item_id, inventory_batch_id, shop_id);


--
-- Name: ix_sale_cancellations_customer_ledger_entry_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellations_customer_ledger_entry_id_shop_id ON public.sale_cancellations USING btree (customer_ledger_entry_id, shop_id);


--
-- Name: ix_sale_cancellations_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sale_cancellations_sale_id ON public.sale_cancellations USING btree (sale_id);


--
-- Name: ix_sale_cancellations_sale_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellations_sale_id_shop_id ON public.sale_cancellations USING btree (sale_id, shop_id);


--
-- Name: ix_sale_cancellations_shop_id_business_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_cancellations_shop_id_business_date ON public.sale_cancellations USING btree (shop_id, business_date);


--
-- Name: ix_sale_cancellations_shop_id_reference_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sale_cancellations_shop_id_reference_number ON public.sale_cancellations USING btree (shop_id, reference_number);


--
-- Name: ix_sale_items_inventory_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_inventory_batch_id ON public.sale_items USING btree (inventory_batch_id);


--
-- Name: ix_sale_items_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_item_id ON public.sale_items USING btree (item_id);


--
-- Name: ix_sale_items_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_sale_id ON public.sale_items USING btree (sale_id);


--
-- Name: ix_sale_items_service_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_service_id ON public.sale_items USING btree (service_id);


--
-- Name: ix_sale_items_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_shop_id ON public.sale_items USING btree (shop_id);


--
-- Name: ix_sale_items_shop_id_configured_batch_rule_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_shop_id_configured_batch_rule_id ON public.sale_items USING btree (shop_id, configured_batch_rule_id);


--
-- Name: ix_sale_items_shop_id_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_shop_id_item_id ON public.sale_items USING btree (shop_id, item_id);


--
-- Name: ix_sale_items_shop_id_service_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_items_shop_id_service_id ON public.sale_items USING btree (shop_id, service_id);


--
-- Name: ix_sale_return_items_quarantine_change_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_quarantine_change_id_shop_id ON public.sale_return_items USING btree (quarantine_change_id, shop_id);


--
-- Name: ix_sale_return_items_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_sale_id ON public.sale_return_items USING btree (sale_id);


--
-- Name: ix_sale_return_items_sale_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_sale_item_id ON public.sale_return_items USING btree (sale_item_id);


--
-- Name: ix_sale_return_items_sale_return_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_sale_return_id ON public.sale_return_items USING btree (sale_return_id);


--
-- Name: ix_sale_return_items_shop_id_condition; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_shop_id_condition ON public.sale_return_items USING btree (shop_id, condition);


--
-- Name: ix_sale_return_items_shop_id_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_shop_id_sale_id ON public.sale_return_items USING btree (shop_id, sale_id);


--
-- Name: ix_sale_return_items_shop_id_sale_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_items_shop_id_sale_item_id ON public.sale_return_items USING btree (shop_id, sale_item_id);


--
-- Name: ix_sale_return_settlements_credit_note_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_settlements_credit_note_id ON public.sale_return_settlements USING btree (credit_note_id);


--
-- Name: ix_sale_return_settlements_customer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_settlements_customer_id ON public.sale_return_settlements USING btree (customer_id);


--
-- Name: ix_sale_return_settlements_customer_ledger_entry_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_settlements_customer_ledger_entry_id ON public.sale_return_settlements USING btree (customer_ledger_entry_id);


--
-- Name: ix_sale_return_settlements_sale_return_id_sequence; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sale_return_settlements_sale_return_id_sequence ON public.sale_return_settlements USING btree (sale_return_id, sequence);


--
-- Name: ix_sale_return_settlements_shop_id_sale_return_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_return_settlements_shop_id_sale_return_id ON public.sale_return_settlements USING btree (shop_id, sale_return_id);


--
-- Name: ix_sale_returns_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_sale_id ON public.sale_returns USING btree (sale_id);


--
-- Name: ix_sale_returns_shop_id_business_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_shop_id_business_date ON public.sale_returns USING btree (shop_id, business_date);


--
-- Name: ix_sale_returns_shop_id_is_voided; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_shop_id_is_voided ON public.sale_returns USING btree (shop_id, is_voided);


--
-- Name: ix_sale_returns_shop_id_processed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_shop_id_processed_at ON public.sale_returns USING btree (shop_id, processed_at);


--
-- Name: ix_sale_returns_shop_id_return_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sale_returns_shop_id_return_number ON public.sale_returns USING btree (shop_id, return_number);


--
-- Name: ix_sale_returns_shop_id_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_shop_id_sale_id ON public.sale_returns USING btree (shop_id, sale_id);


--
-- Name: ix_sale_returns_shop_id_voided_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_shop_id_voided_at ON public.sale_returns USING btree (shop_id, voided_at);


--
-- Name: ix_sale_returns_shop_id_voided_business_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sale_returns_shop_id_voided_business_date ON public.sale_returns USING btree (shop_id, voided_business_date);


--
-- Name: ix_sales_customer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_customer_id ON public.sales USING btree (customer_id);


--
-- Name: ix_sales_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_shop_id ON public.sales USING btree (shop_id);


--
-- Name: ix_sales_shop_id_actor_user_id_idempotency_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sales_shop_id_actor_user_id_idempotency_key ON public.sales USING btree (shop_id, actor_user_id, idempotency_key);


--
-- Name: ix_sales_shop_id_business_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_shop_id_business_date ON public.sales USING btree (shop_id, business_date);


--
-- Name: ix_sales_shop_id_configured_sale_rule_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_shop_id_configured_sale_rule_id ON public.sales USING btree (shop_id, configured_sale_rule_id);


--
-- Name: ix_sales_shop_id_customer_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_shop_id_customer_id ON public.sales USING btree (shop_id, customer_id);


--
-- Name: ix_sales_shop_id_device_id_client_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sales_shop_id_device_id_client_sale_id ON public.sales USING btree (shop_id, device_id, client_sale_id);


--
-- Name: ix_sales_shop_id_invoice_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_sales_shop_id_invoice_number ON public.sales USING btree (shop_id, invoice_number);


--
-- Name: ix_sales_shop_id_sold_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_shop_id_sold_at ON public.sales USING btree (shop_id, sold_at);


--
-- Name: ix_sales_till_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sales_till_session_id_shop_id ON public.sales USING btree (till_session_id, shop_id);


--
-- Name: ix_service_code_sequences_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_service_code_sequences_shop_id ON public.service_code_sequences USING btree (shop_id);


--
-- Name: ix_services_shop_id_code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_services_shop_id_code ON public.services USING btree (shop_id, code);


--
-- Name: ix_services_shop_id_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_services_shop_id_is_active ON public.services USING btree (shop_id, is_active);


--
-- Name: ix_services_shop_id_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_services_shop_id_name ON public.services USING btree (shop_id, name);


--
-- Name: ix_shop_invitations_shop_id_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_invitations_shop_id_created_at ON public.shop_invitations USING btree (shop_id, created_at);


--
-- Name: ix_shop_invitations_shop_id_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_shop_invitations_shop_id_email ON public.shop_invitations USING btree (shop_id, email) WHERE ((accepted_at IS NULL) AND (revoked_at IS NULL));


--
-- Name: ix_shop_license_states_account_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_license_states_account_id ON public.shop_license_states USING btree (account_id);


--
-- Name: ix_shop_license_states_evidence_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_license_states_evidence_id ON public.shop_license_states USING btree (evidence_id);


--
-- Name: ix_shop_memberships_shop_id_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_memberships_shop_id_status ON public.shop_memberships USING btree (shop_id, status);


--
-- Name: ix_shop_memberships_user_id_is_default; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_shop_memberships_user_id_is_default ON public.shop_memberships USING btree (user_id, is_default) WHERE (is_default = true);


--
-- Name: ix_shop_memberships_user_id_last_used_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_memberships_user_id_last_used_at ON public.shop_memberships USING btree (user_id, last_used_at);


--
-- Name: ix_shop_memberships_user_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_shop_memberships_user_id_shop_id ON public.shop_memberships USING btree (user_id, shop_id);


--
-- Name: ix_shop_setup_preferences_shop_id_sample_receipt_acknowledged_; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_setup_preferences_shop_id_sample_receipt_acknowledged_ ON public.shop_setup_preferences USING btree (shop_id, sample_receipt_acknowledged_by_user_id);


--
-- Name: ix_shop_till_setting_events_shop_id_actor_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_shop_till_setting_events_shop_id_actor_user_id ON public.shop_till_setting_events USING btree (shop_id, actor_user_id);


--
-- Name: ix_shop_till_setting_events_shop_id_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_shop_till_setting_events_shop_id_version ON public.shop_till_setting_events USING btree (shop_id, version);


--
-- Name: ix_stock_counts_inventory_adjustment_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_counts_inventory_adjustment_id ON public.stock_counts USING btree (inventory_adjustment_id);


--
-- Name: ix_stock_counts_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_counts_inventory_batch_id_item_id_shop_id ON public.stock_counts USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_stock_counts_shop_id_inventory_batch_id_performed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_counts_shop_id_inventory_batch_id_performed_at ON public.stock_counts USING btree (shop_id, inventory_batch_id, performed_at);


--
-- Name: ix_stock_counts_shop_id_item_id_performed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_counts_shop_id_item_id_performed_at ON public.stock_counts USING btree (shop_id, item_id, performed_at);


--
-- Name: ix_stock_counts_stock_transaction_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_counts_stock_transaction_id ON public.stock_counts USING btree (stock_transaction_id);


--
-- Name: ix_stock_transactions_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_transactions_inventory_batch_id_item_id_shop_id ON public.stock_transactions USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_stock_transactions_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_transactions_item_id_shop_id ON public.stock_transactions USING btree (item_id, shop_id);


--
-- Name: ix_stock_transactions_shop_id_inventory_batch_id_performed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_transactions_shop_id_inventory_batch_id_performed_at ON public.stock_transactions USING btree (shop_id, inventory_batch_id, performed_at);


--
-- Name: ix_stock_transactions_shop_id_item_id_performed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_stock_transactions_shop_id_item_id_performed_at ON public.stock_transactions USING btree (shop_id, item_id, performed_at);


--
-- Name: ix_supplier_ledger_entries_batch_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_ledger_entries_batch_id ON public.supplier_ledger_entries USING btree (batch_id);


--
-- Name: ix_supplier_ledger_entries_shop_id_batch_id_entry_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_ledger_entries_shop_id_batch_id_entry_type ON public.supplier_ledger_entries USING btree (shop_id, batch_id, entry_type);


--
-- Name: ix_supplier_ledger_entries_shop_id_supplier_id_entry_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_ledger_entries_shop_id_supplier_id_entry_date ON public.supplier_ledger_entries USING btree (shop_id, supplier_id, entry_date);


--
-- Name: ix_supplier_ledger_entries_supplier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_ledger_entries_supplier_id ON public.supplier_ledger_entries USING btree (supplier_id);


--
-- Name: ix_supplier_return_credit_lines_receipt_value_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_credit_lines_receipt_value_id_shop_id ON public.supplier_return_credit_lines USING btree (receipt_value_id, shop_id);


--
-- Name: ix_supplier_return_credit_lines_return_line_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_supplier_return_credit_lines_return_line_id ON public.supplier_return_credit_lines USING btree (return_line_id);


--
-- Name: ix_supplier_return_credit_lines_return_line_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_credit_lines_return_line_id_shop_id ON public.supplier_return_credit_lines USING btree (return_line_id, shop_id);


--
-- Name: ix_supplier_return_credit_lines_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_credit_lines_shop_id ON public.supplier_return_credit_lines USING btree (shop_id);


--
-- Name: ix_supplier_return_credit_lines_supplier_ledger_entry_id_shop_; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_credit_lines_supplier_ledger_entry_id_shop_ ON public.supplier_return_credit_lines USING btree (supplier_ledger_entry_id, shop_id);


--
-- Name: ix_supplier_return_credit_lines_supplier_return_credit_id_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_credit_lines_supplier_return_credit_id_shop ON public.supplier_return_credit_lines USING btree (supplier_return_credit_id, shop_id);


--
-- Name: ix_supplier_return_credits_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_credits_shop_id ON public.supplier_return_credits USING btree (shop_id);


--
-- Name: ix_supplier_return_credits_supplier_return_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_supplier_return_credits_supplier_return_id ON public.supplier_return_credits USING btree (supplier_return_id);


--
-- Name: ix_supplier_return_credits_supplier_return_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_supplier_return_credits_supplier_return_id_shop_id ON public.supplier_return_credits USING btree (supplier_return_id, shop_id);


--
-- Name: ix_supplier_return_lines_inventory_batch_id_item_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_lines_inventory_batch_id_item_id_shop_id ON public.supplier_return_lines USING btree (inventory_batch_id, item_id, shop_id);


--
-- Name: ix_supplier_return_lines_receipt_value_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_lines_receipt_value_id_shop_id ON public.supplier_return_lines USING btree (receipt_value_id, shop_id);


--
-- Name: ix_supplier_return_lines_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_lines_shop_id ON public.supplier_return_lines USING btree (shop_id);


--
-- Name: ix_supplier_return_lines_stock_transaction_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_lines_stock_transaction_id_shop_id ON public.supplier_return_lines USING btree (stock_transaction_id, shop_id);


--
-- Name: ix_supplier_return_lines_supplier_return_id_receipt_value_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_supplier_return_lines_supplier_return_id_receipt_value_id ON public.supplier_return_lines USING btree (supplier_return_id, receipt_value_id);


--
-- Name: ix_supplier_return_lines_supplier_return_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_return_lines_supplier_return_id_shop_id ON public.supplier_return_lines USING btree (supplier_return_id, shop_id);


--
-- Name: ix_supplier_returns_shop_id_return_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_supplier_returns_shop_id_return_number ON public.supplier_returns USING btree (shop_id, return_number);


--
-- Name: ix_supplier_returns_shop_id_supplier_id_shipped_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_returns_shop_id_supplier_id_shipped_at ON public.supplier_returns USING btree (shop_id, supplier_id, shipped_at);


--
-- Name: ix_supplier_returns_supplier_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_supplier_returns_supplier_id_shop_id ON public.supplier_returns USING btree (supplier_id, shop_id);


--
-- Name: ix_suppliers_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_suppliers_shop_id ON public.suppliers USING btree (shop_id);


--
-- Name: ix_suppliers_shop_id_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_suppliers_shop_id_is_active ON public.suppliers USING btree (shop_id, is_active);


--
-- Name: ix_suppliers_shop_id_is_system; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_suppliers_shop_id_is_system ON public.suppliers USING btree (shop_id, is_system) WHERE (is_system = true);


--
-- Name: ix_till_close_reviews_close_id_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_close_reviews_close_id_session_id_shop_id ON public.till_close_reviews USING btree (close_id, session_id, shop_id);


--
-- Name: ix_till_close_reviews_shop_id_close_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_close_reviews_shop_id_close_id ON public.till_close_reviews USING btree (shop_id, close_id);


--
-- Name: ix_till_close_reviews_shop_id_reviewed_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_close_reviews_shop_id_reviewed_by ON public.till_close_reviews USING btree (shop_id, reviewed_by);


--
-- Name: ix_till_closes_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_closes_session_id_shop_id ON public.till_closes USING btree (session_id, shop_id);


--
-- Name: ix_till_closes_shop_id_closed_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_closes_shop_id_closed_by ON public.till_closes USING btree (shop_id, closed_by);


--
-- Name: ix_till_closes_shop_id_session_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_closes_shop_id_session_id ON public.till_closes USING btree (shop_id, session_id);


--
-- Name: ix_till_late_postings_case_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_late_postings_case_id_shop_id ON public.till_late_postings USING btree (case_id, shop_id);


--
-- Name: ix_till_late_postings_close_id_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_late_postings_close_id_session_id_shop_id ON public.till_late_postings USING btree (close_id, session_id, shop_id);


--
-- Name: ix_till_late_postings_offline_submission_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_late_postings_offline_submission_id_shop_id ON public.till_late_postings USING btree (offline_submission_id, shop_id);


--
-- Name: ix_till_late_postings_sale_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_late_postings_sale_id_shop_id ON public.till_late_postings USING btree (sale_id, shop_id);


--
-- Name: ix_till_late_postings_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_late_postings_session_id_shop_id ON public.till_late_postings USING btree (session_id, shop_id);


--
-- Name: ix_till_late_postings_shop_id_cashier_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_late_postings_shop_id_cashier_id ON public.till_late_postings USING btree (shop_id, cashier_id);


--
-- Name: ix_till_late_postings_shop_id_offline_submission_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_late_postings_shop_id_offline_submission_id ON public.till_late_postings USING btree (shop_id, offline_submission_id);


--
-- Name: ix_till_late_postings_shop_id_sale_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_late_postings_shop_id_sale_id ON public.till_late_postings USING btree (shop_id, sale_id);


--
-- Name: ix_till_movements_bank_account_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_movements_bank_account_id_shop_id ON public.till_movements USING btree (bank_account_id, shop_id);


--
-- Name: ix_till_movements_reverses_movement_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_movements_reverses_movement_id_shop_id ON public.till_movements USING btree (reverses_movement_id, shop_id);


--
-- Name: ix_till_movements_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_movements_session_id_shop_id ON public.till_movements USING btree (session_id, shop_id);


--
-- Name: ix_till_movements_shop_id_actor_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_movements_shop_id_actor_user_id ON public.till_movements USING btree (shop_id, actor_user_id);


--
-- Name: ix_till_movements_shop_id_session_id_sequence; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_movements_shop_id_session_id_sequence ON public.till_movements USING btree (shop_id, session_id, sequence);


--
-- Name: ix_till_movements_shop_id_source_type_source_id_purpose_line_n; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_movements_shop_id_source_type_source_id_purpose_line_n ON public.till_movements USING btree (shop_id, source_type, source_id, purpose, line_number);


--
-- Name: ix_till_movements_source_record_id_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_movements_source_record_id_session_id_shop_id ON public.till_movements USING btree (source_record_id, session_id, shop_id);


--
-- Name: ix_till_sessions_shop_id_cashier_id_business_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_sessions_shop_id_cashier_id_business_date ON public.till_sessions USING btree (shop_id, cashier_id, business_date);


--
-- Name: ix_till_sessions_shop_id_opened_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_sessions_shop_id_opened_by ON public.till_sessions USING btree (shop_id, opened_by);


--
-- Name: ix_till_sessions_shop_id_till_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_sessions_shop_id_till_id ON public.till_sessions USING btree (shop_id, till_id) WHERE (state = 1);


--
-- Name: ix_till_sessions_till_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_sessions_till_id_shop_id ON public.till_sessions USING btree (till_id, shop_id);


--
-- Name: ix_till_source_records_session_id_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_source_records_session_id_shop_id ON public.till_source_records USING btree (session_id, shop_id);


--
-- Name: ix_till_source_records_shop_id_actor_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_till_source_records_shop_id_actor_user_id ON public.till_source_records USING btree (shop_id, actor_user_id);


--
-- Name: ix_till_source_records_shop_id_source_type_source_id_purpose; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_till_source_records_shop_id_source_type_source_id_purpose ON public.till_source_records USING btree (shop_id, source_type, source_id, purpose);


--
-- Name: ix_tills_shop_id_created_by; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_tills_shop_id_created_by ON public.tills USING btree (shop_id, created_by);


--
-- Name: ix_tills_shop_id_normalized_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_tills_shop_id_normalized_name ON public.tills USING btree (shop_id, normalized_name);


--
-- Name: ix_users_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_users_email ON public.users USING btree (email) WHERE (email IS NOT NULL);


--
-- Name: ix_users_normalized_email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_users_normalized_email ON public.users USING btree (lower(btrim((email)::text))) WHERE (email IS NOT NULL);


--
-- Name: ix_users_phone_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_users_phone_number ON public.users USING btree (phone_number) WHERE (phone_number IS NOT NULL);


--
-- Name: merchant_exception_cutoffs_shop; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX merchant_exception_cutoffs_shop ON public.merchant_coverage_exception_cutoffs USING btree (shop_id, account_id, term_id);


--
-- Name: merchant_exception_single_consume; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX merchant_exception_single_consume ON public.merchant_coverage_exception_decisions USING btree (exception_id) WHERE (outcome = 'Consumed'::text);


--
-- Name: shop_affiliation_consent_claim; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX shop_affiliation_consent_claim ON public.shop_affiliation_consent_events USING btree (shop_id) WHERE (kind = 'Accepted'::text);


--
-- Name: shop_affiliation_consent_delivery; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX shop_affiliation_consent_delivery ON public.shop_affiliation_consent_events USING btree (recorded_at, id) WHERE (kind = 'Accepted'::text);


--
-- Name: shop_closure_consent_proposal; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX shop_closure_consent_proposal ON public.shop_closure_consents USING btree (shop_id, proposal_id);


--
-- Name: single_applied_capacity_revision; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX single_applied_capacity_revision ON public.capacity_reservation_receipts USING btree (account_id, term_id, revision) WHERE (outcome = 'Applied'::text);


--
-- Name: single_applied_reduction_term; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX single_applied_reduction_term ON public.reduction_reservation_receipts USING btree (account_id, term_id) WHERE (outcome = 'Applied'::text);


--
-- Name: batch_disposition_changes batch_disposition_changes_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER batch_disposition_changes_immutable BEFORE DELETE OR UPDATE ON public.batch_disposition_changes FOR EACH ROW EXECUTE FUNCTION public.expiry_reject_revision_mutation();


--
-- Name: batch_disposition_changes batch_disposition_conservation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER batch_disposition_conservation AFTER INSERT ON public.batch_disposition_changes DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.expiry_validate_disposition();


--
-- Name: batch_expiry_revisions batch_expiry_revisions_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER batch_expiry_revisions_immutable BEFORE DELETE OR UPDATE ON public.batch_expiry_revisions FOR EACH ROW EXECUTE FUNCTION public.expiry_reject_revision_mutation();


--
-- Name: entitlement_reconciliation_requests bound_entitlement_reconciliation_schedule; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER bound_entitlement_reconciliation_schedule BEFORE INSERT OR UPDATE ON public.entitlement_reconciliation_requests FOR EACH ROW EXECUTE FUNCTION public.bound_entitlement_reconciliation_schedule();


--
-- Name: expenses expenses_settlement_evidence_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER expenses_settlement_evidence_immutable BEFORE DELETE OR UPDATE ON public.expenses FOR EACH ROW EXECUTE FUNCTION public.protect_source_settlement_evidence();


--
-- Name: expiry_policy_revisions expiry_policy_revisions_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER expiry_policy_revisions_immutable BEFORE DELETE OR UPDATE ON public.expiry_policy_revisions FOR EACH ROW EXECUTE FUNCTION public.expiry_reject_revision_mutation();


--
-- Name: account_license_restrictions guard_account_license_revision; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_account_license_revision BEFORE UPDATE ON public.account_license_restrictions FOR EACH ROW EXECUTE FUNCTION public.guard_license_projection();


--
-- Name: offline_license_permits guard_offline_permit_exception_cutoff; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_offline_permit_exception_cutoff BEFORE INSERT ON public.offline_license_permits FOR EACH ROW EXECUTE FUNCTION public.guard_offline_permit_exception_cutoff();


--
-- Name: shop_license_states guard_shop_activation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_shop_activation BEFORE INSERT OR DELETE OR UPDATE ON public.shop_license_states FOR EACH ROW EXECUTE FUNCTION public.guard_enforcement_activation();


--
-- Name: shop_license_states guard_shop_license_revision; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_shop_license_revision BEFORE UPDATE ON public.shop_license_states FOR EACH ROW EXECUTE FUNCTION public.guard_license_projection();


--
-- Name: offline_license_permits guard_transfer_permit; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER guard_transfer_permit BEFORE INSERT ON public.offline_license_permits FOR EACH ROW EXECUTE FUNCTION public.guard_transfer_permit();


--
-- Name: inventory_adjustments inventory_adjustments_disposal_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER inventory_adjustments_disposal_immutable BEFORE DELETE OR UPDATE ON public.inventory_adjustments FOR EACH ROW EXECUTE FUNCTION public.expiry_protect_disposal_adjustment();


--
-- Name: inventory_batches inventory_batches_quarantine_conservation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER inventory_batches_quarantine_conservation AFTER INSERT OR UPDATE ON public.inventory_batches DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.expiry_validate_batch_quarantine();


--
-- Name: shop_memberships mark_identity_business_access; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER mark_identity_business_access AFTER INSERT ON public.shop_memberships FOR EACH ROW EXECUTE FUNCTION public.mark_identity_business_access();


--
-- Name: offline_expiry_evidence offline_expiry_evidence_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER offline_expiry_evidence_immutable BEFORE DELETE OR UPDATE ON public.offline_expiry_evidence FOR EACH ROW EXECUTE FUNCTION public.expiry_reject_revision_mutation();


--
-- Name: payment_reversals payment_reversals_settlement_evidence_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER payment_reversals_settlement_evidence_immutable BEFORE DELETE OR UPDATE ON public.payment_reversals FOR EACH ROW EXECUTE FUNCTION public.protect_source_settlement_evidence();


--
-- Name: users preserve_identity_business_history; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER preserve_identity_business_history BEFORE UPDATE ON public.users FOR EACH ROW EXECUTE FUNCTION public.preserve_identity_business_history();


--
-- Name: account_license_restrictions retain_account_license_restriction; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_account_license_restriction BEFORE DELETE ON public.account_license_restrictions FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: account_license_restrictions retain_account_license_restriction_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_account_license_restriction_table BEFORE TRUNCATE ON public.account_license_restrictions FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: applied_shop_capacity retain_capacity; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_capacity BEFORE DELETE OR UPDATE OR TRUNCATE ON public.applied_shop_capacity FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: capacity_reservation_receipts retain_capacity; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_capacity BEFORE DELETE OR UPDATE OR TRUNCATE ON public.capacity_reservation_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_snapshot_inbox retain_entitlement_inbox; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_entitlement_inbox BEFORE DELETE OR UPDATE ON public.entitlement_snapshot_inbox FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_snapshot_inbox retain_entitlement_inbox_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_entitlement_inbox_table BEFORE TRUNCATE ON public.entitlement_snapshot_inbox FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_messages retain_entitlement_messages_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_entitlement_messages_rows BEFORE DELETE OR UPDATE ON public.entitlement_messages FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: entitlement_messages retain_entitlement_messages_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_entitlement_messages_table BEFORE TRUNCATE ON public.entitlement_messages FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: merchant_coverage_exception_activations retain_exception_activation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_exception_activation BEFORE DELETE OR UPDATE OR TRUNCATE ON public.merchant_coverage_exception_activations FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: coverage_exception_consent_events retain_exception_consent; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_exception_consent BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_exception_consent_events FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: merchant_coverage_exception_cutoffs retain_exception_cutoff; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_exception_cutoff BEFORE DELETE OR UPDATE OR TRUNCATE ON public.merchant_coverage_exception_cutoffs FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: merchant_coverage_exception_decisions retain_exception_decision; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_exception_decision BEFORE DELETE OR UPDATE OR TRUNCATE ON public.merchant_coverage_exception_decisions FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: license_grant_evidence retain_license_grant_evidence; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_license_grant_evidence BEFORE DELETE OR UPDATE ON public.license_grant_evidence FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: license_grant_evidence retain_license_grant_evidence_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_license_grant_evidence_table BEFORE TRUNCATE ON public.license_grant_evidence FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_submissions retain_offline_licence_receipt; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_offline_licence_receipt BEFORE UPDATE OF licence_grant_id, licence_permit_id, payload_json, payload_hash, received_at, shop_id, device_id, client_sale_id, invoice_number, claimed_sold_at, original_lease_id, original_issuer_user_id, submitted_by_user_id, asserted_issuer_user_id ON public.offline_submissions FOR EACH ROW WHEN (((old.payload_json ? 'licenceEvidence'::text) OR (new.payload_json ? 'licenceEvidence'::text))) EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: applied_shop_reductions retain_reduction; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_reduction BEFORE DELETE OR UPDATE OR TRUNCATE ON public.applied_shop_reductions FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: reduction_manifest_pages retain_reduction; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_reduction BEFORE DELETE OR UPDATE OR TRUNCATE ON public.reduction_manifest_pages FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: reduction_manifest_shops retain_reduction; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_reduction BEFORE DELETE OR UPDATE OR TRUNCATE ON public.reduction_manifest_shops FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: reduction_reservation_receipts retain_reduction; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_reduction BEFORE DELETE OR UPDATE OR TRUNCATE ON public.reduction_reservation_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: coverage_replacement_activations retain_replacement_activation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_replacement_activation BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_replacement_activations FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: coverage_replacement_consent_events retain_replacement_consent; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_replacement_consent BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_replacement_consent_events FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: license_history_review_observations retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.license_history_review_observations FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_license_grants retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.offline_license_grants FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_license_permits retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.offline_license_permits FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_license_verification_keys retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.offline_license_verification_keys FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: shop_affiliation_consent_events retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_affiliation_consent_events FOR EACH ROW EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_affiliation_delivery_receipts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_affiliation_delivery_receipts FOR EACH ROW EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_closure_consents retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_closure_consents FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: shop_trial_consents retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_trial_consents FOR EACH ROW EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_trial_receipts retain_rows; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_rows BEFORE DELETE OR UPDATE ON public.shop_trial_receipts FOR EACH ROW EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_license_states retain_shop_activation_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_shop_activation_table BEFORE TRUNCATE ON public.shop_license_states FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: coverage_slot_activations retain_slot_activation; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_slot_activation BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_slot_activations FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: coverage_slot_consent_events retain_slot_consent; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_slot_consent BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_slot_consent_events FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: license_history_review_observations retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.license_history_review_observations FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_license_grants retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.offline_license_grants FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_license_permits retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.offline_license_permits FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: offline_license_verification_keys retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.offline_license_verification_keys FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: shop_affiliation_consent_events retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_affiliation_consent_events FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_affiliation_delivery_receipts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_affiliation_delivery_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_closure_consents retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_closure_consents FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: shop_trial_consents retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_trial_consents FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: shop_trial_receipts retain_table; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_trial_receipts FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: coverage_transfer_consent_events retain_transfer_consent; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_transfer_consent BEFORE DELETE OR UPDATE OR TRUNCATE ON public.coverage_transfer_consent_events FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();


--
-- Name: merchant_transfer_decisions retain_transfer_decision; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER retain_transfer_decision BEFORE DELETE OR UPDATE OR TRUNCATE ON public.merchant_transfer_decisions FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();


--
-- Name: sale_cancellation_lines sale_cancellation_lines_disposition_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sale_cancellation_lines_disposition_immutable BEFORE DELETE OR UPDATE ON public.sale_cancellation_lines FOR EACH ROW EXECUTE FUNCTION public.expiry_protect_source_disposition();


--
-- Name: sale_cancellation_lines sale_cancellation_lines_disposition_source; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER sale_cancellation_lines_disposition_source AFTER INSERT ON public.sale_cancellation_lines DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.expiry_validate_source_disposition();


--
-- Name: sale_return_items sale_return_items_disposition_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sale_return_items_disposition_immutable BEFORE DELETE OR UPDATE ON public.sale_return_items FOR EACH ROW EXECUTE FUNCTION public.expiry_protect_source_disposition();


--
-- Name: sale_return_items sale_return_items_disposition_source; Type: TRIGGER; Schema: public; Owner: -
--

CREATE CONSTRAINT TRIGGER sale_return_items_disposition_source AFTER INSERT ON public.sale_return_items DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.expiry_validate_source_disposition();


--
-- Name: sale_returns sale_returns_business_date_default; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sale_returns_business_date_default BEFORE INSERT OR UPDATE ON public.sale_returns FOR EACH ROW EXECUTE FUNCTION public.sale_returns_business_date_default();


--
-- Name: sale_returns sale_returns_settlement_evidence_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sale_returns_settlement_evidence_immutable BEFORE DELETE OR UPDATE ON public.sale_returns FOR EACH ROW EXECUTE FUNCTION public.protect_return_settlement_evidence();


--
-- Name: sales sales_business_date_default; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sales_business_date_default BEFORE INSERT OR UPDATE ON public.sales FOR EACH ROW EXECUTE FUNCTION public.sales_business_date_default();


--
-- Name: sales sales_expiry_evidence_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER sales_expiry_evidence_immutable BEFORE DELETE OR UPDATE ON public.sales FOR EACH ROW EXECUTE FUNCTION public.expiry_reject_sale_evidence_mutation();


--
-- Name: shop_affiliation_consent_events shop_affiliation_consent_guard; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER shop_affiliation_consent_guard BEFORE INSERT ON public.shop_affiliation_consent_events FOR EACH ROW EXECUTE FUNCTION public.guard_shop_affiliation_consent();


--
-- Name: shop_till_setting_events shop_till_setting_events_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER shop_till_setting_events_immutable BEFORE DELETE OR UPDATE ON public.shop_till_setting_events FOR EACH ROW EXECUTE FUNCTION public.till_reject_fact_mutation();


--
-- Name: till_close_reviews till_close_reviews_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_close_reviews_immutable BEFORE DELETE OR UPDATE ON public.till_close_reviews FOR EACH ROW EXECUTE FUNCTION public.till_reject_fact_mutation();


--
-- Name: till_closes till_closes_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_closes_immutable BEFORE DELETE OR UPDATE ON public.till_closes FOR EACH ROW EXECUTE FUNCTION public.till_reject_fact_mutation();


--
-- Name: till_late_postings till_late_postings_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_late_postings_immutable BEFORE DELETE OR UPDATE ON public.till_late_postings FOR EACH ROW EXECUTE FUNCTION public.till_reject_fact_mutation();


--
-- Name: till_movements till_movements_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_movements_immutable BEFORE DELETE OR UPDATE ON public.till_movements FOR EACH ROW EXECUTE FUNCTION public.till_reject_fact_mutation();


--
-- Name: till_movements till_movements_open_session; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_movements_open_session BEFORE INSERT ON public.till_movements FOR EACH ROW EXECUTE FUNCTION public.till_guard_source_session();


--
-- Name: till_sessions till_session_closed_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_session_closed_immutable BEFORE DELETE OR UPDATE ON public.till_sessions FOR EACH ROW EXECUTE FUNCTION public.till_guard_closed_session();


--
-- Name: till_source_records till_source_records_immutable; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_source_records_immutable BEFORE DELETE OR UPDATE ON public.till_source_records FOR EACH ROW EXECUTE FUNCTION public.till_reject_fact_mutation();


--
-- Name: till_source_records till_sources_open_session; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER till_sources_open_session BEFORE INSERT ON public.till_source_records FOR EACH ROW EXECUTE FUNCTION public.till_guard_source_session();


--
-- Name: applied_shop_capacity applied_shop_capacity_applied_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.applied_shop_capacity
    ADD CONSTRAINT applied_shop_capacity_applied_event_fkey FOREIGN KEY (applied_event) REFERENCES public.capacity_reservation_receipts(event_id);


--
-- Name: applied_shop_capacity applied_shop_capacity_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.applied_shop_capacity
    ADD CONSTRAINT applied_shop_capacity_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: applied_shop_reductions applied_shop_reductions_applied_event_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.applied_shop_reductions
    ADD CONSTRAINT applied_shop_reductions_applied_event_fkey FOREIGN KEY (applied_event) REFERENCES public.reduction_reservation_receipts(event_id);


--
-- Name: applied_shop_reductions applied_shop_reductions_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.applied_shop_reductions
    ADD CONSTRAINT applied_shop_reductions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: coverage_exception_consent_events coverage_exception_consent_events_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_exception_consent_events
    ADD CONSTRAINT coverage_exception_consent_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: coverage_replacement_activations coverage_replacement_activations_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_activations
    ADD CONSTRAINT coverage_replacement_activations_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: coverage_replacement_consent_events coverage_replacement_consent_events_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_replacement_consent_events
    ADD CONSTRAINT coverage_replacement_consent_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: coverage_slot_activations coverage_slot_activations_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_activations
    ADD CONSTRAINT coverage_slot_activations_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: coverage_slot_consent_events coverage_slot_consent_events_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_slot_consent_events
    ADD CONSTRAINT coverage_slot_consent_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: coverage_transfer_consent_events coverage_transfer_consent_events_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.coverage_transfer_consent_events
    ADD CONSTRAINT coverage_transfer_consent_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: entitlement_outbox entitlement_outbox_event_id_account_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_outbox
    ADD CONSTRAINT entitlement_outbox_event_id_account_id_shop_id_fkey FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id);


--
-- Name: entitlement_reconciliation_requests entitlement_reconciliation_requests_event_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_reconciliation_requests
    ADD CONSTRAINT entitlement_reconciliation_requests_event_id_fkey FOREIGN KEY (event_id) REFERENCES public.entitlement_messages(event_id);


--
-- Name: entitlement_reconciliation_requests entitlement_reconciliation_requests_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.entitlement_reconciliation_requests
    ADD CONSTRAINT entitlement_reconciliation_requests_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: account_license_restrictions fk_account_license_restrictions_license_grant_evidence_evidenc; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.account_license_restrictions
    ADD CONSTRAINT fk_account_license_restrictions_license_grant_evidence_evidenc FOREIGN KEY (evidence_id) REFERENCES public.license_grant_evidence(event_id) ON DELETE RESTRICT;


--
-- Name: auth_sessions fk_auth_sessions_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_sessions
    ADD CONSTRAINT fk_auth_sessions_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: bank_accounts fk_bank_accounts_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.bank_accounts
    ADD CONSTRAINT fk_bank_accounts_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: batch_disposition_changes fk_batch_disposition_changes_batch_disposition_changes_root_ch; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT fk_batch_disposition_changes_batch_disposition_changes_root_ch FOREIGN KEY (root_change_id, inventory_batch_id, item_id, shop_id) REFERENCES public.batch_disposition_changes(id, inventory_batch_id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: batch_disposition_changes fk_batch_disposition_changes_inventory_adjustments_inventory_a; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT fk_batch_disposition_changes_inventory_adjustments_inventory_a FOREIGN KEY (inventory_adjustment_id) REFERENCES public.inventory_adjustments(id) ON DELETE RESTRICT;


--
-- Name: batch_disposition_changes fk_batch_disposition_changes_inventory_batches_inventory_batch; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT fk_batch_disposition_changes_inventory_batches_inventory_batch FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: batch_disposition_changes fk_batch_disposition_changes_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT fk_batch_disposition_changes_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: batch_disposition_changes fk_batch_disposition_changes_stock_transactions_stock_movement; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_disposition_changes
    ADD CONSTRAINT fk_batch_disposition_changes_stock_transactions_stock_movement FOREIGN KEY (stock_movement_id) REFERENCES public.stock_transactions(id) ON DELETE RESTRICT;


--
-- Name: batch_expiry_revisions fk_batch_expiry_revisions_inventory_batches_inventory_batch_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_expiry_revisions
    ADD CONSTRAINT fk_batch_expiry_revisions_inventory_batches_inventory_batch_id FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: batch_expiry_revisions fk_batch_expiry_revisions_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_expiry_revisions
    ADD CONSTRAINT fk_batch_expiry_revisions_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: batch_price_revisions fk_batch_price_revisions_inventory_batches_inventory_batch_id_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_price_revisions
    ADD CONSTRAINT fk_batch_price_revisions_inventory_batches_inventory_batch_id_ FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: batch_price_revisions fk_batch_price_revisions_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_price_revisions
    ADD CONSTRAINT fk_batch_price_revisions_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: credit_note_redemptions fk_credit_note_redemptions_credit_notes_credit_note_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_note_redemptions
    ADD CONSTRAINT fk_credit_note_redemptions_credit_notes_credit_note_id FOREIGN KEY (credit_note_id) REFERENCES public.credit_notes(id) ON DELETE CASCADE;


--
-- Name: credit_note_redemptions fk_credit_note_redemptions_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_note_redemptions
    ADD CONSTRAINT fk_credit_note_redemptions_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE RESTRICT;


--
-- Name: credit_note_redemptions fk_credit_note_redemptions_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_note_redemptions
    ADD CONSTRAINT fk_credit_note_redemptions_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: credit_notes fk_credit_notes_sale_returns_sale_return_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_notes
    ADD CONSTRAINT fk_credit_notes_sale_returns_sale_return_id FOREIGN KEY (sale_return_id) REFERENCES public.sale_returns(id) ON DELETE RESTRICT;


--
-- Name: credit_notes fk_credit_notes_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.credit_notes
    ADD CONSTRAINT fk_credit_notes_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: customer_ledger_entries fk_customer_ledger_entries_customers_customer_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_ledger_entries
    ADD CONSTRAINT fk_customer_ledger_entries_customers_customer_id FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE RESTRICT;


--
-- Name: customer_ledger_entries fk_customer_ledger_entries_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_ledger_entries
    ADD CONSTRAINT fk_customer_ledger_entries_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE RESTRICT;


--
-- Name: customer_ledger_entries fk_customer_ledger_entries_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_ledger_entries
    ADD CONSTRAINT fk_customer_ledger_entries_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: customers fk_customers_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT fk_customers_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: discount_rules fk_discount_rules_inventory_batches_inventory_batch_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.discount_rules
    ADD CONSTRAINT fk_discount_rules_inventory_batches_inventory_batch_id FOREIGN KEY (inventory_batch_id) REFERENCES public.inventory_batches(id) ON DELETE RESTRICT;


--
-- Name: discount_rules fk_discount_rules_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.discount_rules
    ADD CONSTRAINT fk_discount_rules_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: expense_categories fk_expense_categories_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expense_categories
    ADD CONSTRAINT fk_expense_categories_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: expenses fk_expenses_expense_categories_category_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT fk_expenses_expense_categories_category_id FOREIGN KEY (category_id) REFERENCES public.expense_categories(id) ON DELETE RESTRICT;


--
-- Name: expenses fk_expenses_expenses_original_expense_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT fk_expenses_expenses_original_expense_id FOREIGN KEY (original_expense_id) REFERENCES public.expenses(id) ON DELETE RESTRICT;


--
-- Name: expenses fk_expenses_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT fk_expenses_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: expenses fk_expenses_supplier_ledger_entries_supplier_ledger_entry_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT fk_expenses_supplier_ledger_entries_supplier_ledger_entry_id FOREIGN KEY (supplier_ledger_entry_id) REFERENCES public.supplier_ledger_entries(id) ON DELETE SET NULL;


--
-- Name: expiry_policy_revisions fk_expiry_policy_revisions_items_item_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expiry_policy_revisions
    ADD CONSTRAINT fk_expiry_policy_revisions_items_item_id_shop_id FOREIGN KEY (item_id, shop_id) REFERENCES public.items(id, shop_id) ON DELETE RESTRICT;


--
-- Name: expiry_policy_revisions fk_expiry_policy_revisions_shop_expiry_states_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.expiry_policy_revisions
    ADD CONSTRAINT fk_expiry_policy_revisions_shop_expiry_states_shop_id FOREIGN KEY (shop_id) REFERENCES public.shop_expiry_states(shop_id) ON DELETE RESTRICT;


--
-- Name: inventory_adjustments fk_inventory_adjustments_inventory_batches_inventory_batch_id_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_adjustments
    ADD CONSTRAINT fk_inventory_adjustments_inventory_batches_inventory_batch_id_ FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: inventory_adjustments fk_inventory_adjustments_items_item_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_adjustments
    ADD CONSTRAINT fk_inventory_adjustments_items_item_id_shop_id FOREIGN KEY (item_id, shop_id) REFERENCES public.items(id, shop_id) ON DELETE RESTRICT;


--
-- Name: inventory_adjustments fk_inventory_adjustments_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_adjustments
    ADD CONSTRAINT fk_inventory_adjustments_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: inventory_adjustments fk_inventory_adjustments_stock_transactions_reversal_stock_tra; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_adjustments
    ADD CONSTRAINT fk_inventory_adjustments_stock_transactions_reversal_stock_tra FOREIGN KEY (reversal_stock_transaction_id) REFERENCES public.stock_transactions(id) ON DELETE RESTRICT;


--
-- Name: inventory_batches fk_inventory_batches_items_item_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_batches
    ADD CONSTRAINT fk_inventory_batches_items_item_id_shop_id FOREIGN KEY (item_id, shop_id) REFERENCES public.items(id, shop_id) ON DELETE CASCADE;


--
-- Name: inventory_batches fk_inventory_batches_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_batches
    ADD CONSTRAINT fk_inventory_batches_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: inventory_batches fk_inventory_batches_suppliers_supplier_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_batches
    ADD CONSTRAINT fk_inventory_batches_suppliers_supplier_id FOREIGN KEY (supplier_id) REFERENCES public.suppliers(id) ON DELETE SET NULL;


--
-- Name: inventory fk_inventory_items_item_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT fk_inventory_items_item_id_shop_id FOREIGN KEY (item_id, shop_id) REFERENCES public.items(id, shop_id) ON DELETE CASCADE;


--
-- Name: inventory fk_inventory_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT fk_inventory_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: invoice_leases fk_invoice_leases_invoice_sequences_invoice_sequence_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_leases
    ADD CONSTRAINT fk_invoice_leases_invoice_sequences_invoice_sequence_id FOREIGN KEY (invoice_sequence_id) REFERENCES public.invoice_sequences(id) ON DELETE CASCADE;


--
-- Name: invoice_leases fk_invoice_leases_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_leases
    ADD CONSTRAINT fk_invoice_leases_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: invoice_sequences fk_invoice_sequences_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_sequences
    ADD CONSTRAINT fk_invoice_sequences_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: item_barcode_sequences fk_item_barcode_sequences_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.item_barcode_sequences
    ADD CONSTRAINT fk_item_barcode_sequences_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: items fk_items_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.items
    ADD CONSTRAINT fk_items_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: license_history_review_observations fk_license_history_review_observations_offline_submissions_com; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.license_history_review_observations
    ADD CONSTRAINT fk_license_history_review_observations_offline_submissions_com FOREIGN KEY (competing_submission_id, shop_id) REFERENCES public.offline_submissions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: license_history_review_observations fk_license_history_review_observations_offline_submissions_off; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.license_history_review_observations
    ADD CONSTRAINT fk_license_history_review_observations_offline_submissions_off FOREIGN KEY (offline_submission_id, shop_id) REFERENCES public.offline_submissions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: license_history_review_observations fk_license_history_review_observations_reconciliation_issues_c; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.license_history_review_observations
    ADD CONSTRAINT fk_license_history_review_observations_reconciliation_issues_c FOREIGN KEY (case_id, shop_id) REFERENCES public.reconciliation_issues(id, shop_id) ON DELETE RESTRICT;


--
-- Name: license_history_review_observations fk_license_history_review_observations_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.license_history_review_observations
    ADD CONSTRAINT fk_license_history_review_observations_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: offline_expiry_evidence fk_offline_expiry_evidence_inventory_batches_batch_id_item_id_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_expiry_evidence
    ADD CONSTRAINT fk_offline_expiry_evidence_inventory_batches_batch_id_item_id_ FOREIGN KEY (batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: offline_expiry_evidence fk_offline_expiry_evidence_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_expiry_evidence
    ADD CONSTRAINT fk_offline_expiry_evidence_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: offline_license_grants fk_offline_license_grants_invoice_leases_lease_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_grants
    ADD CONSTRAINT fk_offline_license_grants_invoice_leases_lease_id_shop_id FOREIGN KEY (lease_id, shop_id) REFERENCES public.invoice_leases(id, shop_id) ON DELETE RESTRICT;


--
-- Name: offline_license_grants fk_offline_license_grants_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_grants
    ADD CONSTRAINT fk_offline_license_grants_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: offline_license_permits fk_offline_license_permits_offline_license_grants_grant_id_sho; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_permits
    ADD CONSTRAINT fk_offline_license_permits_offline_license_grants_grant_id_sho FOREIGN KEY (grant_id, shop_id) REFERENCES public.offline_license_grants(id, shop_id) ON DELETE RESTRICT;


--
-- Name: offline_license_permits fk_offline_license_permits_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_permits
    ADD CONSTRAINT fk_offline_license_permits_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: offline_license_verification_keys fk_offline_license_verification_keys_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_license_verification_keys
    ADD CONSTRAINT fk_offline_license_verification_keys_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: offline_submissions fk_offline_submissions_offline_license_grants_licence_grant_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_submissions
    ADD CONSTRAINT fk_offline_submissions_offline_license_grants_licence_grant_id FOREIGN KEY (licence_grant_id, shop_id) REFERENCES public.offline_license_grants(id, shop_id) ON DELETE RESTRICT;


--
-- Name: offline_submissions fk_offline_submissions_offline_license_permits_licence_permit_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_submissions
    ADD CONSTRAINT fk_offline_submissions_offline_license_permits_licence_permit_ FOREIGN KEY (licence_permit_id, shop_id) REFERENCES public.offline_license_permits(id, shop_id) ON DELETE RESTRICT;


--
-- Name: offline_submissions fk_offline_submissions_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_submissions
    ADD CONSTRAINT fk_offline_submissions_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE SET NULL;


--
-- Name: offline_submissions fk_offline_submissions_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.offline_submissions
    ADD CONSTRAINT fk_offline_submissions_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: password_reset_tokens fk_password_reset_tokens_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.password_reset_tokens
    ADD CONSTRAINT fk_password_reset_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: payment_allocations fk_payment_allocations_payment_receipts_receipt_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_allocations
    ADD CONSTRAINT fk_payment_allocations_payment_receipts_receipt_id FOREIGN KEY (receipt_id) REFERENCES public.payment_receipts(id) ON DELETE RESTRICT;


--
-- Name: payment_allocations fk_payment_allocations_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_allocations
    ADD CONSTRAINT fk_payment_allocations_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: payment_receipts fk_payment_receipts_bank_accounts_bank_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_receipts
    ADD CONSTRAINT fk_payment_receipts_bank_accounts_bank_account_id FOREIGN KEY (bank_account_id) REFERENCES public.bank_accounts(id) ON DELETE RESTRICT;


--
-- Name: payment_receipts fk_payment_receipts_expenses_expense_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_receipts
    ADD CONSTRAINT fk_payment_receipts_expenses_expense_id FOREIGN KEY (expense_id) REFERENCES public.expenses(id) ON DELETE RESTRICT;


--
-- Name: payment_receipts fk_payment_receipts_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_receipts
    ADD CONSTRAINT fk_payment_receipts_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: payment_reversals fk_payment_reversals_payment_receipts_receipt_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_reversals
    ADD CONSTRAINT fk_payment_reversals_payment_receipts_receipt_id FOREIGN KEY (receipt_id) REFERENCES public.payment_receipts(id) ON DELETE RESTRICT;


--
-- Name: payment_reversals fk_payment_reversals_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_reversals
    ADD CONSTRAINT fk_payment_reversals_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: posting_period_events fk_posting_period_events_posting_periods_posting_period_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.posting_period_events
    ADD CONSTRAINT fk_posting_period_events_posting_periods_posting_period_id FOREIGN KEY (posting_period_id) REFERENCES public.posting_periods(id) ON DELETE RESTRICT;


--
-- Name: posting_period_events fk_posting_period_events_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.posting_period_events
    ADD CONSTRAINT fk_posting_period_events_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: posting_periods fk_posting_periods_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.posting_periods
    ADD CONSTRAINT fk_posting_periods_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: purchase_order_lines fk_purchase_order_lines_items_item_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_lines
    ADD CONSTRAINT fk_purchase_order_lines_items_item_id FOREIGN KEY (item_id) REFERENCES public.items(id) ON DELETE RESTRICT;


--
-- Name: purchase_order_lines fk_purchase_order_lines_purchase_orders_purchase_order_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_lines
    ADD CONSTRAINT fk_purchase_order_lines_purchase_orders_purchase_order_id FOREIGN KEY (purchase_order_id) REFERENCES public.purchase_orders(id) ON DELETE CASCADE;


--
-- Name: purchase_order_receipt_lines fk_purchase_order_receipt_lines_inventory_batches_inventory_ba; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_lines
    ADD CONSTRAINT fk_purchase_order_receipt_lines_inventory_batches_inventory_ba FOREIGN KEY (inventory_batch_id) REFERENCES public.inventory_batches(id) ON DELETE RESTRICT;


--
-- Name: purchase_order_receipt_lines fk_purchase_order_receipt_lines_items_item_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_lines
    ADD CONSTRAINT fk_purchase_order_receipt_lines_items_item_id FOREIGN KEY (item_id) REFERENCES public.items(id) ON DELETE RESTRICT;


--
-- Name: purchase_order_receipt_lines fk_purchase_order_receipt_lines_purchase_order_lines_purchase_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_lines
    ADD CONSTRAINT fk_purchase_order_receipt_lines_purchase_order_lines_purchase_ FOREIGN KEY (purchase_order_line_id) REFERENCES public.purchase_order_lines(id) ON DELETE RESTRICT;


--
-- Name: purchase_order_receipt_lines fk_purchase_order_receipt_lines_purchase_order_receipts_purcha; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_lines
    ADD CONSTRAINT fk_purchase_order_receipt_lines_purchase_order_receipts_purcha FOREIGN KEY (purchase_order_receipt_id) REFERENCES public.purchase_order_receipts(id) ON DELETE CASCADE;


--
-- Name: purchase_order_receipt_lines fk_purchase_order_receipt_lines_stock_transactions_stock_trans; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipt_lines
    ADD CONSTRAINT fk_purchase_order_receipt_lines_stock_transactions_stock_trans FOREIGN KEY (stock_transaction_id) REFERENCES public.stock_transactions(id) ON DELETE RESTRICT;


--
-- Name: purchase_order_receipts fk_purchase_order_receipts_purchase_orders_purchase_order_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipts
    ADD CONSTRAINT fk_purchase_order_receipts_purchase_orders_purchase_order_id FOREIGN KEY (purchase_order_id) REFERENCES public.purchase_orders(id) ON DELETE CASCADE;


--
-- Name: purchase_order_receipts fk_purchase_order_receipts_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_receipts
    ADD CONSTRAINT fk_purchase_order_receipts_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: purchase_order_sequences fk_purchase_order_sequences_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_order_sequences
    ADD CONSTRAINT fk_purchase_order_sequences_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: purchase_orders fk_purchase_orders_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_orders
    ADD CONSTRAINT fk_purchase_orders_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: purchase_orders fk_purchase_orders_suppliers_supplier_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_orders
    ADD CONSTRAINT fk_purchase_orders_suppliers_supplier_id FOREIGN KEY (supplier_id) REFERENCES public.suppliers(id) ON DELETE RESTRICT;


--
-- Name: purchase_orders fk_purchase_orders_users_closed_by; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_orders
    ADD CONSTRAINT fk_purchase_orders_users_closed_by FOREIGN KEY (closed_by) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_cost_adjustments fk_purchase_receipt_cost_adjustments_inventory_batches_invento; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_cost_adjustments
    ADD CONSTRAINT fk_purchase_receipt_cost_adjustments_inventory_batches_invento FOREIGN KEY (inventory_batch_id) REFERENCES public.inventory_batches(id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_cost_adjustments fk_purchase_receipt_cost_adjustments_purchase_receipt_values_p; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_cost_adjustments
    ADD CONSTRAINT fk_purchase_receipt_cost_adjustments_purchase_receipt_values_p FOREIGN KEY (purchase_receipt_value_id) REFERENCES public.purchase_receipt_values(id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_cost_adjustments fk_purchase_receipt_cost_adjustments_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_cost_adjustments
    ADD CONSTRAINT fk_purchase_receipt_cost_adjustments_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: purchase_receipt_cost_adjustments fk_purchase_receipt_cost_adjustments_supplier_ledger_entries_s; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_cost_adjustments
    ADD CONSTRAINT fk_purchase_receipt_cost_adjustments_supplier_ledger_entries_s FOREIGN KEY (supplier_ledger_entry_id) REFERENCES public.supplier_ledger_entries(id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_reversals fk_purchase_receipt_reversals_purchase_receipt_values_receipt_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_reversals
    ADD CONSTRAINT fk_purchase_receipt_reversals_purchase_receipt_values_receipt_ FOREIGN KEY (receipt_value_id, shop_id) REFERENCES public.purchase_receipt_values(id, shop_id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_reversals fk_purchase_receipt_reversals_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_reversals
    ADD CONSTRAINT fk_purchase_receipt_reversals_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_reversals fk_purchase_receipt_reversals_stock_transactions_stock_transac; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_reversals
    ADD CONSTRAINT fk_purchase_receipt_reversals_stock_transactions_stock_transac FOREIGN KEY (stock_transaction_id, shop_id) REFERENCES public.stock_transactions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_reversals fk_purchase_receipt_reversals_supplier_ledger_entries_supplier; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_reversals
    ADD CONSTRAINT fk_purchase_receipt_reversals_supplier_ledger_entries_supplier FOREIGN KEY (supplier_ledger_entry_id, shop_id) REFERENCES public.supplier_ledger_entries(id, shop_id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_values fk_purchase_receipt_values_inventory_batches_inventory_batch_i; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_values
    ADD CONSTRAINT fk_purchase_receipt_values_inventory_batches_inventory_batch_i FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_values fk_purchase_receipt_values_purchase_order_receipt_lines_purcha; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_values
    ADD CONSTRAINT fk_purchase_receipt_values_purchase_order_receipt_lines_purcha FOREIGN KEY (purchase_order_receipt_line_id) REFERENCES public.purchase_order_receipt_lines(id) ON DELETE RESTRICT;


--
-- Name: purchase_receipt_values fk_purchase_receipt_values_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_values
    ADD CONSTRAINT fk_purchase_receipt_values_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: purchase_receipt_values fk_purchase_receipt_values_suppliers_supplier_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_values
    ADD CONSTRAINT fk_purchase_receipt_values_suppliers_supplier_id FOREIGN KEY (supplier_id) REFERENCES public.suppliers(id) ON DELETE SET NULL;


--
-- Name: reconciliation_case_events fk_reconciliation_case_events_reconciliation_issues_case_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_case_events
    ADD CONSTRAINT fk_reconciliation_case_events_reconciliation_issues_case_id FOREIGN KEY (case_id) REFERENCES public.reconciliation_issues(id) ON DELETE RESTRICT;


--
-- Name: reconciliation_case_events fk_reconciliation_case_events_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_case_events
    ADD CONSTRAINT fk_reconciliation_case_events_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: reconciliation_issues fk_reconciliation_issues_inventory_batches_inventory_batch_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT fk_reconciliation_issues_inventory_batches_inventory_batch_id FOREIGN KEY (inventory_batch_id) REFERENCES public.inventory_batches(id) ON DELETE SET NULL;


--
-- Name: reconciliation_issues fk_reconciliation_issues_items_item_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT fk_reconciliation_issues_items_item_id FOREIGN KEY (item_id) REFERENCES public.items(id) ON DELETE SET NULL;


--
-- Name: reconciliation_issues fk_reconciliation_issues_offline_submissions_offline_submissio; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT fk_reconciliation_issues_offline_submissions_offline_submissio FOREIGN KEY (offline_submission_id) REFERENCES public.offline_submissions(id) ON DELETE RESTRICT;


--
-- Name: reconciliation_issues fk_reconciliation_issues_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT fk_reconciliation_issues_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE SET NULL;


--
-- Name: reconciliation_issues fk_reconciliation_issues_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT fk_reconciliation_issues_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: reconciliation_issues fk_reconciliation_issues_stock_transactions_resolution_stock_t; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reconciliation_issues
    ADD CONSTRAINT fk_reconciliation_issues_stock_transactions_resolution_stock_t FOREIGN KEY (resolution_stock_transaction_id) REFERENCES public.stock_transactions(id) ON DELETE RESTRICT;


--
-- Name: refresh_tokens fk_refresh_tokens_auth_sessions_auth_session_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_auth_sessions_auth_session_id FOREIGN KEY (auth_session_id) REFERENCES public.auth_sessions(id) ON DELETE CASCADE;


--
-- Name: refresh_tokens fk_refresh_tokens_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_batch_disposition_changes_quarantin; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_batch_disposition_changes_quarantin FOREIGN KEY (quarantine_change_id, shop_id) REFERENCES public.batch_disposition_changes(id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_inventory_batches_inventory_batch_i; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_inventory_batches_inventory_batch_i FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_items_item_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_items_item_id_shop_id FOREIGN KEY (item_id, shop_id) REFERENCES public.items(id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_sale_cancellations_sale_cancellatio; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_sale_cancellations_sale_cancellatio FOREIGN KEY (sale_cancellation_id, sale_id, shop_id) REFERENCES public.sale_cancellations(id, sale_id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_sale_items_sale_item_id_sale_id_sho; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_sale_items_sale_item_id_sale_id_sho FOREIGN KEY (sale_item_id, sale_id, shop_id) REFERENCES public.sale_items(id, sale_id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: sale_cancellation_lines fk_sale_cancellation_lines_stock_transactions_stock_transactio; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellation_lines
    ADD CONSTRAINT fk_sale_cancellation_lines_stock_transactions_stock_transactio FOREIGN KEY (stock_transaction_id, item_id, inventory_batch_id, shop_id) REFERENCES public.stock_transactions(id, item_id, inventory_batch_id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellations fk_sale_cancellations_customer_ledger_entries_customer_ledger_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellations
    ADD CONSTRAINT fk_sale_cancellations_customer_ledger_entries_customer_ledger_ FOREIGN KEY (customer_ledger_entry_id, shop_id) REFERENCES public.customer_ledger_entries(id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellations fk_sale_cancellations_sales_sale_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellations
    ADD CONSTRAINT fk_sale_cancellations_sales_sale_id_shop_id FOREIGN KEY (sale_id, shop_id) REFERENCES public.sales(id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_cancellations fk_sale_cancellations_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_cancellations
    ADD CONSTRAINT fk_sale_cancellations_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: sale_items fk_sale_items_inventory_batches_inventory_batch_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT fk_sale_items_inventory_batches_inventory_batch_id FOREIGN KEY (inventory_batch_id) REFERENCES public.inventory_batches(id) ON DELETE RESTRICT;


--
-- Name: sale_items fk_sale_items_items_item_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT fk_sale_items_items_item_id FOREIGN KEY (item_id) REFERENCES public.items(id) ON DELETE RESTRICT;


--
-- Name: sale_items fk_sale_items_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT fk_sale_items_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE CASCADE;


--
-- Name: sale_items fk_sale_items_services_service_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT fk_sale_items_services_service_id FOREIGN KEY (service_id) REFERENCES public.services(id) ON DELETE RESTRICT;


--
-- Name: sale_items fk_sale_items_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT fk_sale_items_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: sale_return_items fk_sale_return_items_batch_disposition_changes_quarantine_chan; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_items
    ADD CONSTRAINT fk_sale_return_items_batch_disposition_changes_quarantine_chan FOREIGN KEY (quarantine_change_id, shop_id) REFERENCES public.batch_disposition_changes(id, shop_id) ON DELETE RESTRICT;


--
-- Name: sale_return_items fk_sale_return_items_sale_items_sale_item_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_items
    ADD CONSTRAINT fk_sale_return_items_sale_items_sale_item_id FOREIGN KEY (sale_item_id) REFERENCES public.sale_items(id) ON DELETE RESTRICT;


--
-- Name: sale_return_items fk_sale_return_items_sale_returns_sale_return_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_items
    ADD CONSTRAINT fk_sale_return_items_sale_returns_sale_return_id FOREIGN KEY (sale_return_id) REFERENCES public.sale_returns(id) ON DELETE CASCADE;


--
-- Name: sale_return_items fk_sale_return_items_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_items
    ADD CONSTRAINT fk_sale_return_items_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE RESTRICT;


--
-- Name: sale_return_items fk_sale_return_items_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_items
    ADD CONSTRAINT fk_sale_return_items_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: sale_return_settlements fk_sale_return_settlements_credit_notes_credit_note_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_settlements
    ADD CONSTRAINT fk_sale_return_settlements_credit_notes_credit_note_id FOREIGN KEY (credit_note_id) REFERENCES public.credit_notes(id) ON DELETE RESTRICT;


--
-- Name: sale_return_settlements fk_sale_return_settlements_customer_ledger_entries_customer_le; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_settlements
    ADD CONSTRAINT fk_sale_return_settlements_customer_ledger_entries_customer_le FOREIGN KEY (customer_ledger_entry_id) REFERENCES public.customer_ledger_entries(id) ON DELETE RESTRICT;


--
-- Name: sale_return_settlements fk_sale_return_settlements_customers_customer_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_settlements
    ADD CONSTRAINT fk_sale_return_settlements_customers_customer_id FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE RESTRICT;


--
-- Name: sale_return_settlements fk_sale_return_settlements_sale_returns_sale_return_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_settlements
    ADD CONSTRAINT fk_sale_return_settlements_sale_returns_sale_return_id FOREIGN KEY (sale_return_id) REFERENCES public.sale_returns(id) ON DELETE CASCADE;


--
-- Name: sale_return_settlements fk_sale_return_settlements_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_return_settlements
    ADD CONSTRAINT fk_sale_return_settlements_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: sale_returns fk_sale_returns_sales_sale_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_returns
    ADD CONSTRAINT fk_sale_returns_sales_sale_id FOREIGN KEY (sale_id) REFERENCES public.sales(id) ON DELETE RESTRICT;


--
-- Name: sale_returns fk_sale_returns_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_returns
    ADD CONSTRAINT fk_sale_returns_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: sales fk_sales_customers_customer_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT fk_sales_customers_customer_id FOREIGN KEY (customer_id) REFERENCES public.customers(id) ON DELETE SET NULL;


--
-- Name: sales fk_sales_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT fk_sales_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: sales fk_sales_till_sessions_till_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT fk_sales_till_sessions_till_session_id_shop_id FOREIGN KEY (till_session_id, shop_id) REFERENCES public.till_sessions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: service_code_sequences fk_service_code_sequences_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.service_code_sequences
    ADD CONSTRAINT fk_service_code_sequences_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: services fk_services_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.services
    ADD CONSTRAINT fk_services_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: shop_expiry_states fk_shop_expiry_states_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_expiry_states
    ADD CONSTRAINT fk_shop_expiry_states_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: shop_license_states fk_shop_license_states_account_license_restrictions_account_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_license_states
    ADD CONSTRAINT fk_shop_license_states_account_license_restrictions_account_id FOREIGN KEY (account_id) REFERENCES public.account_license_restrictions(account_id) ON DELETE RESTRICT;


--
-- Name: shop_license_states fk_shop_license_states_license_grant_evidence_evidence_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_license_states
    ADD CONSTRAINT fk_shop_license_states_license_grant_evidence_evidence_id FOREIGN KEY (evidence_id) REFERENCES public.license_grant_evidence(event_id) ON DELETE RESTRICT;


--
-- Name: shop_license_states fk_shop_license_states_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_license_states
    ADD CONSTRAINT fk_shop_license_states_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: shop_memberships fk_shop_memberships_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_memberships
    ADD CONSTRAINT fk_shop_memberships_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: shop_memberships fk_shop_memberships_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_memberships
    ADD CONSTRAINT fk_shop_memberships_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: shop_setup_preferences fk_shop_setup_preferences_shop_memberships_shop_id_sample_rece; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_setup_preferences
    ADD CONSTRAINT fk_shop_setup_preferences_shop_memberships_shop_id_sample_rece FOREIGN KEY (shop_id, sample_receipt_acknowledged_by_user_id) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: shop_setup_preferences fk_shop_setup_preferences_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_setup_preferences
    ADD CONSTRAINT fk_shop_setup_preferences_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: shop_till_setting_events fk_shop_till_setting_events_shop_memberships_shop_id_actor_use; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_till_setting_events
    ADD CONSTRAINT fk_shop_till_setting_events_shop_memberships_shop_id_actor_use FOREIGN KEY (shop_id, actor_user_id) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: shop_till_setting_events fk_shop_till_setting_events_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_till_setting_events
    ADD CONSTRAINT fk_shop_till_setting_events_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: shop_till_settings fk_shop_till_settings_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_till_settings
    ADD CONSTRAINT fk_shop_till_settings_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: stock_counts fk_stock_counts_inventory_adjustments_inventory_adjustment_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_counts
    ADD CONSTRAINT fk_stock_counts_inventory_adjustments_inventory_adjustment_id FOREIGN KEY (inventory_adjustment_id) REFERENCES public.inventory_adjustments(id) ON DELETE RESTRICT;


--
-- Name: stock_counts fk_stock_counts_inventory_batches_inventory_batch_id_item_id_s; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_counts
    ADD CONSTRAINT fk_stock_counts_inventory_batches_inventory_batch_id_item_id_s FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: stock_counts fk_stock_counts_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_counts
    ADD CONSTRAINT fk_stock_counts_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: stock_counts fk_stock_counts_stock_transactions_stock_transaction_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_counts
    ADD CONSTRAINT fk_stock_counts_stock_transactions_stock_transaction_id FOREIGN KEY (stock_transaction_id) REFERENCES public.stock_transactions(id) ON DELETE RESTRICT;


--
-- Name: stock_transactions fk_stock_transactions_inventory_batches_inventory_batch_id_ite; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_transactions
    ADD CONSTRAINT fk_stock_transactions_inventory_batches_inventory_batch_id_ite FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: stock_transactions fk_stock_transactions_items_item_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_transactions
    ADD CONSTRAINT fk_stock_transactions_items_item_id_shop_id FOREIGN KEY (item_id, shop_id) REFERENCES public.items(id, shop_id) ON DELETE RESTRICT;


--
-- Name: stock_transactions fk_stock_transactions_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.stock_transactions
    ADD CONSTRAINT fk_stock_transactions_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: supplier_ledger_entries fk_supplier_ledger_entries_inventory_batches_batch_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_ledger_entries
    ADD CONSTRAINT fk_supplier_ledger_entries_inventory_batches_batch_id FOREIGN KEY (batch_id) REFERENCES public.inventory_batches(id) ON DELETE RESTRICT;


--
-- Name: supplier_ledger_entries fk_supplier_ledger_entries_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_ledger_entries
    ADD CONSTRAINT fk_supplier_ledger_entries_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: supplier_ledger_entries fk_supplier_ledger_entries_suppliers_supplier_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_ledger_entries
    ADD CONSTRAINT fk_supplier_ledger_entries_suppliers_supplier_id FOREIGN KEY (supplier_id) REFERENCES public.suppliers(id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credit_lines fk_supplier_return_credit_lines_purchase_receipt_values_receip; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT fk_supplier_return_credit_lines_purchase_receipt_values_receip FOREIGN KEY (receipt_value_id, shop_id) REFERENCES public.purchase_receipt_values(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credit_lines fk_supplier_return_credit_lines_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT fk_supplier_return_credit_lines_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credit_lines fk_supplier_return_credit_lines_supplier_ledger_entries_suppli; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT fk_supplier_return_credit_lines_supplier_ledger_entries_suppli FOREIGN KEY (supplier_ledger_entry_id, shop_id) REFERENCES public.supplier_ledger_entries(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credit_lines fk_supplier_return_credit_lines_supplier_return_credits_suppli; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT fk_supplier_return_credit_lines_supplier_return_credits_suppli FOREIGN KEY (supplier_return_credit_id, shop_id) REFERENCES public.supplier_return_credits(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credit_lines fk_supplier_return_credit_lines_supplier_return_lines_return_l; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credit_lines
    ADD CONSTRAINT fk_supplier_return_credit_lines_supplier_return_lines_return_l FOREIGN KEY (return_line_id, shop_id) REFERENCES public.supplier_return_lines(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credits fk_supplier_return_credits_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credits
    ADD CONSTRAINT fk_supplier_return_credits_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: supplier_return_credits fk_supplier_return_credits_supplier_returns_supplier_return_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_credits
    ADD CONSTRAINT fk_supplier_return_credits_supplier_returns_supplier_return_id FOREIGN KEY (supplier_return_id, shop_id) REFERENCES public.supplier_returns(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_lines fk_supplier_return_lines_inventory_batches_inventory_batch_id_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT fk_supplier_return_lines_inventory_batches_inventory_batch_id_ FOREIGN KEY (inventory_batch_id, item_id, shop_id) REFERENCES public.inventory_batches(id, item_id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_lines fk_supplier_return_lines_purchase_receipt_values_receipt_value; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT fk_supplier_return_lines_purchase_receipt_values_receipt_value FOREIGN KEY (receipt_value_id, shop_id) REFERENCES public.purchase_receipt_values(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_lines fk_supplier_return_lines_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT fk_supplier_return_lines_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: supplier_return_lines fk_supplier_return_lines_stock_transactions_stock_transaction_; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT fk_supplier_return_lines_stock_transactions_stock_transaction_ FOREIGN KEY (stock_transaction_id, shop_id) REFERENCES public.stock_transactions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_return_lines fk_supplier_return_lines_supplier_returns_supplier_return_id_s; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_return_lines
    ADD CONSTRAINT fk_supplier_return_lines_supplier_returns_supplier_return_id_s FOREIGN KEY (supplier_return_id, shop_id) REFERENCES public.supplier_returns(id, shop_id) ON DELETE RESTRICT;


--
-- Name: supplier_returns fk_supplier_returns_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_returns
    ADD CONSTRAINT fk_supplier_returns_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: supplier_returns fk_supplier_returns_suppliers_supplier_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_returns
    ADD CONSTRAINT fk_supplier_returns_suppliers_supplier_id_shop_id FOREIGN KEY (supplier_id, shop_id) REFERENCES public.suppliers(id, shop_id) ON DELETE RESTRICT;


--
-- Name: suppliers fk_suppliers_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suppliers
    ADD CONSTRAINT fk_suppliers_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE CASCADE;


--
-- Name: till_close_reviews fk_till_close_reviews_shop_memberships_shop_id_reviewed_by; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_close_reviews
    ADD CONSTRAINT fk_till_close_reviews_shop_memberships_shop_id_reviewed_by FOREIGN KEY (shop_id, reviewed_by) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_close_reviews fk_till_close_reviews_till_closes_close_id_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_close_reviews
    ADD CONSTRAINT fk_till_close_reviews_till_closes_close_id_session_id_shop_id FOREIGN KEY (close_id, session_id, shop_id) REFERENCES public.till_closes(id, session_id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_closes fk_till_closes_shop_memberships_shop_id_closed_by; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_closes
    ADD CONSTRAINT fk_till_closes_shop_memberships_shop_id_closed_by FOREIGN KEY (shop_id, closed_by) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_closes fk_till_closes_till_sessions_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_closes
    ADD CONSTRAINT fk_till_closes_till_sessions_session_id_shop_id FOREIGN KEY (session_id, shop_id) REFERENCES public.till_sessions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_late_postings fk_till_late_postings_offline_submissions_offline_submission_i; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT fk_till_late_postings_offline_submissions_offline_submission_i FOREIGN KEY (offline_submission_id, shop_id) REFERENCES public.offline_submissions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_late_postings fk_till_late_postings_reconciliation_issues_case_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT fk_till_late_postings_reconciliation_issues_case_id_shop_id FOREIGN KEY (case_id, shop_id) REFERENCES public.reconciliation_issues(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_late_postings fk_till_late_postings_sales_sale_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT fk_till_late_postings_sales_sale_id_shop_id FOREIGN KEY (sale_id, shop_id) REFERENCES public.sales(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_late_postings fk_till_late_postings_shop_memberships_shop_id_cashier_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT fk_till_late_postings_shop_memberships_shop_id_cashier_id FOREIGN KEY (shop_id, cashier_id) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_late_postings fk_till_late_postings_till_closes_close_id_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT fk_till_late_postings_till_closes_close_id_session_id_shop_id FOREIGN KEY (close_id, session_id, shop_id) REFERENCES public.till_closes(id, session_id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_late_postings fk_till_late_postings_till_sessions_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_late_postings
    ADD CONSTRAINT fk_till_late_postings_till_sessions_session_id_shop_id FOREIGN KEY (session_id, shop_id) REFERENCES public.till_sessions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_movements fk_till_movements_bank_accounts_bank_account_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT fk_till_movements_bank_accounts_bank_account_id_shop_id FOREIGN KEY (bank_account_id, shop_id) REFERENCES public.bank_accounts(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_movements fk_till_movements_shop_memberships_shop_id_actor_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT fk_till_movements_shop_memberships_shop_id_actor_user_id FOREIGN KEY (shop_id, actor_user_id) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_movements fk_till_movements_till_movements_reverses_movement_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT fk_till_movements_till_movements_reverses_movement_id_shop_id FOREIGN KEY (reverses_movement_id, shop_id) REFERENCES public.till_movements(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_movements fk_till_movements_till_sessions_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT fk_till_movements_till_sessions_session_id_shop_id FOREIGN KEY (session_id, shop_id) REFERENCES public.till_sessions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_movements fk_till_movements_till_source_records_source_record_id_session; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_movements
    ADD CONSTRAINT fk_till_movements_till_source_records_source_record_id_session FOREIGN KEY (source_record_id, session_id, shop_id) REFERENCES public.till_source_records(id, session_id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_sessions fk_till_sessions_shop_memberships_shop_id_cashier_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_sessions
    ADD CONSTRAINT fk_till_sessions_shop_memberships_shop_id_cashier_id FOREIGN KEY (shop_id, cashier_id) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_sessions fk_till_sessions_shop_memberships_shop_id_opened_by; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_sessions
    ADD CONSTRAINT fk_till_sessions_shop_memberships_shop_id_opened_by FOREIGN KEY (shop_id, opened_by) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_sessions fk_till_sessions_tills_till_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_sessions
    ADD CONSTRAINT fk_till_sessions_tills_till_id_shop_id FOREIGN KEY (till_id, shop_id) REFERENCES public.tills(id, shop_id) ON DELETE RESTRICT;


--
-- Name: till_source_records fk_till_source_records_shop_memberships_shop_id_actor_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_source_records
    ADD CONSTRAINT fk_till_source_records_shop_memberships_shop_id_actor_user_id FOREIGN KEY (shop_id, actor_user_id) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: till_source_records fk_till_source_records_till_sessions_session_id_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.till_source_records
    ADD CONSTRAINT fk_till_source_records_till_sessions_session_id_shop_id FOREIGN KEY (session_id, shop_id) REFERENCES public.till_sessions(id, shop_id) ON DELETE RESTRICT;


--
-- Name: tills fk_tills_shop_memberships_shop_id_created_by; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tills
    ADD CONSTRAINT fk_tills_shop_memberships_shop_id_created_by FOREIGN KEY (shop_id, created_by) REFERENCES public.shop_memberships(shop_id, user_id) ON DELETE RESTRICT;


--
-- Name: tills fk_tills_shops_shop_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tills
    ADD CONSTRAINT fk_tills_shops_shop_id FOREIGN KEY (shop_id) REFERENCES public.shops(id) ON DELETE RESTRICT;


--
-- Name: merchant_coverage_exception_activations merchant_coverage_exception_activations_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_activations
    ADD CONSTRAINT merchant_coverage_exception_activations_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: merchant_coverage_exception_cutoffs merchant_coverage_exception_cutoffs_decision_event_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_cutoffs
    ADD CONSTRAINT merchant_coverage_exception_cutoffs_decision_event_id_fkey FOREIGN KEY (decision_event_id) REFERENCES public.merchant_coverage_exception_decisions(event_id);


--
-- Name: merchant_coverage_exception_cutoffs merchant_coverage_exception_cutoffs_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_cutoffs
    ADD CONSTRAINT merchant_coverage_exception_cutoffs_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: merchant_coverage_exception_decisions merchant_coverage_exception_decisions_incoming_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_decisions
    ADD CONSTRAINT merchant_coverage_exception_decisions_incoming_shop_id_fkey FOREIGN KEY (incoming_shop_id) REFERENCES public.shops(id);


--
-- Name: merchant_coverage_exception_decisions merchant_coverage_exception_decisions_outgoing_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_coverage_exception_decisions
    ADD CONSTRAINT merchant_coverage_exception_decisions_outgoing_shop_id_fkey FOREIGN KEY (outgoing_shop_id) REFERENCES public.shops(id);


--
-- Name: merchant_transfer_decisions merchant_transfer_decisions_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merchant_transfer_decisions
    ADD CONSTRAINT merchant_transfer_decisions_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: reduction_manifest_shops reduction_manifest_shops_account_id_schedule_id_generation_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reduction_manifest_shops
    ADD CONSTRAINT reduction_manifest_shops_account_id_schedule_id_generation_fkey FOREIGN KEY (account_id, schedule_id, generation, page_index) REFERENCES public.reduction_manifest_pages(account_id, schedule_id, generation, page_index);


--
-- Name: shop_affiliation_consent_events shop_affiliation_consent_events_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_consent_events
    ADD CONSTRAINT shop_affiliation_consent_events_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: shop_affiliation_delivery_receipts shop_affiliation_delivery_receipts_event_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_delivery_receipts
    ADD CONSTRAINT shop_affiliation_delivery_receipts_event_id_fkey FOREIGN KEY (event_id) REFERENCES public.shop_affiliation_consent_events(id);


--
-- Name: shop_affiliation_delivery_receipts shop_affiliation_delivery_receipts_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_affiliation_delivery_receipts
    ADD CONSTRAINT shop_affiliation_delivery_receipts_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: shop_closure_consents shop_closure_consents_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_closure_consents
    ADD CONSTRAINT shop_closure_consents_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: shop_trial_consents shop_trial_consents_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_consents
    ADD CONSTRAINT shop_trial_consents_shop_id_fkey FOREIGN KEY (shop_id) REFERENCES public.shops(id);


--
-- Name: shop_trial_receipts shop_trial_receipts_event_id_shop_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_trial_receipts
    ADD CONSTRAINT shop_trial_receipts_event_id_shop_id_fkey FOREIGN KEY (event_id, shop_id) REFERENCES public.shop_trial_consents(event_id, shop_id);


--
-- Name: account_license_restrictions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.account_license_restrictions ENABLE ROW LEVEL SECURITY;

--
-- Name: account_license_restrictions account_license_restrictions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY account_license_restrictions_shop_isolation ON public.account_license_restrictions USING (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) OR (CURRENT_USER = 'baseline_owner_zq'::name) OR (EXISTS ( SELECT
   FROM public.shop_license_states s
  WHERE ((s.account_id = account_license_restrictions.account_id) AND (s.shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)))))) WITH CHECK ((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid))$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: merchant_coverage_exception_activations activation_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY activation_insert ON public.merchant_coverage_exception_activations FOR INSERT WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: merchant_coverage_exception_activations activation_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY activation_read ON public.merchant_coverage_exception_activations FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_consent_events affiliation_worker_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY affiliation_worker_read ON public.shop_affiliation_consent_events FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: shop_affiliation_delivery_receipts affiliation_worker_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY affiliation_worker_read ON public.shop_affiliation_delivery_receipts FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: applied_shop_capacity applied_capacity_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY applied_capacity_insert ON public.applied_shop_capacity FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.capacity_reservation_receipts r
  WHERE ((r.account_id = applied_shop_capacity.account_id) AND (r.term_id = applied_shop_capacity.term_id) AND (r.revision = applied_shop_capacity.revision) AND (r.event_id = applied_shop_capacity.applied_event) AND (r.outcome = 'Applied'::text) AND (r.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: applied_shop_capacity applied_capacity_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY applied_capacity_read ON public.applied_shop_capacity FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: applied_shop_reductions applied_reduction_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY applied_reduction_insert ON public.applied_shop_reductions FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.reduction_reservation_receipts r
  WHERE ((r.account_id = applied_shop_reductions.account_id) AND (r.term_id = applied_shop_reductions.predecessor_term_id) AND (r.schedule_id = applied_shop_reductions.schedule_id) AND (r.generation = applied_shop_reductions.generation) AND (r.event_id = applied_shop_reductions.applied_event) AND (r.outcome = 'Applied'::text) AND (r.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: applied_shop_reductions applied_reduction_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY applied_reduction_read ON public.applied_shop_reductions FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: applied_shop_capacity; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.applied_shop_capacity ENABLE ROW LEVEL SECURITY;

--
-- Name: applied_shop_reductions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.applied_shop_reductions ENABLE ROW LEVEL SECURITY;

--
-- Name: bank_accounts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.bank_accounts ENABLE ROW LEVEL SECURITY;

--
-- Name: bank_accounts bank_accounts_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY bank_accounts_shop_isolation ON public.bank_accounts USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: batch_disposition_changes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.batch_disposition_changes ENABLE ROW LEVEL SECURITY;

--
-- Name: batch_disposition_changes batch_disposition_changes_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY batch_disposition_changes_shop_isolation ON public.batch_disposition_changes USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: batch_expiry_revisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.batch_expiry_revisions ENABLE ROW LEVEL SECURITY;

--
-- Name: batch_expiry_revisions batch_expiry_revisions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY batch_expiry_revisions_shop_isolation ON public.batch_expiry_revisions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: batch_price_revisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.batch_price_revisions ENABLE ROW LEVEL SECURITY;

--
-- Name: batch_price_revisions batch_price_revisions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY batch_price_revisions_shop_isolation ON public.batch_price_revisions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: capacity_reservation_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.capacity_reservation_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_exception_consent_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_exception_consent_events ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_replacement_activations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_replacement_activations ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_replacement_consent_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_replacement_consent_events ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_slot_activations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_slot_activations ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_slot_consent_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_slot_consent_events ENABLE ROW LEVEL SECURITY;

--
-- Name: coverage_transfer_consent_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.coverage_transfer_consent_events ENABLE ROW LEVEL SECURITY;

--
-- Name: credit_note_redemptions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.credit_note_redemptions ENABLE ROW LEVEL SECURITY;

--
-- Name: credit_note_redemptions credit_note_redemptions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY credit_note_redemptions_shop_isolation ON public.credit_note_redemptions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: credit_notes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.credit_notes ENABLE ROW LEVEL SECURITY;

--
-- Name: credit_notes credit_notes_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY credit_notes_shop_isolation ON public.credit_notes USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: customer_ledger_entries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.customer_ledger_entries ENABLE ROW LEVEL SECURITY;

--
-- Name: customer_ledger_entries customer_ledger_entries_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY customer_ledger_entries_shop_isolation ON public.customer_ledger_entries USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: customers; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.customers ENABLE ROW LEVEL SECURITY;

--
-- Name: customers customers_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY customers_shop_isolation ON public.customers USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: merchant_coverage_exception_cutoffs cutoff_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cutoff_insert ON public.merchant_coverage_exception_cutoffs FOR INSERT WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.merchant_coverage_exception_decisions d
  WHERE ((d.event_id = merchant_coverage_exception_cutoffs.decision_event_id) AND (d.exception_id = merchant_coverage_exception_cutoffs.exception_id) AND (d.generation = merchant_coverage_exception_cutoffs.generation) AND (d.account_id = merchant_coverage_exception_cutoffs.account_id) AND (d.outgoing_shop_id = merchant_coverage_exception_cutoffs.shop_id) AND (d.outcome = 'Consumed'::text) AND (d.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: merchant_coverage_exception_cutoffs cutoff_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY cutoff_read ON public.merchant_coverage_exception_cutoffs FOR SELECT USING (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) OR (account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid)));


--
-- Name: merchant_coverage_exception_decisions decision_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY decision_scope ON public.merchant_coverage_exception_decisions USING (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) OR (((NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid = outgoing_shop_id) OR ((NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid = incoming_shop_id)))) WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_request_uses u
  WHERE (u.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: shop_trial_receipts delivery_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY delivery_insert ON public.shop_trial_receipts FOR INSERT WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: discount_rules; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.discount_rules ENABLE ROW LEVEL SECURITY;

--
-- Name: discount_rules discount_rules_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY discount_rules_shop_isolation ON public.discount_rules USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_messages; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_messages ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_outbox; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_outbox ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_reconciliation_requests; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_reconciliation_requests ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_request_uses; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_request_uses ENABLE ROW LEVEL SECURITY;

--
-- Name: entitlement_messages entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_messages USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_outbox entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_outbox USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_reconciliation_requests entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_reconciliation_requests USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_request_uses entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_request_uses USING ((replay_id = (NULLIF(current_setting('app.entitlement_replay_id'::text, true), ''::text))::uuid)) WITH CHECK ((replay_id = (NULLIF(current_setting('app.entitlement_replay_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_snapshot_inbox entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.entitlement_snapshot_inbox USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_closure_consents entitlement_scope; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY entitlement_scope ON public.shop_closure_consents USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: entitlement_snapshot_inbox; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.entitlement_snapshot_inbox ENABLE ROW LEVEL SECURITY;

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
-- Name: entitlement_reconciliation_requests entitlement_worker; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY entitlement_worker ON public.entitlement_reconciliation_requests TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: expense_categories; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.expense_categories ENABLE ROW LEVEL SECURITY;

--
-- Name: expense_categories expense_categories_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY expense_categories_shop_isolation ON public.expense_categories USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: expenses; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.expenses ENABLE ROW LEVEL SECURITY;

--
-- Name: expenses expenses_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY expenses_shop_isolation ON public.expenses USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: expiry_policy_revisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.expiry_policy_revisions ENABLE ROW LEVEL SECURITY;

--
-- Name: expiry_policy_revisions expiry_policy_revisions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY expiry_policy_revisions_shop_isolation ON public.expiry_policy_revisions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: inventory; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.inventory ENABLE ROW LEVEL SECURITY;

--
-- Name: inventory_adjustments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.inventory_adjustments ENABLE ROW LEVEL SECURITY;

--
-- Name: inventory_adjustments inventory_adjustments_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY inventory_adjustments_shop_isolation ON public.inventory_adjustments USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: inventory_batches; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.inventory_batches ENABLE ROW LEVEL SECURITY;

--
-- Name: inventory_batches inventory_batches_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY inventory_batches_shop_isolation ON public.inventory_batches USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: inventory inventory_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY inventory_shop_isolation ON public.inventory USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: invoice_leases; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.invoice_leases ENABLE ROW LEVEL SECURITY;

--
-- Name: invoice_leases invoice_leases_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invoice_leases_shop_isolation ON public.invoice_leases USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: invoice_sequences; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.invoice_sequences ENABLE ROW LEVEL SECURITY;

--
-- Name: invoice_sequences invoice_sequences_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY invoice_sequences_shop_isolation ON public.invoice_sequences USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: item_barcode_sequences; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.item_barcode_sequences ENABLE ROW LEVEL SECURITY;

--
-- Name: item_barcode_sequences item_barcode_sequences_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY item_barcode_sequences_shop_isolation ON public.item_barcode_sequences USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: items; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.items ENABLE ROW LEVEL SECURITY;

--
-- Name: items items_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY items_shop_isolation ON public.items USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: license_grant_evidence; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.license_grant_evidence ENABLE ROW LEVEL SECURITY;

--
-- Name: license_grant_evidence license_grant_evidence_shop_isolation; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY license_grant_evidence_shop_isolation ON public.license_grant_evidence USING (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) OR (CURRENT_USER = 'baseline_owner_zq'::name))) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid))$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: license_history_review_observations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.license_history_review_observations ENABLE ROW LEVEL SECURITY;

--
-- Name: license_history_review_observations license_history_review_observations_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY license_history_review_observations_shop_isolation ON public.license_history_review_observations USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_delivery_receipts matching_receipt; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY matching_receipt ON public.shop_affiliation_delivery_receipts FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.shop_affiliation_consent_events e
  WHERE ((e.id = shop_affiliation_delivery_receipts.event_id) AND (e.shop_id = shop_affiliation_delivery_receipts.shop_id) AND (e.kind = 'Accepted'::text))))));


--
-- Name: merchant_coverage_exception_activations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.merchant_coverage_exception_activations ENABLE ROW LEVEL SECURITY;

--
-- Name: merchant_coverage_exception_cutoffs; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.merchant_coverage_exception_cutoffs ENABLE ROW LEVEL SECURITY;

--
-- Name: merchant_coverage_exception_decisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.merchant_coverage_exception_decisions ENABLE ROW LEVEL SECURITY;

--
-- Name: merchant_transfer_decisions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.merchant_transfer_decisions ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_expiry_evidence; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.offline_expiry_evidence ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_expiry_evidence offline_expiry_evidence_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY offline_expiry_evidence_shop_isolation ON public.offline_expiry_evidence USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: offline_license_grants; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.offline_license_grants ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_license_grants offline_license_grants_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY offline_license_grants_shop_isolation ON public.offline_license_grants USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: offline_license_permits; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.offline_license_permits ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_license_permits offline_license_permits_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY offline_license_permits_shop_isolation ON public.offline_license_permits USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: offline_license_verification_keys; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.offline_license_verification_keys ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_license_verification_keys offline_license_verification_keys_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY offline_license_verification_keys_shop_isolation ON public.offline_license_verification_keys USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: offline_submissions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.offline_submissions ENABLE ROW LEVEL SECURITY;

--
-- Name: offline_submissions offline_submissions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY offline_submissions_shop_isolation ON public.offline_submissions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: operation_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.operation_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: operation_receipts operation_receipts_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY operation_receipts_shop_isolation ON public.operation_receipts USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_exception_consent_events owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_insert ON public.coverage_exception_consent_events FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.current_user_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.shop_memberships m
  WHERE ((m.user_id = coverage_exception_consent_events.actor_id) AND (m.id = coverage_exception_consent_events.membership_id) AND (m.shop_id = coverage_exception_consent_events.shop_id) AND (m.authority_version = coverage_exception_consent_events.authority_version) AND ((m.role)::text = 'Owner'::text) AND ((m.status)::text = 'Active'::text))))));


--
-- Name: coverage_replacement_consent_events owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_insert ON public.coverage_replacement_consent_events FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.current_user_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.shop_memberships m
  WHERE ((m.user_id = coverage_replacement_consent_events.actor_id) AND (m.id = coverage_replacement_consent_events.membership_id) AND (m.shop_id = coverage_replacement_consent_events.shop_id) AND (m.authority_version = coverage_replacement_consent_events.authority_version) AND ((m.role)::text = 'Owner'::text) AND ((m.status)::text = 'Active'::text))))));


--
-- Name: coverage_slot_consent_events owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_insert ON public.coverage_slot_consent_events FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.current_user_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.shop_memberships m
  WHERE ((m.user_id = coverage_slot_consent_events.actor_id) AND (m.id = coverage_slot_consent_events.membership_id) AND (m.shop_id = coverage_slot_consent_events.shop_id) AND (m.authority_version = coverage_slot_consent_events.authority_version) AND ((m.role)::text = 'Owner'::text) AND ((m.status)::text = 'Active'::text))))));


--
-- Name: coverage_transfer_consent_events owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_insert ON public.coverage_transfer_consent_events FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.current_user_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.shop_memberships m
  WHERE ((m.user_id = coverage_transfer_consent_events.actor_id) AND (m.id = coverage_transfer_consent_events.membership_id) AND (m.shop_id = coverage_transfer_consent_events.shop_id) AND (m.authority_version = coverage_transfer_consent_events.authority_version) AND ((m.role)::text = 'Owner'::text) AND ((m.status)::text = 'Active'::text))))));


--
-- Name: shop_affiliation_consent_events owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_insert ON public.shop_affiliation_consent_events FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.current_user_id'::text, true), ''::text))::uuid)));


--
-- Name: shop_trial_consents owner_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY owner_insert ON public.shop_trial_consents FOR INSERT WITH CHECK (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (actor_id = (NULLIF(current_setting('app.current_user_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.shop_memberships m
  WHERE ((m.id = shop_trial_consents.membership_id) AND (m.shop_id = shop_trial_consents.shop_id) AND (m.user_id = shop_trial_consents.actor_id) AND ((m.role)::text = 'Owner'::text) AND ((m.status)::text = 'Active'::text) AND (m.authority_version = shop_trial_consents.authority_version)))) AND (active_members = ( SELECT count(*) AS count
   FROM public.shop_memberships m
  WHERE ((m.shop_id = shop_trial_consents.shop_id) AND ((m.status)::text = 'Active'::text))))));


--
-- Name: payment_allocations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_allocations ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_allocations payment_allocations_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY payment_allocations_shop_isolation ON public.payment_allocations USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: payment_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_receipts payment_receipts_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY payment_receipts_shop_isolation ON public.payment_receipts USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: payment_reversals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.payment_reversals ENABLE ROW LEVEL SECURITY;

--
-- Name: payment_reversals payment_reversals_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY payment_reversals_shop_isolation ON public.payment_reversals USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: posting_period_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.posting_period_events ENABLE ROW LEVEL SECURITY;

--
-- Name: posting_period_events posting_period_events_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY posting_period_events_shop_isolation ON public.posting_period_events USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: posting_periods; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.posting_periods ENABLE ROW LEVEL SECURITY;

--
-- Name: posting_periods posting_periods_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY posting_periods_shop_isolation ON public.posting_periods USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_order_lines; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_order_lines ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_order_lines purchase_order_lines_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_order_lines_shop_isolation ON public.purchase_order_lines USING ((purchase_order_id IN ( SELECT purchase_orders.id
   FROM public.purchase_orders
  WHERE (purchase_orders.shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)))) WITH CHECK ((purchase_order_id IN ( SELECT purchase_orders.id
   FROM public.purchase_orders
  WHERE (purchase_orders.shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid))));


--
-- Name: purchase_order_receipt_lines; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_order_receipt_lines ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_order_receipt_lines purchase_order_receipt_lines_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_order_receipt_lines_shop_isolation ON public.purchase_order_receipt_lines USING ((purchase_order_receipt_id IN ( SELECT purchase_order_receipts.id
   FROM public.purchase_order_receipts
  WHERE (purchase_order_receipts.shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)))) WITH CHECK ((purchase_order_receipt_id IN ( SELECT purchase_order_receipts.id
   FROM public.purchase_order_receipts
  WHERE (purchase_order_receipts.shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid))));


--
-- Name: purchase_order_receipt_sequences; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_order_receipt_sequences ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_order_receipt_sequences purchase_order_receipt_sequences_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_order_receipt_sequences_shop_isolation ON public.purchase_order_receipt_sequences USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_order_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_order_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_order_receipts purchase_order_receipts_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_order_receipts_shop_isolation ON public.purchase_order_receipts USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_order_sequences; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_order_sequences ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_order_sequences purchase_order_sequences_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_order_sequences_shop_isolation ON public.purchase_order_sequences USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_orders; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_orders ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_orders purchase_orders_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_orders_shop_isolation ON public.purchase_orders USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_receipt_cost_adjustments; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_receipt_cost_adjustments ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_receipt_cost_adjustments purchase_receipt_cost_adjustments_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_receipt_cost_adjustments_shop_isolation ON public.purchase_receipt_cost_adjustments USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_receipt_reversals; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_receipt_reversals ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_receipt_reversals purchase_receipt_reversals_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_receipt_reversals_shop_isolation ON public.purchase_receipt_reversals USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: purchase_receipt_values; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.purchase_receipt_values ENABLE ROW LEVEL SECURITY;

--
-- Name: purchase_receipt_values purchase_receipt_values_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY purchase_receipt_values_shop_isolation ON public.purchase_receipt_values USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: reconciliation_case_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reconciliation_case_events ENABLE ROW LEVEL SECURITY;

--
-- Name: reconciliation_case_events reconciliation_case_events_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reconciliation_case_events_shop_isolation ON public.reconciliation_case_events USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: reconciliation_issues; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reconciliation_issues ENABLE ROW LEVEL SECURITY;

--
-- Name: reconciliation_issues reconciliation_issues_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reconciliation_issues_shop_isolation ON public.reconciliation_issues USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: applied_shop_capacity reduction_fence_capacity_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY reduction_fence_capacity_read ON public.applied_shop_capacity FOR SELECT USING ((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid));


--
-- Name: reduction_manifest_pages; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reduction_manifest_pages ENABLE ROW LEVEL SECURITY;

--
-- Name: reduction_manifest_shops; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reduction_manifest_shops ENABLE ROW LEVEL SECURITY;

--
-- Name: reduction_reservation_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.reduction_reservation_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_cancellation_lines; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sale_cancellation_lines ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_cancellation_lines sale_cancellation_lines_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sale_cancellation_lines_shop_isolation ON public.sale_cancellation_lines USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: sale_cancellations; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sale_cancellations ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_cancellations sale_cancellations_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sale_cancellations_shop_isolation ON public.sale_cancellations USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: sale_items; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sale_items ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_items sale_items_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sale_items_shop_isolation ON public.sale_items USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: sale_return_items; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sale_return_items ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_return_items sale_return_items_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sale_return_items_shop_isolation ON public.sale_return_items USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: sale_return_settlements; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sale_return_settlements ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_return_settlements sale_return_settlements_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sale_return_settlements_shop_isolation ON public.sale_return_settlements USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: sale_returns; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sale_returns ENABLE ROW LEVEL SECURITY;

--
-- Name: sale_returns sale_returns_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sale_returns_shop_isolation ON public.sale_returns USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: sales; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.sales ENABLE ROW LEVEL SECURITY;

--
-- Name: sales sales_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY sales_shop_isolation ON public.sales USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: merchant_transfer_decisions scan_bindings; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY scan_bindings ON public.merchant_transfer_decisions FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: service_code_sequences; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.service_code_sequences ENABLE ROW LEVEL SECURITY;

--
-- Name: service_code_sequences service_code_sequences_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY service_code_sequences_shop_isolation ON public.service_code_sequences USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: services; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.services ENABLE ROW LEVEL SECURITY;

--
-- Name: services services_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY services_shop_isolation ON public.services USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_consent_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_affiliation_consent_events ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_affiliation_delivery_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_affiliation_delivery_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_closure_consents; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_closure_consents ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_expiry_states; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_expiry_states ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_expiry_states shop_expiry_states_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_expiry_states_shop_isolation ON public.shop_expiry_states USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacement_activations shop_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_insert ON public.coverage_replacement_activations FOR INSERT WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_slot_activations shop_insert; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_insert ON public.coverage_slot_activations FOR INSERT WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_license_states; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_license_states ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_license_states shop_license_states_shop_isolation; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY shop_license_states_shop_isolation ON public.shop_license_states USING (((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) OR (CURRENT_USER = 'baseline_owner_zq'::name))) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid))$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: coverage_exception_consent_events shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.coverage_exception_consent_events FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacement_activations shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.coverage_replacement_activations FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_replacement_consent_events shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.coverage_replacement_consent_events FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_slot_activations shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.coverage_slot_activations FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_slot_consent_events shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.coverage_slot_consent_events FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: coverage_transfer_consent_events shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.coverage_transfer_consent_events FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: merchant_transfer_decisions shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.merchant_transfer_decisions FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_consent_events shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.shop_affiliation_consent_events FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_affiliation_delivery_receipts shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.shop_affiliation_delivery_receipts FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_trial_consents shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.shop_trial_consents FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_trial_receipts shop_read; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_read ON public.shop_trial_receipts FOR SELECT USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_setup_preferences; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_setup_preferences ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_setup_preferences shop_setup_preferences_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_setup_preferences_shop_isolation ON public.shop_setup_preferences USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_till_setting_events; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_till_setting_events ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_till_setting_events shop_till_setting_events_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_till_setting_events_shop_isolation ON public.shop_till_setting_events USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_till_settings; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_till_settings ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_till_settings shop_till_settings_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY shop_till_settings_shop_isolation ON public.shop_till_settings USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_trial_consents; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_trial_consents ENABLE ROW LEVEL SECURITY;

--
-- Name: shop_trial_receipts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.shop_trial_receipts ENABLE ROW LEVEL SECURITY;

--
-- Name: capacity_reservation_receipts signed_capacity_event; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY signed_capacity_event ON public.capacity_reservation_receipts USING ((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid)) WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_request_uses u
  WHERE (u.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: reduction_manifest_pages signed_reduction_event; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY signed_reduction_event ON public.reduction_manifest_pages USING ((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid)) WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_request_uses u
  WHERE (u.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: reduction_manifest_shops signed_reduction_event; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY signed_reduction_event ON public.reduction_manifest_shops USING ((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid)) WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_request_uses u
  WHERE (u.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: reduction_reservation_receipts signed_reduction_event; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY signed_reduction_event ON public.reduction_reservation_receipts USING ((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid)) WITH CHECK (((account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_request_uses u
  WHERE (u.xmin = (pg_current_xact_id())::xid)))));


--
-- Name: stock_counts; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.stock_counts ENABLE ROW LEVEL SECURITY;

--
-- Name: stock_counts stock_counts_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY stock_counts_shop_isolation ON public.stock_counts USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: stock_transactions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.stock_transactions ENABLE ROW LEVEL SECURITY;

--
-- Name: stock_transactions stock_transactions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY stock_transactions_shop_isolation ON public.stock_transactions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: supplier_ledger_entries; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.supplier_ledger_entries ENABLE ROW LEVEL SECURITY;

--
-- Name: supplier_ledger_entries supplier_ledger_entries_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY supplier_ledger_entries_shop_isolation ON public.supplier_ledger_entries USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: supplier_return_credit_lines; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.supplier_return_credit_lines ENABLE ROW LEVEL SECURITY;

--
-- Name: supplier_return_credit_lines supplier_return_credit_lines_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY supplier_return_credit_lines_shop_isolation ON public.supplier_return_credit_lines USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: supplier_return_credits; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.supplier_return_credits ENABLE ROW LEVEL SECURITY;

--
-- Name: supplier_return_credits supplier_return_credits_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY supplier_return_credits_shop_isolation ON public.supplier_return_credits USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: supplier_return_lines; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.supplier_return_lines ENABLE ROW LEVEL SECURITY;

--
-- Name: supplier_return_lines supplier_return_lines_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY supplier_return_lines_shop_isolation ON public.supplier_return_lines USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: supplier_returns; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.supplier_returns ENABLE ROW LEVEL SECURITY;

--
-- Name: supplier_returns supplier_returns_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY supplier_returns_shop_isolation ON public.supplier_returns USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: suppliers; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.suppliers ENABLE ROW LEVEL SECURITY;

--
-- Name: suppliers suppliers_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY suppliers_shop_isolation ON public.suppliers USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: till_close_reviews; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.till_close_reviews ENABLE ROW LEVEL SECURITY;

--
-- Name: till_close_reviews till_close_reviews_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY till_close_reviews_shop_isolation ON public.till_close_reviews USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: till_closes; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.till_closes ENABLE ROW LEVEL SECURITY;

--
-- Name: till_closes till_closes_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY till_closes_shop_isolation ON public.till_closes USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: till_late_postings; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.till_late_postings ENABLE ROW LEVEL SECURITY;

--
-- Name: till_late_postings till_late_postings_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY till_late_postings_shop_isolation ON public.till_late_postings USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: till_movements; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.till_movements ENABLE ROW LEVEL SECURITY;

--
-- Name: till_movements till_movements_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY till_movements_shop_isolation ON public.till_movements USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: till_sessions; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.till_sessions ENABLE ROW LEVEL SECURITY;

--
-- Name: till_sessions till_sessions_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY till_sessions_shop_isolation ON public.till_sessions USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: till_source_records; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.till_source_records ENABLE ROW LEVEL SECURITY;

--
-- Name: till_source_records till_source_records_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY till_source_records_shop_isolation ON public.till_source_records USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: tills; Type: ROW SECURITY; Schema: public; Owner: -
--

ALTER TABLE public.tills ENABLE ROW LEVEL SECURITY;

--
-- Name: tills tills_shop_isolation; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY tills_shop_isolation ON public.tills USING ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid)) WITH CHECK ((shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid));


--
-- Name: shop_trial_consents trial_worker_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY trial_worker_read ON public.shop_trial_consents FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: shop_trial_receipts trial_worker_read; Type: POLICY; Schema: public; Owner: -
--
DO $baseline$ BEGIN
    EXECUTE replace(replace($baseline_policy$CREATE POLICY trial_worker_read ON public.shop_trial_receipts FOR SELECT TO baseline_owner_zq USING (true)$baseline_policy$,
        '''baseline_owner_zq''', quote_literal(current_user)),
        'baseline_owner_zq', quote_ident(current_user));
END $baseline$;

--
-- Name: merchant_transfer_decisions verified_command; Type: POLICY; Schema: public; Owner: -
--

CREATE POLICY verified_command ON public.merchant_transfer_decisions FOR INSERT WITH CHECK (((source_account_id = (NULLIF(current_setting('app.entitlement_account_id'::text, true), ''::text))::uuid) AND (shop_id = (NULLIF(current_setting('app.active_shop_id'::text, true), ''::text))::uuid) AND (EXISTS ( SELECT
   FROM public.entitlement_request_uses u
  WHERE ((u.replay_id = (NULLIF(current_setting('app.entitlement_replay_id'::text, true), ''::text))::uuid) AND (u.xmin = (pg_current_xact_id())::xid))))));


--
-- Name: FUNCTION claim_entitlement_delivery(p_now timestamp with time zone, p_lease uuid); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.claim_entitlement_delivery(p_now timestamp with time zone, p_lease uuid) FROM PUBLIC;


--
-- Name: FUNCTION due_merchant_entitlement_reconciliation(p_now timestamp with time zone); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.due_merchant_entitlement_reconciliation(p_now timestamp with time zone) FROM PUBLIC;


--
-- Name: FUNCTION finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamp with time zone, p_success boolean, p_wait_ack boolean, p_failure text); Type: ACL; Schema: public; Owner: -
--

REVOKE ALL ON FUNCTION public.finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamp with time zone, p_success boolean, p_wait_ack boolean, p_failure text) FROM PUBLIC;


--
-- PostgreSQL database dump complete
--
