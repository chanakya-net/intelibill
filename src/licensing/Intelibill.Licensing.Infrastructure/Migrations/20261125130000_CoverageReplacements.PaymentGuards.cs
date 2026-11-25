namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageReplacements
{
    private const string CheckoutGuard = """
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
                AND ((s.shop_id=replacement.incoming_shop_id AND s.affiliation_request_id=replacement.incoming_affiliation_id) OR a.term_id IS NOT DISTINCT FROM NEW.term_id OR (NEW.term_id IS NOT NULL AND EXISTS (
                    SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = s.account_id
                        AND p.term_id = NEW.term_id AND p.shop_id = s.shop_id
                        AND p.affiliation_request_id = s.affiliation_request_id)));
            IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid
                OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
                OR (SELECT count(*) FROM public.checkout_order_shops s
                    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id) <> named
                OR (NEW.term_id IS NOT NULL AND reduction_id IS NULL AND (SELECT count(*) FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) + (SELECT count(*)
                    FROM public.payment_fulfilment_shops p WHERE p.account_id = NEW.account_id AND p.term_id = NEW.term_id) <> named)
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
        """;

    private const string LinkGuard = """
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

            -- Contiguous paid renewal inherits the shop's joined start and originating slot assignment;
            -- RestoredAccess after a lapse starts fresh with NULL coverage_from and NULL slot_assignment_id.
            IF prior IS NOT NULL AND NOT coalesce(is_restored, false) THEN
                SELECT link.coverage_from, link.slot_assignment_id, link.replacement_id INTO NEW.coverage_from, NEW.slot_assignment_id, NEW.replacement_id
                FROM public.payment_fulfilment_shops link
                WHERE link.account_id = NEW.account_id AND link.term_id = prior AND link.shop_id = NEW.shop_id;
                IF NOT FOUND THEN
                    SELECT r.incoming_coverage_from, r.replacement_id INTO NEW.coverage_from, NEW.replacement_id
                    FROM public.coverage_replacements r WHERE r.account_id=NEW.account_id AND r.term_id=prior
                        AND r.successor_term_id=NEW.term_id AND r.state='Applied' AND r.xmin=pg_current_xact_id()::xid
                        AND r.incoming_shop_id=NEW.shop_id AND r.incoming_affiliation_id=NEW.affiliation_request_id;
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
                    AND (prior IS NOT NULL OR a.term_id IS NULL))
                OR (prior IS NULL AND EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.shop_id = NEW.shop_id))
                OR (prior IS NOT NULL AND NEW.replacement_id IS NULL AND NOT EXISTS (SELECT FROM public.shop_affiliations a
                        WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id AND a.term_id = prior)
                    AND NOT EXISTS (SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = NEW.account_id
                        AND p.shop_id = NEW.shop_id AND p.term_id = prior AND p.affiliation_request_id = NEW.affiliation_request_id))
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
}
