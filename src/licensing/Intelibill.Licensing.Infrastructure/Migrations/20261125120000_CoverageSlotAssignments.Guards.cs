namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageSlotAssignments
{
    private const string Actor = "nullif(current_setting('app.billing_actor_id',true),'')::uuid";
    private const string Delegated = """
        EXISTS (SELECT FROM public.merchant_billing_delegation_uses u WHERE u.account_id=coverage_slot_assignments.account_id
            AND u.actor_id=nullif(current_setting('app.billing_actor_id',true),'')::uuid
            AND u.action='billing.slot-assignment.request' AND u.xmin=pg_current_xact_id()::xid)
        """;

    private static readonly string Policies = $"""
        CREATE POLICY account_read ON public.coverage_slot_assignments FOR SELECT USING(account_id={Account});
        CREATE POLICY owner_read ON public.coverage_slot_assignments FOR SELECT USING(
            assignment_id=nullif(current_setting('app.slot_assignment_id',true),'')::uuid
            AND shop_id=nullif(current_setting('app.affiliation_shop_id',true),'')::uuid);
        CREATE POLICY request_insert ON public.coverage_slot_assignments FOR INSERT WITH CHECK(account_id={Account}
            AND requester_id={Actor} AND state='AwaitingConsent' AND {Delegated});
        CREATE POLICY expire_request ON public.coverage_slot_assignments FOR UPDATE USING(account_id={Account} AND {Delegated})
            WITH CHECK(state='Expired');
        CREATE POLICY consent_update ON public.coverage_slot_assignments FOR UPDATE USING(account_id={Account}
            AND assignment_id=nullif(current_setting('app.slot_assignment_id',true),'')::uuid)
            WITH CHECK (EXISTS(SELECT FROM public.entitlement_merchant_inbox i WHERE i.event_id=consent_event
                AND i.account_id=coverage_slot_assignments.account_id AND i.kind='CoverageSlotConsent' AND i.xmin=pg_current_xact_id()::xid
                AND convert_from(i.payload,'UTF8')::jsonb @> jsonb_build_object('assignmentId',assignment_id,'shopId',shop_id,
                    'termId',term_id,'affiliationRequestId',affiliation_request_id)));
        CREATE POLICY account_read ON public.coverage_slot_assignment_deliveries FOR SELECT USING(account_id={Account});
        CREATE POLICY delivery_insert ON public.coverage_slot_assignment_deliveries FOR INSERT WITH CHECK(account_id={Account}
            AND EXISTS(SELECT FROM public.coverage_slot_assignments a JOIN public.entitlement_snapshots s USING(account_id,shop_id)
                WHERE a.assignment_id=coverage_slot_assignment_deliveries.assignment_id AND a.state='Applied'
                AND a.xmin=pg_current_xact_id()::xid AND s.xmin=pg_current_xact_id()::xid
                AND s.event_id=coverage_slot_assignment_deliveries.event_id AND s.shop_id=coverage_slot_assignment_deliveries.shop_id));
        CREATE POLICY slot_link_insert ON public.payment_fulfilment_shops FOR INSERT WITH CHECK(account_id={Account}
            AND EXISTS(SELECT FROM public.coverage_slot_assignments a WHERE a.account_id=payment_fulfilment_shops.account_id
                AND a.order_id=payment_fulfilment_shops.order_id AND a.shop_id=payment_fulfilment_shops.shop_id
                AND a.term_id=payment_fulfilment_shops.term_id AND a.affiliation_request_id=payment_fulfilment_shops.affiliation_request_id
                AND a.state='Applied' AND a.xmin=pg_current_xact_id()::xid));
        """;

    private const string BoundaryFunctions = """
        CREATE FUNCTION public.term_effective_paid_through(scope_account uuid, scope_term uuid)
        RETURNS timestamptz LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT coalesce(
                (SELECT new_paid_through FROM public.refund_boundary(scope_account, scope_term)),
                t.ends_at)
            FROM public.subscription_coverage_terms t
            WHERE t.account_id = scope_account AND t.term_id = scope_term
        $$;

        CREATE FUNCTION public.term_effective_access_end(scope_account uuid, scope_term uuid)
        RETURNS timestamptz LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
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

        -- A shop's paid link is the one it joined or renewed most recently: a slot join counts from its acceptance,
        -- a purchase or renewal from its verified fulfilment. An independently dated term the shop left earlier never
        -- outranks the link that followed it, whatever its original end. A historical read sees only links that
        -- existed at that instant.
        CREATE FUNCTION public.shop_paid_link(ac uuid, shop uuid, request uuid, at_time timestamptz)
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

        CREATE FUNCTION public.shop_has_conflicting_paid_coverage(
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
        """;

    private const string Guards = """
        -- until_time is the candidate package's effective access end (refund-shortened, with grace where it applies):
        -- the slot carries the shop through that instant, so other coverage may begin exactly there and no earlier.
        CREATE FUNCTION public.slot_shop_eligible(ac uuid,shop uuid,at_time timestamptz,until_time timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT EXISTS(SELECT FROM public.shop_affiliations a WHERE a.account_id=ac AND a.shop_id=shop
                AND (a.term_id IS NULL OR public.term_effective_access_end(a.account_id,a.term_id)<=at_time))
              AND NOT public.shop_has_conflicting_paid_coverage(ac,shop,NULL,at_time,until_time)
              AND NOT EXISTS(SELECT FROM public.complimentary_grant_shops g WHERE g.shop_id=shop
                  AND g.starts_at<until_time AND at_time<g.ends_at)
              AND NOT EXISTS(SELECT FROM public.trial_history h WHERE h.shop_id=shop AND NOT EXISTS
                  (SELECT FROM public.payment_trial_conversions c WHERE c.shop_id=h.shop_id AND c.paid_starts_at<=at_time))
              AND NOT EXISTS(SELECT FROM public.active_operational_closure_targets c WHERE c.account_id=ac AND c.shop_id=shop)
        $$;

        CREATE FUNCTION public.guard_slot_assignment() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
        CREATE TRIGGER guard_slot_assignment BEFORE INSERT OR UPDATE ON public.coverage_slot_assignments
            FOR EACH ROW EXECUTE FUNCTION public.guard_slot_assignment();
        """;

    internal const string ShopLinkGuard = """
        CREATE OR REPLACE FUNCTION public.guard_payment_fulfilment_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            prior uuid;
            is_restored boolean;
            win_start timestamptz;
            win_end timestamptz;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            PERFORM pg_advisory_xact_lock(hashtextextended('g' || NEW.shop_id::text, 0));
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
                SELECT link.coverage_from, link.slot_assignment_id INTO NEW.coverage_from, NEW.slot_assignment_id
                FROM public.payment_fulfilment_shops link
                WHERE link.account_id = NEW.account_id AND link.term_id = prior AND link.shop_id = NEW.shop_id;
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
                OR (prior IS NOT NULL AND NOT EXISTS (SELECT FROM public.shop_affiliations a
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
