using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageReplacementExceptions
{
    internal static string DeferWhileOutgoingPermissionRemains =>
        CoverageReplacements.DeferWhileOutgoingPermissionRemains + "\n" + $$"""
        UPDATE public.entitlement_outbox o
        SET status='Pending', attempts=attempts-1, last_failure=NULL, lease_token=NULL, lease_expires_at=NULL,
            next_attempt_at=p_now + interval '1 minute'
        WHERE p_failure='Coverage.ExceptionAwaitingOutgoingRelease' AND NOT p_success AND p_wait_ack
            AND o.event_id=p_event AND o.status='Leased' AND o.lease_token=p_lease AND o.lease_expires_at>p_now
            AND {{LeasedHttpOutboxSchema.Unscoped}}
            AND EXISTS (SELECT FROM public.entitlement_messages m WHERE m.event_id=o.event_id AND m.kind='Snapshot'
                AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementExceptionId' IS NOT NULL);
        """;

    private const string StatusFunctions = """
        CREATE FUNCTION public.coverage_exception_owner_current(ac uuid, id uuid)
        RETURNS bool LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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

        CREATE FUNCTION public.coverage_exception_stale(id uuid, at_time timestamptz)
        RETURNS bool LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            r public.coverage_replacement_exceptions%ROWTYPE;
            previous_account text := current_setting('app.billing_account_id', true);
            stale bool;
        BEGIN
            SELECT * INTO r FROM public.coverage_replacement_exceptions WHERE exception_id = id;
            IF NOT FOUND OR r.state NOT IN ('AwaitingConsents', 'ReadyForReview') THEN RETURN false; END IF;
            PERFORM set_config('app.billing_account_id', r.account_id::text, true);
            SELECT r.expires_at <= at_time OR r.paid_cutover_at >= r.normal_renewal_at
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

        CREATE FUNCTION public.coverage_exception_leg_activation(id uuid, leg text, at_time timestamptz)
        RETURNS text LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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
        """;
}
