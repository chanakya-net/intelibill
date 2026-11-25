namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundReconciliation
{
    private const string Simulator = """
        CREATE TABLE public.development_gateway_refunds (
            reference text PRIMARY KEY,receipt text NOT NULL UNIQUE,payment_reference text NOT NULL,
            amount_paise bigint NOT NULL CHECK(amount_paise>0), currency text NOT NULL,
            provider_account_id text NOT NULL,environment text NOT NULL,configuration_version text NOT NULL
        );
        ALTER TABLE public.development_gateway_refunds ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.development_gateway_refunds FORCE ROW LEVEL SECURITY;
        CREATE POLICY simulator_scope ON public.development_gateway_refunds USING (
            current_setting('app.development_gateway',true)='on'
            AND provider_account_id=current_setting('app.gateway_account',true)
            AND environment=current_setting('app.gateway_environment',true)
            AND configuration_version=current_setting('app.gateway_configuration',true));
        CREATE TRIGGER retain_simulated_refund BEFORE UPDATE OR DELETE OR TRUNCATE ON public.development_gateway_refunds
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TABLE public.development_refund_controls (
            receipt text PRIMARY KEY, provider_account_id text NOT NULL, environment text NOT NULL, configuration_version text NOT NULL,
            outcome text NOT NULL CHECK(outcome IN ('Timeout','Delayed','Failed','Pending','Processed','Mismatch')),
            available_at timestamptz
        );
        ALTER TABLE public.development_refund_controls ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.development_refund_controls FORCE ROW LEVEL SECURITY;
        CREATE POLICY control_owner ON public.development_refund_controls TO CURRENT_USER USING(true);
        CREATE POLICY control_scope ON public.development_refund_controls FOR SELECT USING (
            current_setting('app.development_gateway',true)='on' AND provider_account_id=current_setting('app.gateway_account',true)
            AND environment=current_setting('app.gateway_environment',true) AND configuration_version=current_setting('app.gateway_configuration',true));
        """;

    private const string Finance = """
        CREATE POLICY refund_projection ON public.refund_approvals FOR SELECT TO CURRENT_USER USING(true);
        CREATE FUNCTION public.refund_outcome(acct uuid,request uuid) RETURNS TABLE(status text,approved_amount_paise bigint,
            provider_state text,access_disposition text) LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog AS $$
            SELECT CASE WHEN w.state IN ('Confirmed','Failed','Unknown') THEN w.state ELSE 'Pending' END,a.amount_paise,
                coalesce(w.state, CASE WHEN a.attempt_id IS NULL THEN 'Manual' ELSE 'Historical' END),
                coalesce(x.disposition,'AwaitingConfirmation')
            FROM public.refund_approvals a JOIN public.refund_access_decisions d USING(account_id,approval_id)
            LEFT JOIN public.refund_work w USING(reservation_id) LEFT JOIN public.refund_applications x USING(account_id,decision_id)
            WHERE a.account_id=acct AND a.refund_request_id=request AND acct=nullif(current_setting('app.billing_account_id',true),'')::uuid
        $$;
        REVOKE ALL ON FUNCTION public.refund_outcome(uuid,uuid) FROM PUBLIC;
        CREATE FUNCTION public.refund_finance_observation(acct uuid,request uuid)
        RETURNS TABLE(refund_reference text,last_checked_at timestamptz,observation_reason text)
        LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path=pg_catalog AS $$
            SELECT w.refund_reference,w.last_check,w.reason FROM public.refund_attempts a
            JOIN public.refund_work w USING(reservation_id)
            WHERE a.account_id=acct AND a.refund_request_id=request
                AND acct=nullif(current_setting('app.billing_account_id',true),'')::uuid
                AND public.live_refund_session() IS NOT NULL
        $$;
        REVOKE ALL ON FUNCTION public.refund_finance_observation(uuid,uuid) FROM PUBLIC;
        CREATE FUNCTION public.recheck_refund(acct uuid,request uuid,at_time timestamptz,simulated text) RETURNS boolean
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
        DECLARE a public.refund_attempts;
        BEGIN
            SELECT * INTO a FROM public.refund_attempts WHERE account_id=acct AND refund_request_id=request
                AND acct=nullif(current_setting('app.billing_account_id',true),'')::uuid AND public.live_refund_session() IS NOT NULL;
            IF NOT FOUND THEN RETURN false; END IF;
            IF simulated IS NOT NULL THEN
                IF a.provider_id<>'development-simulator' THEN RETURN false; END IF;
                INSERT INTO public.development_refund_controls VALUES(replace(a.reservation_id::text,'-',''),a.provider_account_id,
                    a.environment,a.configuration_version,simulated,at_time+interval '30 seconds') ON CONFLICT(receipt)
                    DO UPDATE SET outcome=excluded.outcome,available_at=excluded.available_at;
            END IF;
            UPDATE public.refund_work w SET due_at=least(due_at,at_time), wake_pending=(token IS NOT NULL) WHERE w.reservation_id=a.reservation_id
                AND EXISTS(SELECT FROM public.refund_sends WHERE reservation_id=a.reservation_id);
            RETURN FOUND OR simulated IS NOT NULL;
        END $$;
        REVOKE ALL ON FUNCTION public.recheck_refund(uuid,uuid,timestamptz,text) FROM PUBLIC;
        """;
}
