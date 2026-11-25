using Intelibill.Shared.Web.Delivery;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261125130000_CoverageReplacements")]
public sealed partial class CoverageReplacements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public.coverage_replacements (
                replacement_id uuid PRIMARY KEY, account_id uuid NOT NULL, term_id uuid NOT NULL,
                outgoing_shop_id uuid NOT NULL, outgoing_shop_name text NOT NULL, outgoing_affiliation_id uuid NOT NULL,
                incoming_shop_id uuid NOT NULL, incoming_shop_name text NOT NULL, incoming_affiliation_id uuid NOT NULL,
                capacity int NOT NULL CHECK(capacity>0), member_allowance int NOT NULL CHECK(member_allowance>0),
                effective_at timestamptz NOT NULL, permit_deadline timestamptz, created_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL CHECK(created_at<expires_at AND effective_at<=expires_at),
                requester_id uuid NOT NULL, requester_revision int NOT NULL, operation_key uuid NOT NULL,
                request_hash text NOT NULL, source_hash text NOT NULL, state text NOT NULL
                    CHECK(state IN ('AwaitingConsent','Scheduled','Applied','Blocked')), reason text,
                consent_event uuid, revoke_event uuid, successor_term_id uuid, incoming_coverage_from timestamptz,
                CHECK(incoming_shop_id<>outgoing_shop_id),
                CHECK((state='Applied')=(successor_term_id IS NOT NULL)),
                CHECK((successor_term_id IS NULL)=(incoming_coverage_from IS NULL)),
                CHECK(incoming_coverage_from>=effective_at),
                UNIQUE(account_id,requester_id,operation_key),
                FOREIGN KEY(account_id,term_id) REFERENCES public.subscription_coverage_terms(account_id,term_id),
                FOREIGN KEY(account_id,successor_term_id) REFERENCES public.subscription_coverage_terms(account_id,term_id),
                FOREIGN KEY(account_id,outgoing_shop_id,outgoing_affiliation_id)
                    REFERENCES public.shop_affiliation_requests(account_id,shop_id,request_id),
                FOREIGN KEY(account_id,incoming_shop_id,incoming_affiliation_id)
                    REFERENCES public.shop_affiliation_requests(account_id,shop_id,request_id)
            );
            CREATE UNIQUE INDEX replacement_open_term ON public.coverage_replacements(term_id) WHERE state IN ('AwaitingConsent','Scheduled');
            CREATE UNIQUE INDEX replacement_open_shop ON public.coverage_replacements(incoming_shop_id) WHERE state IN ('AwaitingConsent','Scheduled');
            ALTER TABLE public.coverage_replacements ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.coverage_replacements FORCE ROW LEVEL SECURITY;
            CREATE POLICY account_read ON public.coverage_replacements FOR SELECT
                USING(account_id=nullif(current_setting('app.billing_account_id',true),'')::uuid);
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ('Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent','CoverageReplacementConsent'));
            CREATE POLICY owner_read ON public.coverage_replacements FOR SELECT USING(
                incoming_shop_id=nullif(current_setting('app.affiliation_shop_id',true),'')::uuid
                AND replacement_id=nullif(current_setting('app.replacement_id',true),'')::uuid);
            CREATE POLICY consent_update ON public.coverage_replacements FOR UPDATE
                USING(account_id=nullif(current_setting('app.billing_account_id',true),'')::uuid)
                WITH CHECK(EXISTS(SELECT FROM public.entitlement_merchant_inbox i WHERE i.kind='CoverageReplacementConsent'
                    AND i.account_id=coverage_replacements.account_id AND i.event_id IN (consent_event,revoke_event)
                    AND i.xmin=pg_current_xact_id()::xid AND convert_from(i.payload,'UTF8')::jsonb @>
                        jsonb_build_object('replacementId',replacement_id,'shopId',incoming_shop_id,'termId',term_id,
                            'affiliationRequestId',incoming_affiliation_id)));
            CREATE POLICY owner_schedule ON public.coverage_replacements FOR INSERT WITH CHECK(
                account_id=nullif(current_setting('app.billing_account_id',true),'')::uuid
                AND requester_id=nullif(current_setting('app.billing_actor_id',true),'')::uuid AND state='AwaitingConsent'
                AND consent_event IS NULL AND revoke_event IS NULL AND reason IS NULL
                AND EXISTS(SELECT FROM public.merchant_billing_delegation_uses u WHERE u.account_id=coverage_replacements.account_id
                    AND u.actor_id=coverage_replacements.requester_id AND u.action='billing.coverage-replacement.schedule'
                    AND u.xmin=pg_current_xact_id()::xid));
            CREATE TRIGGER retain_replacement BEFORE DELETE OR TRUNCATE ON public.coverage_replacements
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            CREATE FUNCTION public.replacement_permit_end(ac uuid, shop uuid, affiliation uuid, at_time timestamptz)
            RETURNS timestamptz LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
                SELECT max(valid_until) FROM public.offline_device_observations
                WHERE account_id=ac AND shop_id=shop AND affiliation_request_id=affiliation AND valid_until>at_time
            $$;
            CREATE FUNCTION public.replacement_term_available(ac uuid, source_term uuid)
            RETURNS bool LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
                SELECT EXISTS(SELECT FROM public.payment_fulfilments WHERE account_id=ac AND term_id=source_term)
                    AND NOT EXISTS(SELECT FROM public.payment_fulfilments WHERE account_id=ac AND predecessor_term_id=source_term)
                    AND NOT EXISTS(SELECT FROM public.refund_boundary(ac,source_term))
                    AND NOT EXISTS(SELECT FROM public.capacity_increases WHERE account_id=ac AND term_id=source_term AND state='Pending')
                    AND NOT EXISTS(SELECT FROM public.scheduled_capacity_reductions WHERE account_id=ac AND term_id=source_term
                        AND state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview'))
            $$;
            ALTER TABLE public.payment_fulfilment_shops ADD COLUMN replacement_id uuid REFERENCES public.coverage_replacements;
            CREATE FUNCTION public.replacement_owner_current(ac uuid, id uuid)
            RETURNS bool LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
            CREATE FUNCTION public.replacement_stale(id uuid, at_time timestamptz)
            RETURNS bool LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
            CREATE POLICY retire_obsolete ON public.coverage_replacements FOR UPDATE
                USING(account_id=nullif(current_setting('app.billing_account_id',true),'')::uuid AND state IN ('AwaitingConsent','Scheduled'))
                WITH CHECK(state='Blocked' AND reason='SourceChanged' AND EXISTS(
                    SELECT FROM public.merchant_billing_delegation_uses u WHERE u.account_id=coverage_replacements.account_id
                        AND u.actor_id=nullif(current_setting('app.billing_actor_id',true),'')::uuid
                        AND u.action='billing.coverage-replacement.schedule' AND u.xmin=pg_current_xact_id()::xid)
                    AND public.replacement_stale(replacement_id,nullif(current_setting('app.replacement_evaluated_at',true),'')::timestamptz));
            CREATE POLICY apply_renewal ON public.coverage_replacements FOR UPDATE
                USING(account_id=nullif(current_setting('app.billing_account_id',true),'')::uuid AND state='Scheduled')
                WITH CHECK (state='Applied' AND EXISTS(SELECT FROM public.payment_fulfilments f JOIN public.checkout_orders q
                    USING(account_id,order_id) WHERE f.account_id=coverage_replacements.account_id
                    AND f.term_id=coverage_replacements.successor_term_id AND f.predecessor_term_id=coverage_replacements.term_id
                    AND f.xmin=pg_current_xact_id()::xid AND (q.snapshot->'replacement'->>'replacementId')::uuid=replacement_id));
            CREATE FUNCTION public.guard_coverage_replacement() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
            CREATE TRIGGER guard_replacement BEFORE UPDATE ON public.coverage_replacements FOR EACH ROW
                EXECUTE FUNCTION public.guard_coverage_replacement();
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Finish(DeferWhileOutgoingPermissionRemains));
        migrationBuilder.Sql(ActivationStatus);
        migrationBuilder.Sql(CheckoutGuard);
        migrationBuilder.Sql(LinkGuard);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.coverage_replacements IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.coverage_replacements NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN IF EXISTS(SELECT FROM public.coverage_replacements) THEN
                RAISE EXCEPTION 'Retained coverage replacements prevent downgrade' USING ERRCODE='55000';
            END IF; END $$;
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ('Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent'));
            ALTER TABLE public.payment_fulfilment_shops DROP COLUMN replacement_id;
            DROP POLICY retire_obsolete ON public.coverage_replacements;
            DROP FUNCTION public.replacement_stale(uuid,timestamptz);
            DROP FUNCTION public.replacement_owner_current(uuid,uuid);
            DROP FUNCTION public.replacement_activation(uuid,timestamptz);
            DROP TABLE public.coverage_replacements;
            DROP FUNCTION public.guard_coverage_replacement();
            DROP FUNCTION public.replacement_permit_end(uuid,uuid,uuid,timestamptz);
            DROP FUNCTION public.replacement_term_available(uuid,uuid);
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Finish());
        migrationBuilder.Sql(ScheduledCapacityReductions.CheckoutOrderGuard);
        migrationBuilder.Sql(CoverageSlotAssignments.ShopLinkGuard);
    }
}
