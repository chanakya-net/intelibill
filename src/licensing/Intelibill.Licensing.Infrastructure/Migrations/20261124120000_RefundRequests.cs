using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-08: SQL-owned immutable refund request evidence linked to a verified payment confirmation. A request never
/// approves a refund, moves funds, or changes subscription term, coverage, restriction or entitlement state.
/// Down refuses while any refund request row is retained.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261124120000_RefundRequests")]
public sealed partial class RefundRequests : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";
    private const string CreateAction = "billing.refund-request.create";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql($"""
            ALTER TABLE public.refund_requests ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.refund_requests FORCE ROW LEVEL SECURITY;
            CREATE POLICY account_read ON public.refund_requests FOR SELECT USING (account_id = {Account});
            CREATE POLICY refund_request_insert ON public.refund_requests FOR INSERT WITH CHECK (
                account_id = {Account} AND actor_id = {Actor} AND EXISTS (
                    SELECT FROM public.billing_account_members m
                    WHERE m.account_id = refund_requests.account_id AND m.merchant_user_id = {Actor}
                        AND m.status = 1 AND m.role IN (1, 2))
                AND EXISTS (
                    SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = refund_requests.replay_id AND u.account_id = refund_requests.account_id
                        AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid AND u.action = '{CreateAction}'));
            CREATE TRIGGER refund_request_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_requests
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            """);
        migrationBuilder.Sql(Guards);
    }

    private const string Tables = """
        CREATE TABLE public.refund_requests (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            refund_request_id uuid NOT NULL,
            order_id uuid NOT NULL,
            -- Verified against immutable public.payment_confirmations by guard_refund_request() (no FK so
            -- payment_confirmations keeps its BEFORE TRUNCATE 55000 retention contract intact).
            confirmation_id uuid NOT NULL,
            attempt_id uuid,
            manual_receipt_id uuid,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            provider_id text NOT NULL CHECK (length(provider_id) BETWEEN 1 AND 80),
            provider_account_id text NOT NULL CHECK (length(provider_account_id) BETWEEN 1 AND 200),
            environment text NOT NULL CHECK (length(environment) BETWEEN 1 AND 40),
            configuration_version text NOT NULL CHECK (length(configuration_version) BETWEEN 1 AND 200),
            payment_amount_paise bigint NOT NULL CHECK (payment_amount_paise BETWEEN 1 AND 1000000000000),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND payment_amount_paise),
            currency text NOT NULL CHECK (currency = 'INR'),
            reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 500 AND reason = btrim(reason) AND reason !~ '[[:cntrl:]]'),
            requested_at timestamptz NOT NULL CHECK (isfinite(requested_at)),
            PRIMARY KEY (account_id, refund_request_id),
            UNIQUE (account_id, operation_key),
            UNIQUE (replay_id),
            CHECK ((attempt_id IS NULL) <> (manual_receipt_id IS NULL)),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id)
        );
        CREATE INDEX refund_requests_account_order ON public.refund_requests (account_id, requested_at, refund_request_id);
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.refund_requests IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.refund_requests NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.refund_requests) THEN
                RAISE EXCEPTION 'Retained refund requests prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.refund_requests;
        DROP FUNCTION public.guard_refund_request();
        """);
}
