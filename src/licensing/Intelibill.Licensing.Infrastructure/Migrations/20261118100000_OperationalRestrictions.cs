using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261118100000_OperationalRestrictions")]
public sealed class OperationalRestrictions : Migration
{
    private const string AccountScope = "account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE POLICY restriction_account_lookup ON public.billing_accounts FOR SELECT USING
                (id = nullif(current_setting('app.restriction_account_id', true), '')::uuid);
            CREATE TABLE public.operational_restrictions (
                id uuid PRIMARY KEY,
                account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
                shop_id uuid,
                version bigint NOT NULL CHECK (version > 0),
                mode text NOT NULL CHECK (mode IN ('Trading', 'Security')),
                effective_at timestamptz NOT NULL CHECK (isfinite(effective_at)),
                recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at) AND recorded_at <= effective_at),
                customer_explanation text NOT NULL CHECK (length(trim(customer_explanation)) BETWEEN 1 AND 500),
                internal_reason text NOT NULL CHECK (length(trim(internal_reason)) BETWEEN 1 AND 1000),
                review_reference text NOT NULL CHECK (length(trim(review_reference)) BETWEEN 1 AND 120),
                actor_id uuid NOT NULL REFERENCES public.admin_identities(id),
                session_id uuid NOT NULL,
                operation_key uuid NOT NULL,
                payload_digest text NOT NULL CHECK (payload_digest ~ '^[0-9a-f]{64}$'),
                UNIQUE (id, account_id),
                UNIQUE NULLS NOT DISTINCT (account_id, shop_id, version),
                UNIQUE NULLS NOT DISTINCT (account_id, shop_id),
                UNIQUE (account_id, operation_key)
            );
            CREATE TABLE public.operational_restriction_deliveries (
                restriction_id uuid NOT NULL,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                shop_name text NOT NULL,
                event_id uuid NOT NULL UNIQUE,
                PRIMARY KEY (restriction_id, shop_id),
                FOREIGN KEY (restriction_id, account_id) REFERENCES public.operational_restrictions(id, account_id),
                FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id)
            );
            """);
        foreach (var table in new[] { "operational_restrictions", "operational_restriction_deliveries" })
        {
            migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope(table, AccountScope));
            migrationBuilder.Sql($"""
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.operational_restrictions IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.operational_restrictions NO FORCE ROW LEVEL SECURITY;
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM public.operational_restrictions) THEN
                RAISE EXCEPTION 'Operational restriction evidence must be retained' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.operational_restriction_deliveries, public.operational_restrictions;
        DROP POLICY restriction_account_lookup ON public.billing_accounts;
        """);
}
