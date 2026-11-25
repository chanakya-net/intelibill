using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261125120000_CoverageSlotAssignments")]
public sealed partial class CoverageSlotAssignments : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string InboxKinds = "'Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(BoundaryFunctions);
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql(StatusFunction);
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ({InboxKinds},'CoverageSlotConsent'));
            """);
        migrationBuilder.Sql(ShopLinkGuard);
    }

    private const string Tables = """
        CREATE TABLE public.coverage_slot_assignments (
            assignment_id uuid PRIMARY KEY, account_id uuid NOT NULL, term_id uuid NOT NULL, order_id uuid NOT NULL,
            shop_id uuid NOT NULL, affiliation_request_id uuid NOT NULL, shop_name text NOT NULL,
            requester_id uuid NOT NULL, requester_revision int NOT NULL, operation_key uuid NOT NULL,
            capacity int NOT NULL CHECK(capacity>0), member_allowance int NOT NULL CHECK(member_allowance>0),
            cancellation_revision bigint NOT NULL DEFAULT 0 CHECK(cancellation_revision>=0 AND cancellation_revision%2=0),
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
        ALTER TABLE public.payment_fulfilment_shops
            ADD COLUMN coverage_from timestamptz,
            ADD COLUMN slot_assignment_id uuid REFERENCES public.coverage_slot_assignments(assignment_id);
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
            ALTER TABLE public.payment_fulfilment_shops DROP COLUMN slot_assignment_id, DROP COLUMN coverage_from;
            DROP TABLE public.coverage_slot_assignment_deliveries, public.coverage_slot_assignments;
            DROP FUNCTION public.slot_assignment_status(uuid,uuid,timestamptz);
            DROP FUNCTION public.guard_slot_assignment();
            DROP FUNCTION public.slot_shop_eligible(uuid,uuid,timestamptz,timestamptz);
            DROP FUNCTION public.shop_has_conflicting_paid_coverage(uuid,uuid,uuid,timestamptz,timestamptz);
            DROP FUNCTION public.shop_paid_link(uuid,uuid,uuid,timestamptz);
            DROP FUNCTION public.term_effective_access_end(uuid,uuid);
            DROP FUNCTION public.term_effective_paid_through(uuid,uuid);
            """);
        migrationBuilder.Sql(PaidFreshStarts.ShopLinkGuard);
    }
}
