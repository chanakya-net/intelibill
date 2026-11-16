using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261117100000_PendingPaymentReconciliation")]
public sealed class PendingPaymentReconciliation : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string NoActor = "nullif(current_setting('app.billing_actor_id', true), '') IS NULL";
    private const string NoShop = "nullif(current_setting('app.status_shop_id', true), '') IS NULL";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX checkout_order_shops_covered ON public.checkout_order_shops(account_id, shop_id, order_id);
            CREATE TABLE public.pending_payment_attempts (
                attempt_id uuid PRIMARY KEY,
                account_id uuid NOT NULL,
                next_attempt_at timestamptz NOT NULL CHECK (isfinite(next_attempt_at)),
                attempts integer NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 12),
                completed boolean NOT NULL DEFAULT false,
                lease_token uuid,
                lease_expires_at timestamptz CHECK (isfinite(lease_expires_at)),
                CHECK ((lease_token IS NULL) = (lease_expires_at IS NULL)),
                CHECK (NOT completed OR lease_token IS NULL),
                FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id)
            );
            CREATE INDEX pending_payment_attempts_due ON public.pending_payment_attempts(next_attempt_at, attempt_id)
                WHERE NOT completed;
            CREATE FUNCTION public.queue_pending_payment() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $$
            BEGIN
                INSERT INTO public.pending_payment_attempts (attempt_id, account_id, next_attempt_at)
                VALUES (NEW.attempt_id, NEW.account_id, NEW.created_at + interval '30 seconds');
                RETURN NEW;
            END $$;
            REVOKE ALL ON FUNCTION public.queue_pending_payment() FROM PUBLIC;
            CREATE TRIGGER queue_pending_payment AFTER INSERT ON public.payment_attempts
                FOR EACH ROW EXECUTE FUNCTION public.queue_pending_payment();

            LOCK TABLE public.payment_attempts IN SHARE ROW EXCLUSIVE MODE;
            ALTER TABLE public.payment_attempts NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.verified_payments NO FORCE ROW LEVEL SECURITY;
            INSERT INTO public.pending_payment_attempts (attempt_id, account_id, next_attempt_at)
            SELECT a.attempt_id, a.account_id, a.created_at + interval '30 seconds'
            FROM public.payment_attempts a WHERE NOT EXISTS (
                SELECT FROM public.verified_payments v WHERE v.attempt_id = a.attempt_id);
            ALTER TABLE public.payment_attempts FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.verified_payments FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.pending_payment_attempts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.pending_payment_attempts FORCE ROW LEVEL SECURITY;
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.WorkerAccess("pending_payment_attempts"));
        migrationBuilder.Sql(Workers);
        migrationBuilder.Sql(SimulatorControls);
    }

    private static readonly string Workers = $$"""
        CREATE FUNCTION public.claim_pending_payment(p_now timestamptz, p_token uuid)
        RETURNS TABLE (account_id uuid, attempt_id uuid)
        LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH due AS (
                SELECT p.attempt_id FROM public.pending_payment_attempts p WHERE NOT p.completed
                    AND p.next_attempt_at <= p_now AND (p.lease_expires_at IS NULL OR p.lease_expires_at <= p_now)
                    AND (p.attempts < 12 OR p.lease_token IS NOT NULL)
                    AND {{NoActor}} AND {{NoShop}} AND {{Attempt}} IS NULL AND {{LeasedHttpOutboxSchema.Unscoped}}
                ORDER BY p.next_attempt_at, p.attempt_id LIMIT 1 FOR UPDATE SKIP LOCKED
            ), claimed AS (
                UPDATE public.pending_payment_attempts p SET attempts = least(p.attempts + 1, 12),
                    lease_token = CASE WHEN p.attempts < 12 THEN p_token END,
                    lease_expires_at = CASE WHEN p.attempts < 12 THEN p_now + interval '30 seconds' END
                FROM due WHERE p.attempt_id = due.attempt_id RETURNING p.account_id, p.attempt_id, p.lease_token
            )
            SELECT c.account_id, c.attempt_id FROM claimed c WHERE c.lease_token IS NOT NULL
        $$;
        CREATE FUNCTION public.lock_pending_payment(p_token uuid, p_now timestamptz)
        RETURNS boolean LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT EXISTS (SELECT FROM public.pending_payment_attempts p WHERE p.account_id = {{Account}}
                AND p.attempt_id = {{Attempt}} AND {{NoActor}} AND NOT p.completed
                AND p.lease_token = p_token AND p.lease_expires_at > p_now FOR UPDATE)
        $$;
        CREATE FUNCTION public.finish_pending_payment(p_token uuid, p_now timestamptz, p_complete boolean)
        RETURNS void LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            UPDATE public.pending_payment_attempts p SET completed = p_complete, lease_token = NULL, lease_expires_at = NULL,
                next_attempt_at = p_now + make_interval(secs => least(300, (2 << least(p.attempts, 8))))
            WHERE p.account_id = {{Account}} AND p.attempt_id = {{Attempt}} AND {{NoActor}}
                AND p.lease_token = p_token AND p.lease_expires_at > p_now
        $$;
        CREATE FUNCTION public.complete_pending_payment()
        RETURNS void LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            UPDATE public.pending_payment_attempts p SET completed = true, lease_token = NULL, lease_expires_at = NULL
            WHERE p.account_id = {{Account}} AND p.attempt_id = {{Attempt}} AND {{NoActor}} AND NOT p.completed
                AND (EXISTS (SELECT FROM public.verified_payments v WHERE v.attempt_id = p.attempt_id)
                    OR EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = p.attempt_id AND e.kind = 'CheckoutFailed'))
        $$;
        REVOKE ALL ON FUNCTION public.complete_pending_payment() FROM PUBLIC;
        REVOKE ALL ON FUNCTION public.claim_pending_payment(timestamptz,uuid) FROM PUBLIC;
        REVOKE ALL ON FUNCTION public.lock_pending_payment(uuid,timestamptz) FROM PUBLIC;
        REVOKE ALL ON FUNCTION public.finish_pending_payment(uuid,timestamptz,boolean) FROM PUBLIC;
        """;

    private const string SimulatorControls = """
        ALTER TABLE public.payment_events DROP CONSTRAINT payment_events_kind_check,
            ADD CONSTRAINT payment_events_kind_check CHECK (kind IN ('AttemptStarted', 'SessionRecorded',
                'VerificationRequested', 'OrderFulfilled', 'PaidNeedsReview', 'CheckoutFailed'));

        ALTER TABLE public.development_gateway_sessions ADD COLUMN order_id uuid;
        ALTER TABLE public.development_gateway_sessions NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_attempts NO FORCE ROW LEVEL SECURITY;
        UPDATE public.development_gateway_sessions s SET order_id = a.order_id FROM public.payment_attempts a
            WHERE s.account_id = a.account_id AND s.attempt_id = a.attempt_id;
        ALTER TABLE public.payment_attempts FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.development_gateway_sessions FORCE ROW LEVEL SECURITY;

        CREATE TABLE public.development_gateway_controls (
            provider_order_reference varchar(120) PRIMARY KEY REFERENCES public.development_gateway_sessions(provider_order_reference),
            outcome text NOT NULL CHECK (outcome IN ('Timeout', 'Delayed', 'Failed')),
            capture_at timestamptz CHECK (isfinite(capture_at)),
            CHECK ((outcome = 'Delayed') = (capture_at IS NOT NULL))
        );
        ALTER TABLE public.development_gateway_controls ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.development_gateway_controls FORCE ROW LEVEL SECURITY;
        CREATE POLICY simulator_only ON public.development_gateway_controls
            USING (current_setting('app.development_gateway', true) = 'on')
            WITH CHECK (current_setting('app.development_gateway', true) = 'on');
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.payment_events DROP CONSTRAINT payment_events_kind_check,
            ADD CONSTRAINT payment_events_kind_check CHECK (kind IN ('AttemptStarted', 'SessionRecorded',
                'VerificationRequested', 'OrderFulfilled', 'PaidNeedsReview'));
        DROP TABLE public.development_gateway_controls;
        ALTER TABLE public.development_gateway_sessions DROP COLUMN order_id;
        DROP TRIGGER queue_pending_payment ON public.payment_attempts;
        DROP FUNCTION public.queue_pending_payment();
        DROP FUNCTION public.claim_pending_payment(timestamptz,uuid);
        DROP FUNCTION public.lock_pending_payment(uuid,timestamptz);
        DROP FUNCTION public.finish_pending_payment(uuid,timestamptz,boolean);
        DROP FUNCTION public.complete_pending_payment();
        DROP TABLE public.pending_payment_attempts;
        DROP INDEX public.checkout_order_shops_covered;
        """);
}
