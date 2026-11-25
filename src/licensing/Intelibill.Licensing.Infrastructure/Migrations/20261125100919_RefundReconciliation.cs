using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261125100919_RefundReconciliation")]
public sealed partial class RefundReconciliation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(WorkTables);
        migrationBuilder.Sql(OwnerOnly("refund_work"));
        migrationBuilder.Sql(OwnerOnly("refund_sends"));
        migrationBuilder.Sql(Claim);
        migrationBuilder.Sql(Evidence);
        migrationBuilder.Sql(OwnerOnly("refund_observations"));
        migrationBuilder.Sql(OwnerOnly("refund_bindings"));
        migrationBuilder.Sql(ApplyDecision);
        migrationBuilder.Sql(Finish);
        migrationBuilder.Sql(Simulator);
        migrationBuilder.Sql(Finance);
        migrationBuilder.Sql(RefundHints);
        migrationBuilder.Sql(CreateAttempt);
        migrationBuilder.Sql(RefundApprovals.DecisionGuard(true));
        migrationBuilder.Sql(RefundApprovals.Boundary(true));
        // The boundary returns at most one row per term, including in shop-wide publication queries.
        migrationBuilder.Sql("ALTER FUNCTION public.refund_boundary(uuid,uuid) ROWS 1");
        migrationBuilder.Sql(ScheduledCapacityReductions.FulfilmentCompleteness(true, true));
    }

    private const string Tables = """
        CREATE TABLE public.refund_applications (
            account_id uuid NOT NULL, decision_id uuid NOT NULL, disposition text NOT NULL
                CHECK (disposition IN ('Applied','NeedsReview')), observed_at timestamptz NOT NULL,
            PRIMARY KEY (account_id,decision_id),
            FOREIGN KEY (account_id,decision_id) REFERENCES public.refund_access_decisions
        );
        -- Every decision written by P06-09 was already effective. Its money outcome remains unproven.
        INSERT INTO public.refund_applications SELECT account_id, decision_id, 'Applied', decided_at
            FROM public.refund_access_decisions;
        ALTER TABLE public.refund_applications ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.refund_applications FORCE ROW LEVEL SECURITY;
        CREATE POLICY application_owner ON public.refund_applications TO CURRENT_USER USING (true);
        CREATE POLICY application_read ON public.refund_applications FOR SELECT
            USING (account_id = nullif(current_setting('app.billing_account_id',true),'')::uuid);
        CREATE TRIGGER refund_application_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_applications
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE VIEW public.applied_refund_decisions WITH (security_invoker=true) AS
            SELECT d.* FROM public.refund_access_decisions d JOIN public.refund_applications x USING (account_id,decision_id)
            WHERE x.disposition = 'Applied';

        CREATE TABLE public.refund_attempts (
            reservation_id uuid PRIMARY KEY REFERENCES public.refund_approvals(reservation_id),
            account_id uuid NOT NULL, approval_id uuid NOT NULL, decision_id uuid NOT NULL, refund_request_id uuid NOT NULL,
            confirmation_id uuid NOT NULL, order_id uuid NOT NULL, attempt_id uuid NOT NULL,
            provider_id text NOT NULL, provider_account_id text NOT NULL, environment text NOT NULL, configuration_version text NOT NULL,
            order_reference text NOT NULL, payment_reference text NOT NULL, affiliations jsonb NOT NULL, predecessor uuid,
            amount_paise bigint NOT NULL CHECK (amount_paise > 0), currency text NOT NULL CHECK (currency = 'INR'),
            payment_amount_paise bigint NOT NULL CHECK (payment_amount_paise >= amount_paise),
            FOREIGN KEY (account_id,decision_id) REFERENCES public.refund_access_decisions,
            UNIQUE (account_id,refund_request_id), UNIQUE(account_id,decision_id)
        );
        CREATE INDEX refund_attempt_payment ON public.refund_attempts(provider_id,provider_account_id,environment,payment_reference);
        ALTER TABLE public.refund_attempts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.refund_attempts FORCE ROW LEVEL SECURITY;
        CREATE POLICY refund_attempt_owner ON public.refund_attempts TO CURRENT_USER USING(true);
        CREATE POLICY refund_attempt_finance ON public.refund_attempts FOR SELECT USING (
            account_id = nullif(current_setting('app.billing_account_id',true),'')::uuid AND public.live_refund_session() IS NOT NULL);
        CREATE TRIGGER refund_attempt_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_attempts
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private const string CreateAttempt = """
        CREATE FUNCTION public.prepare_approved_refund() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
        SET search_path = pg_catalog AS $$
        DECLARE a public.refund_approvals;
        BEGIN
            SELECT * INTO STRICT a FROM public.refund_approvals WHERE account_id = NEW.account_id AND approval_id = NEW.approval_id;
            IF a.attempt_id IS NULL THEN
                INSERT INTO public.refund_applications VALUES (a.account_id, NEW.decision_id,'Applied',NEW.decided_at);
                RETURN NULL;
            END IF;
            INSERT INTO public.refund_attempts SELECT a.reservation_id,a.account_id,a.approval_id,NEW.decision_id,
                a.refund_request_id,a.confirmation_id,a.order_id,a.attempt_id,a.provider_id,a.provider_account_id,
                a.environment,a.configuration_version,s.provider_order_reference,v.provider_payment_reference,
                coalesce((SELECT jsonb_object_agg(shop_id,request_id) FROM public.refund_target_shops(
                    a.account_id, NEW.term_id, NEW.decided_at)), '{}'::jsonb),
                (SELECT decision_id FROM public.applied_refund_decisions WHERE account_id=a.account_id AND term_id=NEW.term_id
                    AND term_revision IS NOT NULL ORDER BY term_revision DESC LIMIT 1),a.amount_paise,a.currency,a.payment_amount_paise
            FROM public.payment_confirmations c JOIN public.verified_payments v USING (account_id,order_id,attempt_id)
            JOIN public.payment_attempt_sessions s USING (account_id,attempt_id)
            WHERE c.confirmation_id=a.confirmation_id AND c.account_id=a.account_id AND c.attempt_id=a.attempt_id
                AND s.provider_order_reference=c.facts->>'providerOrderReference'
                AND v.provider_payment_reference=c.facts->>'providerPaymentReference'
                AND (v.provider_id,v.provider_account_id,v.environment,v.configuration_version,v.amount_paise,v.currency)
                    = (a.provider_id,a.provider_account_id,a.environment,a.configuration_version,a.payment_amount_paise,a.currency);
            IF NOT FOUND THEN RAISE EXCEPTION 'Original verified payment required' USING ERRCODE='23514'; END IF;
            INSERT INTO public.refund_work(reservation_id,due_at) VALUES(a.reservation_id,a.decided_at);
            RETURN NULL;
        END $$;
        REVOKE ALL ON FUNCTION public.prepare_approved_refund() FROM PUBLIC;
        CREATE TRIGGER prepare_approved_refund AFTER INSERT ON public.refund_access_decisions
            FOR EACH ROW EXECUTE FUNCTION public.prepare_approved_refund();
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.refund_attempts, public.refund_applications IN ACCESS EXCLUSIVE MODE;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.gateway_inbox_events WHERE kind='RefundChanged') OR
                    EXISTS (SELECT FROM public.refund_attempts) OR EXISTS (SELECT FROM public.refund_applications) THEN
                    RAISE EXCEPTION 'Retained refund evidence prevents downgrade' USING ERRCODE='55000';
                END IF;
            END $$;
            """);
        migrationBuilder.Sql(RefundApprovals.DecisionGuard(false));
        migrationBuilder.Sql(RefundApprovals.Boundary(false));
        migrationBuilder.Sql("ALTER FUNCTION public.refund_boundary(uuid,uuid) ROWS 1000");
        migrationBuilder.Sql(ScheduledCapacityReductions.FulfilmentCompleteness(true));
        migrationBuilder.Sql("""
            DROP TRIGGER prepare_approved_refund ON public.refund_access_decisions;
            DROP FUNCTION public.prepare_approved_refund();
            DROP FUNCTION public.receive_refund_hint(uuid,text,text,text,text,text,text,text,text,bytea,bytea,timestamptz);
            ALTER TABLE public.gateway_inbox_events DROP CONSTRAINT gateway_inbox_events_kind_check;
            ALTER TABLE public.gateway_inbox_events ADD CHECK (kind IN ('PaymentCaptured','PaymentAuthorized','PaymentFailed','Other'));
            DROP FUNCTION public.refund_outcome(uuid,uuid), public.refund_finance_observation(uuid,uuid);
            DROP POLICY refund_projection ON public.refund_approvals;
            DROP VIEW public.applied_refund_decisions;
            DROP FUNCTION public.claim_refund(timestamptz,uuid), public.start_refund_send(uuid,timestamptz);
            DROP FUNCTION public.apply_confirmed_refund(public.refund_attempts,timestamptz), public.finish_refund(uuid,timestamptz,text,text,text),
                public.recheck_refund(uuid,uuid,timestamptz,text);
            DROP TABLE public.development_refund_controls,public.development_gateway_refunds,public.refund_observations,public.refund_bindings;
            DROP TABLE public.refund_sends, public.refund_work;
            DROP TABLE public.refund_attempts, public.refund_applications;
            """);
    }
}
