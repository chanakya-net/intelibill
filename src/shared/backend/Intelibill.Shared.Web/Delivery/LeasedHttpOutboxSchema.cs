namespace Intelibill.Shared.Web.Delivery;

public static class LeasedHttpOutboxSchema
{
    public const string Create = """
        CREATE TABLE public.entitlement_messages (
            event_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('Snapshot', 'Acknowledgement', 'Reconciliation')),
            payload bytea NOT NULL CHECK (octet_length(payload) BETWEEN 1 AND 8192),
            digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
            UNIQUE (event_id, account_id, shop_id)
        );
        CREATE TABLE public.entitlement_outbox (
            event_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            status text NOT NULL DEFAULT 'Pending'
                CHECK (status IN ('Pending', 'Leased', 'AwaitingAcknowledgement', 'Delivered', 'Applied', 'NeedsReview', 'DeadLetter')),
            next_attempt_at timestamptz NOT NULL CHECK (isfinite(next_attempt_at)),
            lease_token uuid,
            lease_expires_at timestamptz CHECK (isfinite(lease_expires_at)),
            attempts integer NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 12),
            last_failure text CHECK (last_failure IN ('Unavailable', 'Rejected', 'InvalidResponse', 'AcknowledgementMissing', 'LeaseExpired')),
            CHECK ((status = 'Leased') = (lease_token IS NOT NULL AND lease_expires_at IS NOT NULL)),
            CHECK ((lease_token IS NULL) = (lease_expires_at IS NULL)),
            FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id)
        );
        CREATE INDEX entitlement_outbox_due ON public.entitlement_outbox(next_attempt_at, event_id)
            WHERE status IN ('Pending', 'Leased', 'AwaitingAcknowledgement');
        CREATE FUNCTION public.retain_entitlement_evidence() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            RAISE EXCEPTION 'Entitlement evidence is immutable' USING ERRCODE = '55000';
        END $$;
        CREATE TRIGGER retain_entitlement_messages_rows BEFORE UPDATE OR DELETE ON public.entitlement_messages
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_entitlement_messages_table BEFORE TRUNCATE ON public.entitlement_messages
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        """;

    public const string Unscoped = """
        nullif(current_setting('app.active_shop_id', true), '') IS NULL
        AND nullif(current_setting('app.current_user_id', true), '') IS NULL
        AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
        AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
        """;

    // The signed receipt atomically settles this outbox row. Until then, keep probing the same
    // immutable command at the capped backoff, including when the final delivery lost its reply.
    private const string DeadCapacity = """
        OR o.status = 'DeadLetter' AND EXISTS (SELECT FROM public.entitlement_messages m
            WHERE m.event_id = o.event_id AND m.kind IN ('CapacityReserve','CapacityConsume'))
        """;

    private const string DeadCapacityAndReduction = """
        OR o.status = 'DeadLetter' AND EXISTS (SELECT FROM public.entitlement_messages m
            WHERE m.event_id = o.event_id AND m.kind IN ('CapacityReserve','CapacityConsume','ReductionManifest','ReductionReserve','ReductionConsume'))
        """;

    private const string DeadCapacityReductionAndExceptions = """
        OR o.status = 'DeadLetter' AND EXISTS (SELECT FROM public.entitlement_messages m
            WHERE m.event_id = o.event_id AND m.kind IN ('CapacityReserve','CapacityConsume','ReductionManifest','ReductionReserve','ReductionConsume','CoverageExceptionPrepare','CoverageExceptionConsume','CoverageExceptionAbort'))
        """;

    public static string Claim(bool recoverCapacity, bool recoverReductions = false, bool recoverExceptions = false) => $$"""
        CREATE OR REPLACE FUNCTION public.claim_entitlement_delivery(p_now timestamptz, p_lease uuid)
        RETURNS TABLE (event_id uuid, kind text, payload bytea, attempts integer)
        LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH due AS MATERIALIZED (
                SELECT o.event_id, (o.attempts < 12 OR o.status = 'DeadLetter') AS claimable FROM public.entitlement_outbox o
                WHERE (o.status IN ('Pending', 'Leased', 'AwaitingAcknowledgement') {{(recoverExceptions ? DeadCapacityReductionAndExceptions : recoverReductions ? DeadCapacityAndReduction : recoverCapacity ? DeadCapacity : "")}})
                    AND o.next_attempt_at <= p_now AND {{Unscoped}}
                ORDER BY o.next_attempt_at, o.event_id LIMIT 1 FOR UPDATE SKIP LOCKED
            ), claimed AS (
                UPDATE public.entitlement_outbox o
                SET status = CASE WHEN due.claimable THEN 'Leased' ELSE 'DeadLetter' END,
                    lease_token = CASE WHEN due.claimable THEN p_lease END,
                    lease_expires_at = CASE WHEN due.claimable THEN p_now + interval '30 seconds' END,
                    next_attempt_at = p_now + interval '30 seconds',
                    last_failure = CASE WHEN o.status = 'Leased' THEN 'LeaseExpired' ELSE o.last_failure END,
                    attempts = least(o.attempts + 1, 12)
                FROM due WHERE o.event_id = due.event_id
                RETURNING o.event_id, o.attempts, o.status
            )
            SELECT c.event_id, m.kind, m.payload, c.attempts FROM claimed c
            JOIN public.entitlement_messages m USING (event_id) WHERE c.status = 'Leased'
        $$;
        """;

    public static string Finish(string beforeFinish = "") => $$"""
        CREATE OR REPLACE FUNCTION public.finish_entitlement_delivery(p_event uuid, p_lease uuid, p_now timestamptz,
            p_success boolean, p_wait_ack boolean, p_failure text)
        RETURNS void LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            {{beforeFinish}}
            UPDATE public.entitlement_outbox
            SET status = CASE WHEN p_success AND NOT p_wait_ack THEN 'Delivered'
                    WHEN attempts >= 12 THEN 'DeadLetter'
                    WHEN p_success THEN 'AwaitingAcknowledgement' ELSE 'Pending' END,
                lease_token = NULL, lease_expires_at = NULL,
                next_attempt_at = p_now + make_interval(secs => least(300, (2 << least(attempts, 8)))),
                last_failure = CASE WHEN p_success AND p_wait_ack THEN 'AcknowledgementMissing'
                    WHEN p_success THEN NULL ELSE p_failure END
            WHERE event_id = p_event AND status = 'Leased' AND lease_token = p_lease AND lease_expires_at > p_now AND {{Unscoped}}
        $$;
        """;

    public static string Workers => Claim(false) + Finish() + """

        REVOKE ALL ON FUNCTION public.claim_entitlement_delivery(timestamptz, uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION public.finish_entitlement_delivery(uuid, uuid, timestamptz, boolean, boolean, text) FROM PUBLIC;
        """;

    public static string Scope(string table, string predicate) => $"""
        ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
        CREATE POLICY entitlement_scope ON public.{table} USING ({predicate}) WITH CHECK ({predicate});
        """;

    public static string WorkerAccess(string table) => $"""
        DO $$ BEGIN
            EXECUTE format('CREATE POLICY entitlement_worker ON public.{table} TO %I USING (true)', current_user);
        END $$;
        """;

    public const string Drop = """
        DROP FUNCTION public.finish_entitlement_delivery(uuid, uuid, timestamptz, boolean, boolean, text);
        DROP FUNCTION public.claim_entitlement_delivery(timestamptz, uuid);
        DROP TABLE public.entitlement_outbox, public.entitlement_messages;
        DROP FUNCTION public.retain_entitlement_evidence();
        """;
}
