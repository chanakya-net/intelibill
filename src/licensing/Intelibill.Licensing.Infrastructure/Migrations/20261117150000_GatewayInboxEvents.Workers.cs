namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class GatewayInboxEvents
{
    private static string AttemptLookupPolicy(string table) => $"""
        DO $$ BEGIN
            EXECUTE format('CREATE POLICY gateway_inbox_lookup ON public.{table} FOR SELECT TO %I USING (true)', current_user);
        END $$;
        """;

    private const string Claim = $$"""
        CREATE INDEX gateway_inbox_order_lookup ON public.payment_attempt_sessions(provider_order_reference);
        CREATE FUNCTION public.claim_gateway_inbox_event(p_now timestamptz, p_token uuid)
        RETURNS TABLE (inbox_id uuid, kind text, order_reference text, payment_reference text,
            account_id uuid, attempt_id uuid, candidates integer, attempts integer)
        LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH due AS MATERIALIZED (
                SELECT w.inbox_id FROM public.gateway_inbox_work w
                WHERE w.status IN ('Pending', 'Leased') AND w.next_attempt_at <= p_now
                    AND (w.lease_expires_at IS NULL OR w.lease_expires_at <= p_now)
                    AND {{Unscoped}}
                ORDER BY w.next_attempt_at, w.inbox_id LIMIT 1 FOR UPDATE SKIP LOCKED
            ), resolved AS MATERIALIZED (
                SELECT due.inbox_id, e.kind, e.provider_order_reference, e.provider_payment_reference,
                    m.candidates, CASE WHEN m.candidates = 1 THEN m.accounts[1] END AS account_id,
                    CASE WHEN m.candidates = 1 THEN m.attempts[1] END AS attempt_id
                FROM due JOIN public.gateway_inbox_events e USING (inbox_id)
                CROSS JOIN LATERAL (
                    SELECT count(*)::integer AS candidates, array_agg(a.account_id) AS accounts,
                        array_agg(a.attempt_id) AS attempts
                    FROM (
                        SELECT a.account_id, a.attempt_id
                        FROM public.payment_attempt_sessions s
                        JOIN public.payment_attempts a USING (account_id, attempt_id)
                        WHERE e.kind = 'PaymentCaptured' AND s.provider_order_reference = e.provider_order_reference
                            AND a.provider_id = e.provider_id AND a.provider_account_id = e.provider_account_id
                            AND a.environment = e.environment
                        LIMIT 2
                    ) a
                ) m
            ), leased AS (
                UPDATE public.gateway_inbox_work w SET
                    attempts = least(w.attempts + 1, 12),
                    status = CASE WHEN w.attempts < 12 THEN 'Leased' ELSE 'DeadLetter' END,
                    lease_token = CASE WHEN w.attempts < 12 THEN p_token END,
                    lease_expires_at = CASE WHEN w.attempts < 12 THEN p_now + interval '30 seconds' END,
                    next_attempt_at = p_now + interval '30 seconds',
                    last_failure = CASE WHEN w.status = 'Leased' THEN 'LeaseExpired' ELSE w.last_failure END,
                    finished_at = CASE WHEN w.attempts = 12 THEN p_now END,
                    account_id = r.account_id, attempt_id = r.attempt_id
                FROM resolved r WHERE w.inbox_id = r.inbox_id
                RETURNING w.inbox_id, w.lease_token, w.attempts
            )
            SELECT r.inbox_id, r.kind, r.provider_order_reference::text, r.provider_payment_reference::text,
                r.account_id, r.attempt_id, r.candidates, l.attempts
            FROM resolved r JOIN leased l USING (inbox_id) WHERE l.lease_token IS NOT NULL
        $$;
        REVOKE ALL ON FUNCTION public.claim_gateway_inbox_event(timestamptz,uuid) FROM PUBLIC;
        """;

    // Fulfilment can leave a shop scope after publishing its last entitlement. Finishing still uses the same attempt.
    private const string AttemptScope = $"w.account_id = {Account} AND w.attempt_id = {Attempt} AND {NoActor}";

    private const string Finish = $$"""
        CREATE FUNCTION public.lock_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamptz)
        RETURNS boolean LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT EXISTS (SELECT FROM public.gateway_inbox_work w WHERE w.inbox_id = p_inbox
                AND w.status = 'Leased' AND w.lease_token = p_token AND w.lease_expires_at > p_now
                AND {{AttemptScope}} AND {{NoShop}} FOR UPDATE)
        $$;
        CREATE FUNCTION public.finish_gateway_inbox_event(p_inbox uuid, p_token uuid, p_now timestamptz,
            p_outcome text, p_failure text, p_dead boolean)
        RETURNS boolean LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH finished AS (
                UPDATE public.gateway_inbox_work w SET
                    status = CASE WHEN p_outcome IS NOT NULL THEN 'Processed'
                                  WHEN p_dead OR w.attempts = 12 THEN 'DeadLetter' ELSE 'Pending' END,
                    outcome = p_outcome, last_failure = p_failure, lease_token = NULL, lease_expires_at = NULL,
                    next_attempt_at = p_now + make_interval(secs => least(300, (2 << least(w.attempts, 8)))),
                    finished_at = CASE WHEN p_outcome IS NOT NULL OR p_dead OR w.attempts = 12 THEN p_now END
                WHERE w.inbox_id = p_inbox AND w.status = 'Leased' AND w.lease_token = p_token
                    AND w.lease_expires_at > p_now
                    AND (({{AttemptScope}}) OR ({{Unscoped}} AND (p_outcome IS NULL OR p_outcome = 'NoAction')))
                RETURNING w.inbox_id
            )
            SELECT EXISTS (SELECT FROM finished)
        $$;
        CREATE FUNCTION public.requeue_gateway_inbox_event(p_inbox uuid, p_now timestamptz)
        RETURNS boolean LANGUAGE sql SET search_path = pg_catalog AS $$
            WITH queued AS (
                UPDATE public.gateway_inbox_work SET status = 'Pending', next_attempt_at = p_now, attempts = 0,
                    finished_at = NULL
                WHERE inbox_id = p_inbox AND status = 'DeadLetter'
                RETURNING inbox_id
            )
            SELECT EXISTS (SELECT FROM queued)
        $$;
        REVOKE ALL ON FUNCTION public.lock_gateway_inbox_event(uuid,uuid,timestamptz) FROM PUBLIC;
        REVOKE ALL ON FUNCTION public.finish_gateway_inbox_event(uuid,uuid,timestamptz,text,text,boolean) FROM PUBLIC;
        REVOKE ALL ON FUNCTION public.requeue_gateway_inbox_event(uuid,timestamptz) FROM PUBLIC;
        """;
}
