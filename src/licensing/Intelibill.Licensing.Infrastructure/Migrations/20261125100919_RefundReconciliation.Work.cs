using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundReconciliation
{
    private const string WorkerScope = LeasedHttpOutboxSchema.Unscoped + """
         AND nullif(current_setting('app.billing_actor_id',true),'') IS NULL
         AND nullif(current_setting('app.manual_admin_id',true),'') IS NULL
         AND nullif(current_setting('app.status_shop_id',true),'') IS NULL
         AND nullif(current_setting('app.payment_attempt_id',true),'') IS NULL
        """;

    private const string WorkTables = """
        CREATE TABLE public.refund_work (
            reservation_id uuid PRIMARY KEY REFERENCES public.refund_attempts, token uuid UNIQUE,
            due_at timestamptz NOT NULL, lease_until timestamptz,
            state text NOT NULL DEFAULT 'Reserved' CHECK (state IN ('Reserved','Unknown','Pending','Confirmed','Failed')),
            checks smallint NOT NULL DEFAULT 0 CHECK(checks BETWEEN 0 AND 12),
            wake_pending boolean NOT NULL DEFAULT false, refund_reference text, last_check timestamptz, reason text
        );
        CREATE INDEX refund_work_due ON public.refund_work(due_at,reservation_id);
        CREATE TABLE public.refund_sends (
            reservation_id uuid PRIMARY KEY REFERENCES public.refund_attempts, started_at timestamptz NOT NULL
        );
        CREATE TRIGGER retain_refund_sends BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_sends
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private static readonly string Claim = $$"""
        CREATE FUNCTION public.claim_refund(at_time timestamptz, claim uuid)
        RETURNS TABLE (reservation_id uuid, account_id uuid, order_id uuid, attempt_id uuid,
            provider_id text, provider_account_id text, environment text, configuration_version text,
            payment_reference text, order_reference text, amount_paise bigint, payment_amount_paise bigint,
            currency text, sent boolean, refund_reference text)
        LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path=pg_catalog AS $$
            WITH due AS (
                SELECT w.reservation_id FROM public.refund_work w WHERE w.due_at <= at_time
                    AND (w.lease_until IS NULL OR w.lease_until <= at_time) AND {{WorkerScope}}
                ORDER BY w.due_at,w.reservation_id LIMIT 1 FOR UPDATE SKIP LOCKED
            ), leased AS (
                UPDATE public.refund_work w SET token=claim, wake_pending=false, lease_until=at_time+interval '60 seconds'
                    FROM due WHERE w.reservation_id=due.reservation_id RETURNING w.reservation_id,w.refund_reference
            )
            SELECT a.reservation_id,a.account_id,a.order_id,a.attempt_id,a.provider_id,a.provider_account_id,
                a.environment,a.configuration_version,a.payment_reference,a.order_reference,a.amount_paise,
                a.payment_amount_paise,a.currency,s.reservation_id IS NOT NULL,l.refund_reference
            FROM leased l JOIN public.refund_attempts a USING(reservation_id) LEFT JOIN public.refund_sends s USING(reservation_id)
        $$;
        REVOKE ALL ON FUNCTION public.claim_refund(timestamptz,uuid) FROM PUBLIC;

        CREATE FUNCTION public.start_refund_send(claim uuid, at_time timestamptz) RETURNS boolean
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
        DECLARE id uuid;
        BEGIN
            SELECT reservation_id INTO id FROM public.refund_work WHERE token=claim AND lease_until>at_time
                AND {{WorkerScope}} FOR UPDATE;
            IF NOT FOUND THEN RETURN false; END IF;
            INSERT INTO public.refund_sends VALUES(id,at_time) ON CONFLICT DO NOTHING;
            IF NOT FOUND THEN RETURN false; END IF;
            UPDATE public.refund_work SET state='Unknown',reason='DispatchStarted' WHERE reservation_id=id;
            RETURN true;
        END $$;
        REVOKE ALL ON FUNCTION public.start_refund_send(uuid,timestamptz) FROM PUBLIC;
        """;

    private static string OwnerOnly(string table) => $"""
        ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
        CREATE POLICY refund_internal ON public.{table} TO CURRENT_USER USING(true);
        """;
}
