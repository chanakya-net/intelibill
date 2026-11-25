namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageSlotAssignments
{
    // Narrow operational projection shared by billing-account and shop-owner reads. Runs under SECURITY INVOKER;
    // for a verified owner read, scopes app.billing_account_id strictly for the duration of the operational query
    // and restores it before returning. Never exposes commercial data, sibling shops or internal restriction reasons.
    private static readonly string StatusFunction = $$"""
        CREATE FUNCTION public.slot_assignment_status(scope_account uuid, scope_assignment uuid, at_time timestamptz)
        RETURNS TABLE (
            projected_state text,
            activation text,
            merchant_receipt text,
            effective_paid_through timestamptz,
            effective_access_ends_at timestamptz
        )
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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
            has_usable_applied boolean;
            has_any_applied boolean;
            receipt_out text;
            act_out text;
        BEGIN
            SELECT * INTO a FROM public.coverage_slot_assignments r
            WHERE r.account_id = scope_account AND r.assignment_id = scope_assignment;
            IF NOT FOUND THEN RETURN; END IF;
            IF NOT (scope_account = {{Account}}
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

                WITH RECURSIVE chain(term_id, depth) AS (
                    SELECT a.term_id, 0
                    UNION ALL
                    SELECT f.term_id, c.depth + 1
                    FROM chain c
                    JOIN public.payment_fulfilments f ON f.account_id = a.account_id AND f.predecessor_term_id = c.term_id
                    JOIN public.payment_fulfilment_shops ps ON ps.account_id = f.account_id AND ps.term_id = f.term_id AND ps.shop_id = a.shop_id
                    WHERE NOT EXISTS (SELECT FROM public.paid_fresh_starts pf
                        WHERE pf.account_id = f.account_id AND pf.order_id = f.order_id AND pf.kind = 'RestoredAccess')
                      AND c.depth < 120
                )
                SELECT term_id INTO tail_term FROM chain ORDER BY depth DESC LIMIT 1;

                tail_paid := public.term_effective_paid_through(a.account_id, tail_term);
                tail_access := public.term_effective_access_end(a.account_id, tail_term);

                SELECT s.shop_revision, d.event_id INTO initial_rev, initial_event
                FROM public.coverage_slot_assignment_deliveries d
                JOIN public.entitlement_snapshots s ON s.event_id = d.event_id
                WHERE d.assignment_id = a.assignment_id;

                WITH lineage AS (
                    SELECT s.event_id, s.shop_revision, o.status AS outbox_status,
                           convert_from(m.payload, 'UTF8')::jsonb AS snap,
                           CASE WHEN ack.payload IS NOT NULL THEN convert_from(ack.payload, 'UTF8')::jsonb END AS receipt
                    FROM public.entitlement_snapshots s
                    JOIN public.entitlement_messages m USING (event_id, account_id, shop_id)
                    JOIN public.entitlement_outbox o ON o.event_id = s.event_id
                    LEFT JOIN public.entitlement_merchant_inbox ack
                        ON ack.event_id = s.event_id AND ack.account_id = s.account_id AND ack.kind = 'Acknowledgement'
                    WHERE s.account_id = a.account_id AND s.shop_id = a.shop_id
                      AND s.affiliation_request_id = a.affiliation_request_id
                      AND initial_rev IS NOT NULL AND s.shop_revision >= initial_rev
                      AND (s.event_id = initial_event
                           OR convert_from(m.payload, 'UTF8')::jsonb->'shop'->>'slotAssignmentId' = a.assignment_id::text)
                )
                SELECT
                    (SELECT outbox_status FROM lineage ORDER BY shop_revision DESC LIMIT 1),
                    (SELECT receipt->>'outcome' FROM lineage ORDER BY shop_revision DESC LIMIT 1),
                    EXISTS (SELECT FROM lineage l WHERE l.receipt->>'outcome' = 'Applied'
                        AND l.receipt->>'disposition' = 'Applied'
                        AND (l.receipt->>'appliedShopRevision')::bigint = l.shop_revision
                        AND l.snap->'shop'->>'state' IN ('Active', 'Grace')
                        AND (l.snap->'shop'->'suspension' IS NULL
                             OR (l.snap->'shop'->'suspension'->>'effectiveAt')::timestamptz > greatest(
                                 (l.snap->'shop'->>'upstreamVerifiedAt')::timestamptz,
                                 (l.snap->'account'->>'upstreamVerifiedAt')::timestamptz))
                        AND (l.snap->'account'->>'restriction' = 'Unrestricted'
                             OR (l.snap->'account'->'suspension' IS NOT NULL
                                 AND (l.snap->'account'->'suspension'->>'effectiveAt')::timestamptz > greatest(
                                     (l.snap->'shop'->>'upstreamVerifiedAt')::timestamptz,
                                     (l.snap->'account'->>'upstreamVerifiedAt')::timestamptz)))),
                    EXISTS (SELECT FROM lineage l WHERE l.receipt->>'outcome' = 'Applied')
                INTO latest_status, latest_ack_outcome, has_usable_applied, has_any_applied;

                IF latest_status = 'NeedsReview' OR latest_ack_outcome = 'NeedsReview' THEN
                    receipt_out := 'NeedsReview';
                ELSIF has_usable_applied OR has_any_applied THEN
                    receipt_out := 'Applied';
                ELSIF latest_status = 'DeadLetter' THEN
                    receipt_out := 'DeadLetter';
                ELSE
                    receipt_out := 'Pending';
                END IF;

                IF EXISTS (SELECT FROM public.active_operational_closure_targets c
                    WHERE c.account_id = a.account_id AND c.shop_id = a.shop_id) THEN
                    act_out := 'Closed';
                ELSIF EXISTS (SELECT FROM public.active_operational_restrictions r
                    WHERE r.account_id = a.account_id AND (r.shop_id IS NULL OR r.shop_id = a.shop_id)
                      AND r.effective_at <= at_time) THEN
                    act_out := 'Suspended';
                ELSIF at_time >= tail_access THEN
                    act_out := 'TermEnded';
                ELSIF receipt_out IN ('NeedsReview', 'DeadLetter') THEN
                    act_out := 'NeedsReview';
                ELSIF NOT has_usable_applied THEN
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
        """;
}
