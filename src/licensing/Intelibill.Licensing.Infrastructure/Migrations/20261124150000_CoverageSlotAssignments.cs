using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261124150000_CoverageSlotAssignments")]
public sealed partial class CoverageSlotAssignments : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string InboxKinds = "'Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ({InboxKinds},'CoverageSlotConsent'));
            """);
        migrationBuilder.Sql(PaidFreshStarts.ShopLinkGuard.Replace("SELECT f.predecessor_term_id INTO prior", """
            IF nullif(current_setting('app.slot_assignment_id',true),'') IS NOT NULL AND EXISTS
                (SELECT FROM public.coverage_slot_assignments a WHERE a.assignment_id=nullif(current_setting('app.slot_assignment_id',true),'')::uuid
                    AND a.account_id=NEW.account_id AND a.term_id=NEW.term_id AND a.order_id=NEW.order_id AND a.shop_id=NEW.shop_id
                    AND a.affiliation_request_id=NEW.affiliation_request_id AND a.state='Applied' AND a.xmin=pg_current_xact_id()::xid)
            THEN
                SELECT applied_at INTO NEW.coverage_from FROM public.coverage_slot_assignments
                    WHERE assignment_id=nullif(current_setting('app.slot_assignment_id',true),'')::uuid;
                RETURN NEW;
            END IF;
            -- A renewal inherits the earliest instant this shop joined its uninterrupted paid chain.
            SELECT link.coverage_from INTO NEW.coverage_from FROM public.payment_fulfilments successor
                JOIN public.payment_fulfilment_shops link ON link.account_id=successor.account_id
                    AND link.term_id=successor.predecessor_term_id AND link.shop_id=NEW.shop_id
                WHERE successor.account_id=NEW.account_id AND successor.term_id=NEW.term_id;

            SELECT f.predecessor_term_id INTO prior
            """, StringComparison.Ordinal));
    }

    private const string Tables = """
        ALTER TABLE public.payment_fulfilment_shops ADD COLUMN coverage_from timestamptz;
        CREATE TABLE public.coverage_slot_assignments (
            assignment_id uuid PRIMARY KEY, account_id uuid NOT NULL, term_id uuid NOT NULL, order_id uuid NOT NULL,
            shop_id uuid NOT NULL, affiliation_request_id uuid NOT NULL, shop_name text NOT NULL,
            requester_id uuid NOT NULL, requester_revision int NOT NULL, operation_key uuid NOT NULL,
            capacity int NOT NULL CHECK(capacity>0), member_allowance int NOT NULL CHECK(member_allowance>0),
            created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL, coverage_ends_at timestamptz NOT NULL,
            CHECK(created_at<expires_at AND expires_at<=coverage_ends_at),
            state text NOT NULL CHECK(state IN ('AwaitingConsent','Applied','Refused','Expired')),
            reason text, consent_event uuid UNIQUE, applied_at timestamptz,
            CHECK((state='Applied')=(applied_at IS NOT NULL)), CHECK((state='Refused')=(reason IS NOT NULL)),
            CHECK((state IN ('Applied','Refused'))=(consent_event IS NOT NULL)),
            CHECK(applied_at IS NULL OR (created_at<=applied_at AND applied_at<expires_at)),
            UNIQUE(account_id,requester_id,operation_key),
            FOREIGN KEY(account_id,term_id) REFERENCES public.subscription_coverage_terms(account_id,term_id),
            FOREIGN KEY(account_id,order_id) REFERENCES public.payment_fulfilments(account_id,order_id),
            FOREIGN KEY(account_id,shop_id,affiliation_request_id) REFERENCES public.shop_affiliation_requests(account_id,shop_id,request_id)
        );
        CREATE UNIQUE INDEX coverage_slot_pending_shop ON public.coverage_slot_assignments(shop_id) WHERE state='AwaitingConsent';
        CREATE UNIQUE INDEX coverage_slot_applied_shop ON public.coverage_slot_assignments(term_id,shop_id) WHERE state='Applied';
        CREATE TABLE public.coverage_slot_assignment_deliveries (
            assignment_id uuid PRIMARY KEY REFERENCES public.coverage_slot_assignments,
            account_id uuid NOT NULL, shop_id uuid NOT NULL, event_id uuid NOT NULL UNIQUE REFERENCES public.entitlement_snapshots
        );
        ALTER TABLE public.coverage_slot_assignments ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_assignments FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_assignment_deliveries ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_assignment_deliveries FORCE ROW LEVEL SECURITY;
        CREATE TRIGGER retain_slot BEFORE DELETE OR TRUNCATE ON public.coverage_slot_assignments
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_slot_delivery BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_slot_assignment_deliveries
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            LOCK TABLE public.coverage_slot_assignments IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.coverage_slot_assignments NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN IF EXISTS(SELECT FROM public.coverage_slot_assignments) THEN
                RAISE EXCEPTION 'Retained coverage slot assignments prevent downgrade' USING ERRCODE='55000';
            END IF; END $$;
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ({InboxKinds}));
            DROP POLICY slot_link_insert ON public.payment_fulfilment_shops;
            DROP TABLE public.coverage_slot_assignment_deliveries,public.coverage_slot_assignments;
            ALTER TABLE public.payment_fulfilment_shops DROP COLUMN coverage_from;
            DROP FUNCTION public.guard_slot_assignment();
            DROP FUNCTION public.slot_shop_eligible(uuid,uuid,timestamptz,timestamptz);
            """);
        migrationBuilder.Sql(PaidFreshStarts.ShopLinkGuard);
    }
}
