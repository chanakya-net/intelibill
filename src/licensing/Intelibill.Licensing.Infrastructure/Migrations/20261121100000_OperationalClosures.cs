using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261121100000_OperationalClosures")]
public sealed class OperationalClosures : Migration
{
    private const string Account = "account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public.operational_closures (
                id uuid PRIMARY KEY,
                account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
                actor_id uuid NOT NULL,
                shop_id uuid,
                operation_key uuid NOT NULL,
                request_digest text NOT NULL CHECK (request_digest ~ '^[0-9a-f]{64}$'),
                financial_treatment text NOT NULL CHECK (financial_treatment = 'NoFinancialChange'),
                recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
                UNIQUE (account_id, operation_key),
                UNIQUE (id, account_id)
            );
            CREATE UNIQUE INDEX account_closure_once ON public.operational_closures(account_id) WHERE shop_id IS NULL;
            CREATE TABLE public.operational_closure_targets (
                closure_id uuid NOT NULL,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                affiliation_request_id uuid NOT NULL,
                shop_name text NOT NULL,
                consent_id uuid NOT NULL,
                work_digest text NOT NULL CHECK (work_digest ~ '^[0-9a-f]{64}$'),
                event_id uuid NOT NULL UNIQUE,
                PRIMARY KEY (closure_id, shop_id),

                FOREIGN KEY (closure_id, account_id) REFERENCES public.operational_closures(id, account_id),
                FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                    REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
                FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id)
                    DEFERRABLE INITIALLY DEFERRED
            );
            CREATE INDEX closure_targets_affiliation ON public.operational_closure_targets(account_id, shop_id, affiliation_request_id);
            CREATE TABLE public.operational_closure_proposals (
                id uuid PRIMARY KEY,
                ordinal bigint GENERATED ALWAYS AS IDENTITY,
                account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
                actor_id uuid NOT NULL,
                billing_revision integer NOT NULL CHECK (billing_revision > 0),
                financial_treatment text NOT NULL CHECK (financial_treatment = 'NoFinancialChange'),
                scope_digest text NOT NULL CHECK (scope_digest ~ '^[0-9a-f]{64}$'),
                scope jsonb NOT NULL CHECK (jsonb_typeof(scope) = 'array'),
                recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
                expires_at timestamptz NOT NULL CHECK (expires_at > recorded_at),
                UNIQUE (id, account_id)
            );
            CREATE TABLE public.operational_closure_consent_notices (
                proposal_id uuid NOT NULL,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                PRIMARY KEY (proposal_id, shop_id),
                FOREIGN KEY (proposal_id, account_id) REFERENCES public.operational_closure_proposals(id, account_id)
            );
            CREATE INDEX account_closure_proposals ON public.operational_closure_proposals(account_id, ordinal DESC);
            """);
        foreach (var table in new[] { "operational_closures", "operational_closure_targets", "operational_closure_proposals", "operational_closure_consent_notices" })
        {
            migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope(table, Account));
            migrationBuilder.Sql($"""
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.operational_closures, public.operational_closure_proposals IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.operational_closures NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.operational_closure_proposals NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.operational_closures) OR EXISTS (SELECT FROM public.operational_closure_proposals) THEN
                RAISE EXCEPTION 'Retained closure evidence prevents downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.operational_closure_consent_notices, public.operational_closure_targets,
            public.operational_closures, public.operational_closure_proposals;
        """);
}
