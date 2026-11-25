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
            confirmed_usable boolean;
            change_outstanding boolean;
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
        """;
}
