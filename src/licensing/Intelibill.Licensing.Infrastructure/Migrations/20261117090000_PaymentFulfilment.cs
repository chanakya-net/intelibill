using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// SQL-owned payment attempts, recorded checkout sessions, payment audit events, verified payments and exactly one
/// fulfilment per order with its paid shop links and the entitlement snapshot revision each link published. Every
/// row is retained. Writes after the attempt start need the transaction-local attempt scope. Also the local
/// simulator's own provider-side tables, written only by the development adapter.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261117090000_PaymentFulfilment")]
public sealed partial class PaymentFulfilment : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string Shop = "nullif(current_setting('app.status_shop_id', true), '')::uuid";
    private const string Hex64 = "~ '^[a-f0-9]{64}$'";

    private static readonly string[] Evidence = ["payment_attempts", "payment_attempt_sessions", "payment_events",
        "verified_payments", "payment_fulfilments", "payment_fulfilment_shops", "payment_fulfilment_deliveries"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(SimulatorTables);
        foreach (var table in Evidence)
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY account_read ON public.{table} FOR SELECT USING (account_id = {Account});
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql(Completeness);
    }

    private const string Pin = """
            provider_id varchar(40) NOT NULL CHECK (length(provider_id) > 0),
            provider_account_id varchar(80) NOT NULL CHECK (length(provider_account_id) > 0),
            environment varchar(20) NOT NULL CHECK (environment IN ('Development', 'Test', 'Live')),
            configuration_version varchar(80) NOT NULL CHECK (length(configuration_version) > 0),
        """;

    private static readonly string Tables = $"""
        CREATE TABLE public.payment_attempts (
            attempt_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            actor_id uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash {Hex64}),
            replay_id uuid NOT NULL UNIQUE,
            {Pin}
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            currency text NOT NULL CHECK (currency = 'INR'),
            snapshot_hash text NOT NULL CHECK (snapshot_hash {Hex64}),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            UNIQUE (account_id, attempt_id), UNIQUE (account_id, order_id), UNIQUE (account_id, operation_key)
        );
        CREATE TABLE public.payment_attempt_sessions (
            attempt_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            provider_order_reference varchar(120) NOT NULL CHECK (length(provider_order_reference) > 0),
            launcher_flow varchar(40) NOT NULL CHECK (length(launcher_flow) > 0),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id)
        );
        CREATE TABLE public.payment_events (
            event_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            attempt_id uuid NOT NULL,
            order_id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('AttemptStarted', 'SessionRecorded', 'VerificationRequested',
                'OrderFulfilled', 'PaidNeedsReview')),
            actor_id uuid,
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id)
        );
        CREATE INDEX payment_events_attempt ON public.payment_events(attempt_id, kind);
        CREATE TABLE public.verified_payments (
            attempt_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            {Pin}
            provider_payment_reference varchar(120) NOT NULL CHECK (length(provider_payment_reference) > 0),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            currency text NOT NULL CHECK (currency = 'INR'),
            captured_at timestamptz NOT NULL CHECK (isfinite(captured_at)),
            verified_at timestamptz NOT NULL CHECK (isfinite(verified_at)),
            UNIQUE (provider_id, provider_account_id, environment, provider_payment_reference),
            FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id)
        );
        CREATE TABLE public.payment_fulfilments (
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            attempt_id uuid NOT NULL UNIQUE REFERENCES public.verified_payments(attempt_id),
            term_id uuid NOT NULL UNIQUE,
            policy_version text NOT NULL CHECK (policy_version = 'payment-fulfilment/1'),
            fulfilled_at timestamptz NOT NULL CHECK (isfinite(fulfilled_at)),
            PRIMARY KEY (account_id, order_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
                DEFERRABLE INITIALLY DEFERRED
        );
        CREATE TABLE public.payment_fulfilment_shops (
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            term_id uuid NOT NULL,
            shop_id uuid NOT NULL UNIQUE,
            affiliation_request_id uuid NOT NULL,
            PRIMARY KEY (account_id, order_id, shop_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id),
            FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id)
        );
        CREATE INDEX payment_fulfilment_shops_term ON public.payment_fulfilment_shops(term_id);
        CREATE TABLE public.payment_fulfilment_deliveries (
            account_id uuid NOT NULL,
            order_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            shop_revision bigint NOT NULL CHECK (shop_revision > 0),
            PRIMARY KEY (account_id, order_id, shop_id),
            FOREIGN KEY (account_id, order_id, shop_id) REFERENCES public.payment_fulfilment_shops(account_id, order_id, shop_id)
        );
        """;

    private const string SimulatorTables = """
        CREATE TABLE public.development_gateway_sessions (
            provider_order_reference varchar(120) PRIMARY KEY,
            account_id uuid NOT NULL,
            attempt_id uuid NOT NULL UNIQUE,
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            currency text NOT NULL CHECK (currency = 'INR'),
            created_at timestamptz NOT NULL
        );
        CREATE TABLE public.development_gateway_payments (
            provider_payment_reference varchar(120) PRIMARY KEY,
            provider_order_reference varchar(120) NOT NULL UNIQUE
                REFERENCES public.development_gateway_sessions(provider_order_reference),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            currency text NOT NULL CHECK (currency = 'INR'),
            state text NOT NULL CHECK (state = 'Captured'),
            captured_at timestamptz NOT NULL
        );
        ALTER TABLE public.development_gateway_sessions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.development_gateway_sessions FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.development_gateway_payments ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.development_gateway_payments FORCE ROW LEVEL SECURITY;
        CREATE POLICY simulator_only ON public.development_gateway_sessions FOR ALL
            USING (current_setting('app.development_gateway', true) = 'on')
            WITH CHECK (current_setting('app.development_gateway', true) = 'on');
        CREATE POLICY simulator_only ON public.development_gateway_payments FOR ALL
            USING (current_setting('app.development_gateway', true) = 'on')
            WITH CHECK (current_setting('app.development_gateway', true) = 'on');
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Rollback);
}
