namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundReconciliation
{
    private const string Evidence = """
        CREATE TABLE public.refund_observations (
            observation_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            reservation_id uuid NOT NULL REFERENCES public.refund_attempts,
            observed_at timestamptz NOT NULL, outcome text NOT NULL, reason text NOT NULL, refund_reference text
        );
        CREATE TABLE public.refund_bindings (
            reservation_id uuid PRIMARY KEY REFERENCES public.refund_sends,
            provider_id text NOT NULL,provider_account_id text NOT NULL,environment text NOT NULL,reference text NOT NULL,
            UNIQUE(provider_id,provider_account_id,environment,reference)
        );
        CREATE TRIGGER retain_refund_observations BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_observations
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_refund_bindings BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_bindings
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private const string ApplyDecision = """
        CREATE FUNCTION public.apply_confirmed_refund(a public.refund_attempts, at_time timestamptz)
        RETURNS SETOF uuid LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d public.refund_access_decisions; applied boolean;
        BEGIN
            SELECT * INTO STRICT d FROM public.refund_access_decisions
            WHERE account_id=a.account_id AND decision_id=a.decision_id;
            applied := d.treatment='Preserve' OR EXISTS (
                SELECT FROM public.subscription_coverage_terms t
                LEFT JOIN LATERAL (SELECT * FROM public.applied_refund_decisions p WHERE p.account_id=t.account_id
                    AND p.term_id=t.term_id AND p.term_revision IS NOT NULL ORDER BY p.term_revision DESC LIMIT 1) p ON true
                LEFT JOIN LATERAL (SELECT kind FROM public.scheduled_subscription_changes c WHERE c.account_id=t.account_id
                    AND c.term_id=t.term_id ORDER BY revision DESC LIMIT 1) c ON true
                WHERE t.account_id=a.account_id AND t.term_id=d.term_id AND p.decision_id IS NOT DISTINCT FROM a.predecessor
                    AND d.previous_paid_through=coalesce(p.new_paid_through,t.ends_at)
                    AND d.previous_access_ends_at=coalesce(p.new_access_ends_at,CASE WHEN c.kind='ScheduleCancellation'
                        THEN t.ends_at ELSE t.ends_at+interval '168 hours' END)
                    AND NOT public.refund_term_committed(t.account_id,t.term_id)
                    AND a.capacity_revision = coalesce((SELECT max(i.target_revision) FROM public.capacity_increases i
                        WHERE i.account_id=t.account_id AND i.term_id=t.term_id AND i.state='Applied'), 0)
                    AND a.affiliations=(SELECT coalesce(jsonb_object_agg(shop_id,request_id),'{}'::jsonb)
                        FROM public.refund_target_shops(t.account_id,t.term_id,at_time)));
            INSERT INTO public.refund_applications VALUES(a.account_id,a.decision_id,
                CASE WHEN applied THEN 'Applied' ELSE 'NeedsReview' END,at_time) ON CONFLICT DO NOTHING;
            IF FOUND AND applied AND d.treatment<>'Preserve' THEN
                RETURN QUERY SELECT key::uuid FROM jsonb_object_keys(a.affiliations) key ORDER BY key;
            END IF;
        END $$;
        REVOKE ALL ON FUNCTION public.apply_confirmed_refund(public.refund_attempts,timestamptz) FROM PUBLIC;
        """;

    private static readonly string Finish = $$"""
        CREATE FUNCTION public.finish_refund(claim uuid,at_time timestamptz,outcome text,detail text,remote text)
        RETURNS SETOF uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
        DECLARE a public.refund_attempts; previous text; bound text; sent boolean; read_deferred boolean;
        BEGIN
            SELECT r.* INTO a FROM public.refund_work w JOIN public.refund_attempts r USING(reservation_id)
                WHERE w.token=claim AND {{WorkerScope}};
            IF NOT FOUND THEN RETURN; END IF;
            PERFORM pg_advisory_xact_lock(hashtextextended('b'||a.account_id::text,0));
            PERFORM public.lock_refund_receipt(a.confirmation_id);
            SELECT w.state INTO previous FROM public.refund_work w WHERE w.token=claim AND w.lease_until>at_time FOR UPDATE;
            IF NOT FOUND THEN RETURN; END IF;
            PERFORM set_config('app.billing_account_id',a.account_id::text,true);
            SELECT EXISTS(SELECT FROM public.refund_sends WHERE reservation_id=a.reservation_id) INTO sent;
            IF outcome NOT IN ('Reserved','Unknown','Pending','Failed','Confirmed') OR
                (outcome='Reserved')=sent OR length(detail)>80 OR length(remote)>120 OR
                (outcome IN ('Pending','Failed','Confirmed') AND (remote IS NULL OR detail<>'OriginalProviderRead')) THEN
                RAISE EXCEPTION 'Invalid refund observation' USING ERRCODE='23514';
            END IF;
            -- Keep unverified POST references in observations. Only an independent read binds the identity.
            IF remote IS NOT NULL AND detail='OriginalProviderRead' AND outcome IN ('Pending','Confirmed','Failed') THEN
                INSERT INTO public.refund_bindings VALUES(a.reservation_id,a.provider_id,a.provider_account_id,a.environment,remote)
                    ON CONFLICT DO NOTHING;
                SELECT reference INTO bound FROM public.refund_bindings WHERE reservation_id=a.reservation_id;
                IF bound IS DISTINCT FROM remote THEN outcome:='Unknown'; detail:='ConflictingReference'; END IF;
            END IF;
            INSERT INTO public.refund_observations(reservation_id,observed_at,outcome,reason,refund_reference)
                VALUES(a.reservation_id,at_time,outcome,detail,remote);
            IF previous IN ('Confirmed','Failed') AND outcome IN ('Confirmed','Failed') AND previous<>outcome THEN
                detail:='ConflictingProviderState';
            END IF;
            IF previous='Confirmed' OR (previous='Failed' AND outcome<>'Confirmed') THEN outcome:=previous; END IF;
            -- A missing local credential made no provider read. Keep the read budget available until it returns.
            read_deferred := sent AND outcome='Unknown' AND detail='OriginalAdapterUnavailable';
            UPDATE public.refund_work SET token=NULL,lease_until=NULL,state=outcome,last_check=at_time,
                reason=detail,refund_reference=coalesce(bound,refund_reference),
                candidate_reference=CASE WHEN coalesce(bound,refund_reference) IS NOT NULL THEN NULL
                    WHEN wake_pending AND candidate_reference IS NOT NULL AND candidate_reference IS DISTINCT FROM remote THEN candidate_reference
                    WHEN detail IN ('CandidateMismatch','ConflictingReference') THEN NULL
                    ELSE coalesce(remote,candidate_reference) END,
                checks=CASE WHEN read_deferred THEN checks ELSE least(checks+1,12) END,
                due_at=CASE WHEN wake_pending THEN at_time
                    WHEN read_deferred THEN at_time+interval '5 minutes'
                    WHEN outcome IN ('Confirmed','Failed')
                        OR detail IN ('BelowProviderMinimum','ProviderRefundUnsupported','LiveDispatchDisabled')
                        OR (sent AND checks>=11) THEN 'infinity'::timestamptz
                    ELSE at_time+interval '1 minute'*(1<<least(checks,5)) END WHERE reservation_id=a.reservation_id;
            IF outcome='Confirmed' AND previous<>'Confirmed' THEN
                RETURN QUERY SELECT * FROM public.apply_confirmed_refund(a,at_time);
            END IF;
        END $$;
        REVOKE ALL ON FUNCTION public.finish_refund(uuid,timestamptz,text,text,text) FROM PUBLIC;
        """;
}
