using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageTransferApplications
{
    private const string InboxKinds = "'Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'," +
        "'CoverageSlotConsent','CoverageReplacementConsent','CoverageReplacementExceptionConsent','CoverageReplacementExceptionReceipt','CoverageTransferConsent'";
    private const string Shop = "nullif(current_setting('app.status_shop_id',true),'')::uuid";
    // Receipt evidence must have been inserted by the signed inbox in this transaction.
    private const string VerifiedReceipt = """
        FROM public.entitlement_merchant_inbox i
        CROSS JOIN LATERAL (SELECT convert_from(i.payload,'UTF8')::jsonb AS body) r
        WHERE i.kind='TransferReceipt' AND i.xmin=pg_current_xact_id()::xid AND i.account_id=source_account_id
            AND (r.body->>'transferId')::uuid=transfer_id
        """;

    private static string BindingSql => $"""
        ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
            ADD CHECK(kind IN ({InboxKinds},'TransferReceipt'));
        CREATE TABLE public.coverage_transfer_bindings (
            transfer_id uuid PRIMARY KEY REFERENCES public.coverage_transfer_approvals,
            predecessor_id uuid UNIQUE NOT NULL, source_account_id uuid NOT NULL, destination_account_id uuid NOT NULL,
            shop_id uuid NOT NULL, shop_name text NOT NULL, source_term_id uuid NOT NULL, slot_origin_shop_id uuid NOT NULL,
            member_allowance int NOT NULL CHECK(member_allowance>0), owner_id uuid NOT NULL, membership_id uuid NOT NULL,
            authority_revision bigint NOT NULL, cutover_at timestamptz NOT NULL, paid_through_at timestamptz NOT NULL,
            CHECK(cutover_at<=paid_through_at AND source_account_id<>destination_account_id),
            UNIQUE(source_term_id,slot_origin_shop_id)
        );
        ALTER TABLE public.coverage_transfer_bindings ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_bindings FORCE ROW LEVEL SECURITY;
        CREATE POLICY scoped_read ON public.coverage_transfer_bindings FOR SELECT USING (
            {Account} IN (source_account_id,destination_account_id) OR shop_id={Shop});
        CREATE POLICY consumed ON public.coverage_transfer_bindings FOR INSERT WITH CHECK (
            source_account_id={Account} AND EXISTS (SELECT {VerifiedReceipt}
                AND r.body @> jsonb_build_object('phase','TransferConsume','outcome','Consumed','shopId',coverage_transfer_bindings.shop_id,
                    'destinationAccountId',destination_account_id))
            AND EXISTS (SELECT FROM public.coverage_transfer_approvals a WHERE a.transfer_id=coverage_transfer_bindings.transfer_id
                AND a.affiliation_id=coverage_transfer_bindings.predecessor_id AND a.cutover_at=coverage_transfer_bindings.cutover_at
                AND a.paid_through_at=coverage_transfer_bindings.paid_through_at AND a.source_term_id=coverage_transfer_bindings.source_term_id));
        CREATE TRIGGER retain_binding BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_transfer_bindings
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE POLICY receipt_progress ON public.coverage_transfer_progress FOR INSERT WITH CHECK (
            source_account_id={Account} AND (EXISTS (SELECT {VerifiedReceipt} AND i.event_id=coverage_transfer_progress.event_id
                AND (state='Scheduled' AND r.body->>'outcome'='Prepared'
                    OR state IN ('PendingActivation','NeedsReview') AND r.body->>'outcome'='Consumed'
                    OR state='NeedsReview' AND r.body->>'outcome'='Refused'))
                OR EXISTS (SELECT FROM public.entitlement_messages m
                    WHERE m.xmin=pg_current_xact_id()::xid AND m.event_id=coverage_transfer_progress.event_id
                    AND m.account_id=coverage_transfer_progress.source_account_id
                    AND (convert_from(m.payload,'UTF8')::jsonb->>'transferId')::uuid=coverage_transfer_progress.transfer_id
                    AND (m.kind='TransferConsume' AND state='Consuming' OR m.kind='TransferAbort' AND state='NeedsReview'))));
        CREATE VIEW public.current_shop_affiliations WITH (security_invoker=true) AS
            SELECT a.* FROM public.shop_affiliations a WHERE NOT EXISTS (
                SELECT FROM public.coverage_transfer_bindings b WHERE b.predecessor_id=a.request_id)
            UNION ALL SELECT b.transfer_id,b.destination_account_id,b.shop_id,NULL::uuid,b.transfer_id,b.owner_id,b.membership_id,
                b.authority_revision,b.shop_name,''::text,b.cutover_at,b.cutover_at,NULL::timestamptz
                FROM public.coverage_transfer_bindings b
                WHERE (b.destination_account_id={Account} OR b.shop_id={Shop}
                    OR current_user=pg_get_userbyid((SELECT relowner FROM pg_class WHERE oid='public.shop_affiliations'::regclass)))
                AND NOT EXISTS (
                    SELECT FROM public.coverage_transfer_bindings next WHERE next.predecessor_id=b.transfer_id);
        CREATE FUNCTION public.transfer_activation_state(id uuid) RETURNS text LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE previous text:=current_setting('app.billing_account_id',true); destination uuid; outcome text;
        BEGIN
            SELECT destination_account_id INTO destination FROM public.coverage_transfer_bindings WHERE transfer_id=id AND source_account_id={Account};
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
        CREATE FUNCTION public.coverage_transfer_participant_state(id uuid) RETURNS text
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
        CREATE POLICY complete_transfer ON public.coverage_transfer_proposals FOR UPDATE
            USING (source_account_id={Account} AND state='Open')
            WITH CHECK (state='Retired' AND retired_reason='AllocationCommitted' AND EXISTS (
                SELECT FROM public.coverage_transfer_bindings b WHERE b.transfer_id=coverage_transfer_proposals.transfer_id
                    AND b.source_account_id=coverage_transfer_proposals.source_account_id AND b.xmin=pg_current_xact_id()::xid));
        CREATE POLICY transfer_head ON public.entitlement_shop_heads FOR UPDATE
            USING (EXISTS(SELECT FROM public.coverage_transfer_bindings b WHERE b.shop_id=entitlement_shop_heads.shop_id
                AND b.source_account_id=entitlement_shop_heads.account_id AND b.destination_account_id={Account}
                AND b.xmin=pg_current_xact_id()::xid))
            WITH CHECK(account_id={Account});
        CREATE POLICY transfer_head_read ON public.entitlement_shop_heads FOR SELECT
            USING (EXISTS(SELECT FROM public.coverage_transfer_bindings b WHERE b.shop_id=entitlement_shop_heads.shop_id
                AND b.source_account_id=entitlement_shop_heads.account_id AND b.destination_account_id={Account}
                AND b.xmin=pg_current_xact_id()::xid));
        CREATE POLICY transfer_request ON public.shop_affiliation_requests FOR INSERT WITH CHECK (
            account_id={Account} AND EXISTS(SELECT FROM public.coverage_transfer_bindings b WHERE b.transfer_id=request_id
                AND b.shop_id=shop_affiliation_requests.shop_id AND b.destination_account_id=shop_affiliation_requests.account_id
                AND b.xmin=pg_current_xact_id()::xid));
        {CoverageReplacementExceptions.UpdatedGuards.Replace("public.shop_affiliations", "public.current_shop_affiliations", StringComparison.Ordinal)}
        ALTER FUNCTION public.term_paid_slot_intervals(uuid,uuid) RENAME TO pre_transfer_paid_slot_intervals;
        CREATE FUNCTION public.term_paid_slot_intervals(ac uuid,scope_term uuid) RETURNS TABLE(slot_origin_shop_id uuid,
            shop_id uuid,affiliation_request_id uuid,interval_starts_at timestamptz,interval_ends_at timestamptz,
            slot_assignment_id uuid,handoff_id uuid,handoff_generation bigint,is_final_holder boolean)
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT i.slot_origin_shop_id,i.shop_id,i.affiliation_request_id,i.interval_starts_at,least(i.interval_ends_at,b.cutover_at),
                i.slot_assignment_id,i.handoff_id,i.handoff_generation,i.is_final_holder AND b.transfer_id IS NULL
            FROM public.pre_transfer_paid_slot_intervals(ac,scope_term) i LEFT JOIN public.coverage_transfer_bindings b
                ON b.source_account_id=ac AND b.source_term_id=scope_term AND b.predecessor_id=i.affiliation_request_id
        $$;
        ALTER FUNCTION public.capacity_slot_available(uuid,uuid,int) RENAME TO pre_transfer_slot_available;
        CREATE FUNCTION public.capacity_slot_available(account uuid,term uuid,original_capacity int) RETURNS boolean
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT public.pre_transfer_slot_available(account,term,original_capacity) AND
                coalesce((SELECT capacity FROM public.capacity_increases WHERE account_id=account AND term_id=term AND state='Applied'
                    ORDER BY target_revision DESC LIMIT 1), original_capacity) >
                    (SELECT count(*) FROM public.pre_transfer_paid_slot_intervals(account,term) WHERE is_final_holder)
        $$;
        ALTER FUNCTION public.shop_has_conflicting_paid_coverage(uuid,uuid,uuid,timestamptz,timestamptz) RENAME TO pre_transfer_conflict;
        CREATE FUNCTION public.shop_has_conflicting_paid_coverage(ac uuid,shop uuid,exclude_term uuid,win_start timestamptz,win_end timestamptz)
        RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT public.pre_transfer_conflict(ac,shop,exclude_term,win_start,win_end) OR EXISTS(
                SELECT FROM public.coverage_transfer_bindings b WHERE b.destination_account_id=ac AND b.shop_id=shop
                    AND b.cutover_at<b.paid_through_at AND b.cutover_at<win_end AND b.paid_through_at>win_start)
        $$;
        CREATE FUNCTION public.due_transfer_applications(p_now timestamptz)
        RETURNS TABLE(source_account_id uuid,destination_account_id uuid,transfer_id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog AS $$
            SELECT a.source_account_id,a.destination_account_id,a.transfer_id FROM public.coverage_transfer_approvals a
            WHERE a.cutover_at<=p_now AND (SELECT p.state FROM public.coverage_transfer_progress p WHERE p.transfer_id=a.transfer_id
                ORDER BY p.sequence DESC LIMIT 1)='Scheduled' AND {LeasedHttpOutboxSchema.Unscoped}
            ORDER BY a.cutover_at,a.transfer_id LIMIT 20
        $$;
        REVOKE ALL ON FUNCTION public.due_transfer_applications(timestamptz) FROM PUBLIC;
        CREATE OR REPLACE FUNCTION public.due_entitlement_verification(p_now timestamptz) RETURNS TABLE(account_id uuid,shop_id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog AS $$
            SELECT a.account_id,a.shop_id FROM public.current_shop_affiliations a LEFT JOIN public.entitlement_shop_heads h USING(account_id,shop_id)
            WHERE (h.shop_id IS NULL OR h.next_verification_at<=p_now) AND {LeasedHttpOutboxSchema.Unscoped}
            ORDER BY h.next_verification_at NULLS FIRST,a.shop_id LIMIT 20
        $$;
        """;

    // Keep the original insert-before-quote seal and validate the exact affiliation epoch visible to this payer.
    private static string CheckoutShopGuard(string affiliations) => $"""
        CREATE OR REPLACE FUNCTION public.guard_checkout_shop() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id=NEW.account_id AND q.order_id=NEW.order_id)
                OR NOT EXISTS (SELECT FROM public.{affiliations} a WHERE a.account_id=NEW.account_id
                    AND a.shop_id=NEW.shop_id AND a.request_id=NEW.affiliation_request_id AND a.shop_name=NEW.shop_name) THEN
                RAISE EXCEPTION 'Checkout shop must be affiliated and inserted before the quote' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    private static string UndoBindings => $"""
        DROP FUNCTION public.coverage_transfer_participant_state(uuid),public.transfer_activation_state(uuid),public.due_transfer_applications(timestamptz);
        CREATE OR REPLACE FUNCTION public.due_entitlement_verification(p_now timestamptz) RETURNS TABLE(account_id uuid,shop_id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog AS $$
            SELECT a.account_id,a.shop_id FROM public.shop_affiliations a LEFT JOIN public.entitlement_shop_heads h USING(account_id,shop_id)
            WHERE (h.shop_id IS NULL OR h.next_verification_at<=p_now) AND {LeasedHttpOutboxSchema.Unscoped}
            ORDER BY h.next_verification_at NULLS FIRST,a.shop_id LIMIT 20
        $$;
        DROP FUNCTION public.term_paid_slot_intervals(uuid,uuid),public.capacity_slot_available(uuid,uuid,int),
            public.shop_has_conflicting_paid_coverage(uuid,uuid,uuid,timestamptz,timestamptz);
        ALTER FUNCTION public.pre_transfer_paid_slot_intervals(uuid,uuid) RENAME TO term_paid_slot_intervals;
        ALTER FUNCTION public.pre_transfer_slot_available(uuid,uuid,int) RENAME TO capacity_slot_available;
        ALTER FUNCTION public.pre_transfer_conflict(uuid,uuid,uuid,timestamptz,timestamptz) RENAME TO shop_has_conflicting_paid_coverage;
        {CoverageReplacementExceptions.UpdatedGuards}
        DROP POLICY complete_transfer ON public.coverage_transfer_proposals;
        DROP POLICY transfer_head ON public.entitlement_shop_heads;
        DROP POLICY transfer_head_read ON public.entitlement_shop_heads;
        DROP POLICY transfer_request ON public.shop_affiliation_requests;
        DROP VIEW public.current_shop_affiliations;
        DROP TABLE public.coverage_transfer_bindings;
        ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check, ADD CHECK(kind IN ({InboxKinds}));
        """;
}
