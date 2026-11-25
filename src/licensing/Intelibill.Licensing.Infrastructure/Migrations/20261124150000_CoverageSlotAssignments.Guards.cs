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

    private const string Guards = """
        CREATE FUNCTION public.slot_shop_eligible(ac uuid,shop uuid,at_time timestamptz,until_time timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT EXISTS(SELECT FROM public.shop_affiliations a WHERE a.account_id=ac AND a.shop_id=shop
                AND (a.term_id IS NULL OR a.coverage_ends_at<=at_time))
              AND NOT EXISTS(SELECT FROM public.payment_fulfilment_shops p JOIN public.subscription_coverage_terms t
                  USING(account_id,term_id) WHERE p.shop_id=shop AND t.ends_at>at_time AND t.starts_at<until_time)
              AND NOT EXISTS(SELECT FROM public.complimentary_grant_shops g WHERE g.shop_id=shop
                  AND g.starts_at<until_time AND at_time<g.ends_at)
              AND NOT EXISTS(SELECT FROM public.trial_history h WHERE h.shop_id=shop AND NOT EXISTS
                  (SELECT FROM public.payment_trial_conversions c WHERE c.shop_id=h.shop_id AND c.paid_starts_at<=at_time))
              AND NOT EXISTS(SELECT FROM public.active_operational_closure_targets c WHERE c.account_id=ac AND c.shop_id=shop)
        $$;

        CREATE FUNCTION public.guard_slot_assignment() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE at_time timestamptz;
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
            IF NOT public.slot_shop_eligible(NEW.account_id,NEW.shop_id,at_time,NEW.coverage_ends_at)
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
                    AND t.starts_at<=at_time AND NEW.coverage_ends_at=t.ends_at AND at_time<t.ends_at
                    AND NEW.capacity=coalesce(adj.capacity,t.capacity) AND NEW.member_allowance=coalesce(adj.members,s.member_allowance)
                    AND public.capacity_slot_available(t.account_id,t.term_id,t.capacity)) THEN
                RAISE EXCEPTION 'Paid slot unavailable' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_slot_assignment BEFORE INSERT OR UPDATE ON public.coverage_slot_assignments
            FOR EACH ROW EXECUTE FUNCTION public.guard_slot_assignment();
        """;
}
