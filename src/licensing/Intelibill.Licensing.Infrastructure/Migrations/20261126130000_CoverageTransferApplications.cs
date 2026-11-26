using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261126130000_CoverageTransferApplications")]
public sealed partial class CoverageTransferApplications : Migration
{
    private const string OriginalKinds = "'Snapshot','Acknowledgement','Reconciliation','CapacityReserve','CapacityConsume'," +
        "'ReductionManifest','ReductionReserve','ReductionConsume','CoverageExceptionPrepare','CoverageExceptionConsume','CoverageExceptionAbort'";
    private const string Account = "nullif(current_setting('app.billing_account_id',true),'')::uuid";
    private const string Admin = "nullif(current_setting('app.exception_admin_id',true),'')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ({OriginalKinds},'TransferPrepare','TransferConsume','TransferAbort'));
            CREATE TABLE public.coverage_transfer_approvals (
                transfer_id uuid PRIMARY KEY REFERENCES public.coverage_transfer_proposals,
                source_account_id uuid NOT NULL, destination_account_id uuid NOT NULL CHECK (destination_account_id <> source_account_id),
                shop_id uuid NOT NULL, affiliation_id uuid NOT NULL, source_term_id uuid NOT NULL,
                admin_id uuid NOT NULL, session_id uuid NOT NULL, mfa_verified_at timestamptz NOT NULL,
                operation_key uuid NOT NULL, hash text NOT NULL CHECK (length(hash)=64 AND hash !~ '[^a-f0-9]'),
                reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 500),
                review_digest text NOT NULL, consents uuid[] NOT NULL CHECK (cardinality(consents)=3),
                cutover_at timestamptz NOT NULL, paid_through_at timestamptz NOT NULL CHECK (paid_through_at >= cutover_at),
                prepare_event uuid UNIQUE NOT NULL, decided_at timestamptz NOT NULL CHECK (decided_at < cutover_at),
                UNIQUE(source_account_id,operation_key), UNIQUE(transfer_id,source_account_id),
                FOREIGN KEY (source_account_id,source_term_id) REFERENCES public.subscription_coverage_terms(account_id,term_id)
            );
            CREATE TABLE public.coverage_transfer_progress (
                transfer_id uuid NOT NULL, source_account_id uuid NOT NULL, sequence int NOT NULL CHECK (sequence > 0),
                state text NOT NULL CHECK (state IN ('Scheduled','Consuming','PendingActivation','Applied','NeedsReview')),
                event_id uuid NOT NULL, UNIQUE(event_id,state), reason text, recorded_at timestamptz NOT NULL,
                PRIMARY KEY(transfer_id,sequence),
                FOREIGN KEY(transfer_id,source_account_id) REFERENCES public.coverage_transfer_approvals(transfer_id,source_account_id)
            );
            ALTER TABLE public.coverage_transfer_approvals ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.coverage_transfer_approvals FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.coverage_transfer_progress ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.coverage_transfer_progress FORCE ROW LEVEL SECURITY;
            CREATE POLICY scope_read ON public.coverage_transfer_approvals FOR SELECT USING (source_account_id={Account});
            CREATE POLICY scope_read ON public.coverage_transfer_progress FOR SELECT USING (source_account_id={Account});
            CREATE POLICY manager_decision ON public.coverage_transfer_approvals FOR INSERT WITH CHECK (
                source_account_id={Account} AND admin_id={Admin}
                AND session_id=nullif(current_setting('app.exception_session_id',true),'')::uuid
                AND EXISTS (SELECT FROM public.admin_sessions s JOIN public.admin_identities a ON a.id=s.admin_id
                    WHERE s.id=coverage_transfer_approvals.session_id AND a.id=coverage_transfer_approvals.admin_id
                    AND s.revoked_at IS NULL AND a.deactivated_at IS NULL AND a.mfa_enrolled_at IS NOT NULL
                    AND (a.capabilities & 4)=4 AND s.mfa_verified_at=coverage_transfer_approvals.mfa_verified_at)
                AND EXISTS (SELECT FROM public.coverage_transfer_proposals p WHERE p.transfer_id=coverage_transfer_approvals.transfer_id
                    AND p.source_account_id=coverage_transfer_approvals.source_account_id
                    AND p.destination_account_id=coverage_transfer_approvals.destination_account_id
                    AND p.source_term_id=coverage_transfer_approvals.source_term_id
                    AND p.shop_id=coverage_transfer_approvals.shop_id AND p.source_affiliation_id=coverage_transfer_approvals.affiliation_id
                    AND p.source_paid_through_at=coverage_transfer_approvals.paid_through_at
                    AND coalesce(p.cutover_at,p.source_paid_through_at)=coverage_transfer_approvals.cutover_at
                    AND p.state='Open' AND p.expires_at>coverage_transfer_approvals.decided_at));
            CREATE TRIGGER retain_approval BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_transfer_approvals
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            CREATE TRIGGER retain_progress BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_transfer_progress
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            """);
        migrationBuilder.Sql(BindingSql);
        migrationBuilder.Sql(CheckoutShopGuard("current_shop_affiliations"));
        migrationBuilder.Sql(Funding);
        foreach (var table in new[] { "coverage_transfer_approvals","coverage_transfer_progress","coverage_transfer_bindings" })
            migrationBuilder.Sql(LeasedHttpOutboxSchema.WorkerAccess(table));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Claim(true, true, true, recoverTransfers: true));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.coverage_transfer_approvals IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_transfer_approvals NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN IF EXISTS (SELECT FROM public.coverage_transfer_approvals) THEN
            RAISE EXCEPTION 'Transfer decisions prevent downgrade' USING ERRCODE='55000'; END IF; END $$;
        LOCK TABLE public.coverage_transfer_proposals IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_transfer_proposals NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN IF EXISTS(SELECT FROM public.coverage_transfer_proposals WHERE funding_digest IS NOT NULL) THEN
            RAISE EXCEPTION 'Retained transfer terms prevent downgrade' USING ERRCODE='55000'; END IF; END $$;
        ALTER TABLE public.coverage_transfer_proposals FORCE ROW LEVEL SECURITY;
        {UndoFunding}
        {CheckoutShopGuard("shop_affiliations")}
        {UndoBindings}
        {LeasedHttpOutboxSchema.Claim(true,true,true)}
        DROP TABLE public.coverage_transfer_progress, public.coverage_transfer_approvals;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK (kind IN ({OriginalKinds}));
        """);
}
