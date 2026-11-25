namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageReplacementExceptions
{
    private const string RosterFunctions = """
        -- Append-only paid slot intervals for one term. Original fulfilment/affiliation rows stay immutable;
        -- persisted handoffs split each slot in generation order without duplicating paid time.
        CREATE FUNCTION public.term_paid_slot_intervals(ac uuid, scope_term uuid)
        RETURNS TABLE (
            slot_origin_shop_id uuid,
            shop_id uuid,
            affiliation_request_id uuid,
            interval_starts_at timestamptz,
            interval_ends_at timestamptz,
            slot_assignment_id uuid,
            handoff_id uuid,
            handoff_generation bigint,
            is_final_holder boolean
        ) LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
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

        -- Effective paid roster for a term. NULL at_time returns the final/renewal holders; a concrete instant
        -- returns the holder covering that instant (with post-paid-through / grace belonging to the final holder).
        CREATE FUNCTION public.term_effective_roster(ac uuid, scope_term uuid, at_time timestamptz)
        RETURNS TABLE (
            slot_origin_shop_id uuid,
            shop_id uuid,
            affiliation_request_id uuid,
            interval_starts_at timestamptz,
            interval_ends_at timestamptz,
            slot_assignment_id uuid,
            handoff_id uuid,
            handoff_generation bigint,
            is_final_holder boolean
        ) LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
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

        -- Time-aware shop paid coverage lookup across fulfilled terms, imported affiliations and persisted handoffs.
        -- A released outgoing interval is returned with is_released = true and coverage_ends_at = cutover_at so an old
        -- affiliation.term_id or full-term payment_fulfilment_shops row never resurrects the released shop.
        CREATE FUNCTION public.shop_effective_paid_link(ac uuid, shop uuid, request uuid, at_time timestamptz)
        RETURNS TABLE (
            term_id uuid,
            coverage_from timestamptz,
            coverage_ends_at timestamptz,
            slot_assignment_id uuid,
            handoff_id uuid,
            handoff_generation bigint,
            is_released boolean
        ) LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
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

        CREATE OR REPLACE FUNCTION public.shop_paid_link(ac uuid, shop uuid, request uuid, at_time timestamptz)
        RETURNS TABLE (term_id uuid, coverage_from timestamptz, slot_assignment_id uuid)
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT e.term_id, e.coverage_from, e.slot_assignment_id
            FROM public.shop_effective_paid_link(ac, shop, request, at_time) e
        $$;
        """;

    private const string UpdatedGuards = """
        CREATE OR REPLACE FUNCTION public.shop_has_conflicting_paid_coverage(
            ac uuid, shop uuid, exclude_term uuid, win_start timestamptz, win_end timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            WITH candidate_terms AS (
                SELECT DISTINCT p.term_id FROM public.payment_fulfilment_shops p
                WHERE p.account_id = ac AND p.shop_id = shop
                UNION
                SELECT DISTINCT a.term_id FROM public.shop_affiliations a
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

        CREATE OR REPLACE FUNCTION public.slot_shop_eligible(ac uuid, shop uuid, at_time timestamptz, until_time timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id = ac AND a.shop_id = shop
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

        CREATE OR REPLACE FUNCTION public.capacity_slot_available(account uuid, term uuid, original_capacity int)
        RETURNS boolean LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT NOT EXISTS (SELECT FROM public.capacity_increases WHERE account_id = account AND term_id = term AND state = 'Pending')
                AND NOT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id = account AND predecessor_term_id = term)
                AND NOT EXISTS (SELECT FROM public.coverage_replacement_exceptions WHERE account_id = account AND term_id = term
                    AND state IN ('Preparing', 'Consuming', 'Aborting'))
                AND coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = account AND i.term_id = term
                    AND i.state = 'Applied' ORDER BY target_revision DESC LIMIT 1), original_capacity) > (
                    SELECT count(*) FROM public.term_effective_roster(account, term, NULL))
        $$;

        CREATE OR REPLACE FUNCTION public.replacement_term_available(ac uuid, source_term uuid)
        RETURNS bool LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id = ac AND term_id = source_term)
                AND NOT EXISTS (SELECT FROM public.payment_fulfilments WHERE account_id = ac AND predecessor_term_id = source_term)
                AND NOT EXISTS (SELECT FROM public.refund_boundary(ac, source_term))
                AND NOT EXISTS (SELECT FROM public.capacity_increases WHERE account_id = ac AND term_id = source_term AND state = 'Pending')
                AND NOT EXISTS (SELECT FROM public.scheduled_capacity_reductions WHERE account_id = ac AND term_id = source_term
                    AND state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview'))
                AND NOT EXISTS (SELECT FROM public.coverage_replacement_exceptions WHERE account_id = ac AND term_id = source_term
                    AND state IN ('Preparing','Consuming','Aborting'))
        $$;

        -- Prevent unrelated complimentary grants from colliding with an in-flight courtesy bridge reservation.
        CREATE OR REPLACE FUNCTION public.guard_complimentary_grant_shop() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('g' || NEW.shop_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.request_id = NEW.affiliation_request_id
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

        CREATE OR REPLACE FUNCTION public.guard_checkout_order() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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
            FROM public.checkout_order_shops s JOIN public.shop_affiliations a
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

        CREATE OR REPLACE FUNCTION public.guard_payment_fulfilment_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id = NEW.account_id
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
        """;

    private const string RestorePreviousGuards = """
        CREATE OR REPLACE FUNCTION public.shop_paid_link(ac uuid, shop uuid, request uuid, at_time timestamptz)
        RETURNS TABLE (term_id uuid, coverage_from timestamptz, slot_assignment_id uuid)
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT l.term_id, l.coverage_from, l.slot_assignment_id
            FROM public.payment_fulfilment_shops l
            JOIN public.payment_fulfilments lf
                ON lf.account_id = l.account_id AND lf.order_id = l.order_id AND lf.term_id = l.term_id
            JOIN public.subscription_coverage_terms lt ON lt.account_id = l.account_id AND lt.term_id = l.term_id
            WHERE l.account_id = ac AND l.shop_id = shop
              AND (request IS NULL OR l.affiliation_request_id = request)
              AND (at_time IS NULL OR (lf.fulfilled_at <= at_time AND (l.coverage_from IS NULL OR l.coverage_from <= at_time)))
            ORDER BY greatest(lf.fulfilled_at, coalesce(l.coverage_from, lf.fulfilled_at)) DESC, lt.ends_at DESC
            LIMIT 1
        $$;

        CREATE OR REPLACE FUNCTION public.shop_has_conflicting_paid_coverage(
            ac uuid, shop uuid, exclude_term uuid, win_start timestamptz, win_end timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS (
                SELECT FROM public.payment_fulfilment_shops p
                JOIN public.subscription_coverage_terms t USING (account_id, term_id)
                WHERE p.account_id = ac AND p.shop_id = shop
                  AND (exclude_term IS NULL OR p.term_id <> exclude_term)
                  AND greatest(t.starts_at, coalesce(p.coverage_from, t.starts_at)) < win_end
                  AND win_start < public.term_effective_access_end(t.account_id, t.term_id)
            ) OR EXISTS (
                SELECT FROM public.shop_affiliations a
                JOIN public.subscription_coverage_terms t ON t.account_id = a.account_id AND t.term_id = a.term_id
                WHERE a.account_id = ac AND a.shop_id = shop AND a.term_id IS NOT NULL
                  AND (exclude_term IS NULL OR a.term_id <> exclude_term)
                  AND t.starts_at < win_end
                  AND win_start < public.term_effective_access_end(t.account_id, t.term_id)
            )
        $$;

        CREATE OR REPLACE FUNCTION public.slot_shop_eligible(ac uuid, shop uuid, at_time timestamptz, until_time timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS(SELECT FROM public.shop_affiliations a WHERE a.account_id=ac AND a.shop_id=shop
                AND (a.term_id IS NULL OR public.term_effective_access_end(a.account_id,a.term_id)<=at_time))
              AND NOT public.shop_has_conflicting_paid_coverage(ac,shop,NULL,at_time,until_time)
              AND NOT EXISTS(SELECT FROM public.complimentary_grant_shops g WHERE g.shop_id=shop
                  AND g.starts_at<until_time AND at_time<g.ends_at)
              AND NOT EXISTS(SELECT FROM public.trial_history h WHERE h.shop_id=shop AND NOT EXISTS
                  (SELECT FROM public.payment_trial_conversions c WHERE c.shop_id=h.shop_id AND c.paid_starts_at<=at_time))
              AND NOT EXISTS(SELECT FROM public.active_operational_closure_targets c WHERE c.account_id=ac AND c.shop_id=shop)
        $$;

        CREATE OR REPLACE FUNCTION public.capacity_slot_available(account uuid, term uuid, original_capacity int)
        RETURNS boolean LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
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

        CREATE OR REPLACE FUNCTION public.replacement_term_available(ac uuid, source_term uuid)
        RETURNS bool LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS(SELECT FROM public.payment_fulfilments WHERE account_id=ac AND term_id=source_term)
                AND NOT EXISTS(SELECT FROM public.payment_fulfilments WHERE account_id=ac AND predecessor_term_id=source_term)
                AND NOT EXISTS(SELECT FROM public.refund_boundary(ac,source_term))
                AND NOT EXISTS(SELECT FROM public.capacity_increases WHERE account_id=ac AND term_id=source_term AND state='Pending')
                AND NOT EXISTS(SELECT FROM public.scheduled_capacity_reductions WHERE account_id=ac AND term_id=source_term
                    AND state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview'))
        $$;

        CREATE OR REPLACE FUNCTION public.guard_complimentary_grant_shop() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('g' || NEW.shop_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.shop_affiliations a WHERE a.request_id = NEW.affiliation_request_id
                    AND a.shop_id = NEW.shop_id AND a.account_id = NEW.account_id)
                OR EXISTS (SELECT FROM public.complimentary_grant_shops l WHERE l.shop_id = NEW.shop_id
                    AND l.starts_at < NEW.ends_at AND NEW.starts_at < l.ends_at) THEN
                RAISE EXCEPTION 'Complimentary grant shop is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;
}
