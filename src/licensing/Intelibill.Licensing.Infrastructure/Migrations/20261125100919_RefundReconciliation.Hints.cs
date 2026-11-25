namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundReconciliation
{
    // Ingress commits the authenticated evidence and wakes an existing read in one transaction.
    // Credential rotation does not change the configuration used for that read.
    private const string RefundHints = """
        ALTER TABLE public.gateway_inbox_events DROP CONSTRAINT gateway_inbox_events_kind_check;
        ALTER TABLE public.gateway_inbox_events ADD CHECK
            (kind IN ('PaymentCaptured','PaymentAuthorized','PaymentFailed','Other','RefundChanged'));
        CREATE FUNCTION public.receive_refund_hint(id uuid, provider text, account text, env text, version text,
            event_id text, payment text, remote text, receipt text, hash bytea, evidence bytea, at_time timestamptz)
        RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
        BEGIN
            IF NOT public.receive_gateway_inbox_event(id,provider,account,env,version,event_id,'RefundChanged',
                NULL,payment,hash,evidence,at_time) THEN RETURN false; END IF;
            UPDATE public.refund_work w SET due_at=least(w.due_at,at_time),wake_pending=(w.token IS NOT NULL),
                candidate_reference=CASE WHEN w.refund_reference IS NULL AND remote IS NOT NULL AND length(remote) BETWEEN 1 AND 120
                    THEN remote ELSE w.candidate_reference END
            FROM public.refund_attempts a JOIN public.refund_sends s USING(reservation_id)
            WHERE w.reservation_id=a.reservation_id AND a.provider_id=provider AND a.provider_account_id=account
                AND a.environment=env AND a.payment_reference=payment
                AND (w.refund_reference IS NULL OR w.refund_reference=remote)
                AND (receipt IS NULL OR receipt=replace(a.reservation_id::text,'-',''))
                AND (w.refund_reference=remote OR receipt=replace(a.reservation_id::text,'-',''));
            RETURN true;
        END $$;
        REVOKE ALL ON FUNCTION public.receive_refund_hint(uuid,text,text,text,text,text,text,text,text,bytea,bytea,timestamptz)
            FROM PUBLIC;
        """;
}
