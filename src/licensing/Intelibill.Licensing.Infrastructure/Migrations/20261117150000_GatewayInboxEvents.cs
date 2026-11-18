using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261117150000_GatewayInboxEvents")]
public sealed partial class GatewayInboxEvents : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string NoActor = "nullif(current_setting('app.billing_actor_id', true), '') IS NULL";
    private const string NoShop = "nullif(current_setting('app.status_shop_id', true), '') IS NULL";
    private const string Unscoped = $"{NoActor} AND {NoShop} AND {Attempt} IS NULL AND {LeasedHttpOutboxSchema.Unscoped}";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.WorkerAccess("gateway_inbox_events"));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.WorkerAccess("gateway_inbox_work"));
        migrationBuilder.Sql(AttemptLookupPolicy("payment_attempts"));
        migrationBuilder.Sql(AttemptLookupPolicy("payment_attempt_sessions"));
        migrationBuilder.Sql(Receive);
        migrationBuilder.Sql(Claim);
        migrationBuilder.Sql(Finish);
    }

    private const string Tables = """
        CREATE TABLE public.gateway_inbox_events (
            inbox_id uuid PRIMARY KEY,
            provider_id varchar(80) NOT NULL,
            provider_account_id varchar(80) NOT NULL,
            environment text NOT NULL CHECK (environment IN ('Development', 'Test', 'Live')),
            configuration_version varchar(80) NOT NULL,
            provider_event_id varchar(120) NOT NULL CHECK (length(provider_event_id) > 0),
            kind text NOT NULL CHECK (kind IN ('PaymentCaptured', 'PaymentAuthorized', 'PaymentFailed', 'Other')),
            provider_order_reference varchar(120),
            provider_payment_reference varchar(120),
            body_sha256 bytea NOT NULL CHECK (octet_length(body_sha256) = 32),
            protected_evidence bytea NOT NULL CHECK (octet_length(protected_evidence) BETWEEN 1 AND 4096),
            received_at timestamptz NOT NULL CHECK (isfinite(received_at)),
            UNIQUE (provider_id, provider_account_id, environment, provider_event_id)
        );
        CREATE TABLE public.gateway_inbox_work (
            inbox_id uuid PRIMARY KEY REFERENCES public.gateway_inbox_events(inbox_id),
            status text NOT NULL DEFAULT 'Pending' CHECK (status IN ('Pending', 'Leased', 'Processed', 'DeadLetter')),
            outcome text CHECK (outcome IN ('Fulfilled', 'VerifiedNeedsReview', 'AlreadyVerified', 'NoAction')),
            last_failure text CHECK (last_failure IN ('UnknownOrder', 'AmbiguousOrder', 'AdapterUnavailable',
                'CaptureUnproven', 'DifferentPayment', 'Busy', 'Unavailable', 'LeaseExpired')),
            attempts int NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 12),
            next_attempt_at timestamptz NOT NULL CHECK (isfinite(next_attempt_at)),
            lease_token uuid,
            lease_expires_at timestamptz CHECK (isfinite(lease_expires_at)),
            account_id uuid,
            attempt_id uuid,
            finished_at timestamptz CHECK (isfinite(finished_at)),
            FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id) MATCH FULL,
            CHECK ((status = 'Leased') = (lease_token IS NOT NULL)),
            CHECK ((lease_token IS NULL) = (lease_expires_at IS NULL)),
            CHECK ((status IN ('Processed', 'DeadLetter')) = (finished_at IS NOT NULL)),
            CHECK ((status = 'Processed') = (outcome IS NOT NULL)),
            CHECK (status <> 'DeadLetter' OR last_failure IS NOT NULL)
        );
        CREATE INDEX gateway_inbox_due ON public.gateway_inbox_work(next_attempt_at, inbox_id)
            WHERE status IN ('Pending', 'Leased');
        ALTER TABLE public.gateway_inbox_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.gateway_inbox_events FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.gateway_inbox_work ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.gateway_inbox_work FORCE ROW LEVEL SECURITY;
        CREATE TRIGGER gateway_work_retained BEFORE DELETE OR TRUNCATE ON public.gateway_inbox_work
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER gateway_event_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.gateway_inbox_events
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private const string Receive = $$"""
        CREATE FUNCTION public.receive_gateway_inbox_event(p_inbox uuid, p_provider text, p_account text,
            p_environment text, p_version text, p_event text, p_kind text, p_order text, p_payment text,
            p_hash bytea, p_evidence bytea, p_now timestamptz)
        RETURNS boolean LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH stored AS (
                INSERT INTO public.gateway_inbox_events
                    (inbox_id, provider_id, provider_account_id, environment, configuration_version, provider_event_id,
                     kind, provider_order_reference, provider_payment_reference, body_sha256, protected_evidence, received_at)
                SELECT p_inbox, p_provider, p_account, p_environment, p_version, p_event, p_kind, p_order, p_payment,
                    p_hash, p_evidence, p_now WHERE {{Unscoped}}
                ON CONFLICT (provider_id, provider_account_id, environment, provider_event_id) DO NOTHING
                RETURNING inbox_id
            ), queued AS (
                INSERT INTO public.gateway_inbox_work (inbox_id, next_attempt_at)
                SELECT inbox_id, p_now FROM stored RETURNING inbox_id
            )
            SELECT EXISTS (SELECT FROM queued)
        $$;
        REVOKE ALL ON FUNCTION public.receive_gateway_inbox_event(uuid,text,text,text,text,text,text,text,text,bytea,bytea,timestamptz)
            FROM PUBLIC;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
            LOCK TABLE public.gateway_inbox_events IN ACCESS EXCLUSIVE MODE;
            IF EXISTS (SELECT FROM public.gateway_inbox_events) THEN
                RAISE EXCEPTION 'Gateway inbox evidence is retained' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP FUNCTION public.receive_gateway_inbox_event(uuid,text,text,text,text,text,text,text,text,bytea,bytea,timestamptz);
        DROP FUNCTION public.claim_gateway_inbox_event(timestamptz,uuid);
        DROP FUNCTION public.lock_gateway_inbox_event(uuid,uuid,timestamptz);
        DROP FUNCTION public.finish_gateway_inbox_event(uuid,uuid,timestamptz,text,text,boolean);
        DROP FUNCTION public.requeue_gateway_inbox_event(uuid,timestamptz);
        DROP POLICY gateway_inbox_lookup ON public.payment_attempts;
        DROP POLICY gateway_inbox_lookup ON public.payment_attempt_sessions;
        DROP INDEX public.gateway_inbox_order_lookup;
        DROP TABLE public.gateway_inbox_work, public.gateway_inbox_events;
        """);
}
