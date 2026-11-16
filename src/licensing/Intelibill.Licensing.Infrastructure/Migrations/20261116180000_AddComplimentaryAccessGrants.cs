using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// Dated complimentary grants (SQL-owned, no EF model): an explicit per-account grant revision, the immutable
/// operational grant and its named shops, and a provider-only receipt holding actor, reason and digests. Writes
/// need the transaction-local provider grant context; reads are scoped to an account or a named shop.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261116180000_AddComplimentaryAccessGrants")]
public sealed partial class AddComplimentaryAccessGrants : Migration
{
    private const string GrantContext = "nullif(current_setting('app.grant_admin_id', true), '') IS NOT NULL";
    private const string AccountScope = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string ShopScope = "nullif(current_setting('app.status_shop_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "complimentary_grant_scopes", "complimentary_grants", "complimentary_grant_receipts", "complimentary_grant_shops" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }
        foreach (var table in new[] { "complimentary_grants", "complimentary_grant_receipts", "complimentary_grant_shops" })
        {
            migrationBuilder.Sql($"""
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql(Completeness);
    }

    private const string Hex64 = "~ '^[0-9a-f]{64}$'";

    private const string Tables = $"""
        CREATE TABLE public.complimentary_grant_scopes (
            account_id uuid PRIMARY KEY REFERENCES public.billing_accounts(id),
            revision bigint NOT NULL CHECK (revision > 0)
        );
        CREATE TABLE public.complimentary_grants (
            grant_id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            term_id uuid NOT NULL UNIQUE,
            basis text NOT NULL CHECK (basis = 'Complimentary'),
            starts_at timestamptz NOT NULL CHECK (isfinite(starts_at)),
            ends_at timestamptz NOT NULL CHECK (isfinite(ends_at) AND ends_at > starts_at),
            coverage_mode text NOT NULL,
            shop_capacity integer NOT NULL CHECK (shop_capacity BETWEEN 1 AND 100),
            member_allowance integer NOT NULL CHECK (member_allowance BETWEEN 1 AND 100000),
            policy_version text NOT NULL CHECK (policy_version = 'complimentary-access/1'),
            granted_version bigint NOT NULL CHECK (granted_version > 0),
            recorded_at timestamptz NOT NULL,
            CHECK ((coverage_mode = 'Independent' AND shop_capacity = 1) OR (coverage_mode = 'Grouped' AND shop_capacity > 1)),
            UNIQUE (account_id, granted_version),
            UNIQUE (grant_id, account_id, term_id, starts_at, ends_at),
            FOREIGN KEY (account_id, term_id, starts_at, ends_at)
                REFERENCES public.subscription_coverage_terms(account_id, term_id, starts_at, ends_at)
                DEFERRABLE INITIALLY DEFERRED
        );
        CREATE TABLE public.complimentary_grant_receipts (
            grant_id uuid PRIMARY KEY REFERENCES public.complimentary_grants(grant_id),
            account_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            admin_id uuid NOT NULL REFERENCES public.admin_identities(id),
            session_id uuid NOT NULL,
            reason varchar(500) NOT NULL CHECK (length(btrim(reason)) > 0 AND reason !~ '[[:cntrl:]]'),
            payload_digest text NOT NULL CHECK (payload_digest {Hex64}),
            source_hash text NOT NULL CHECK (source_hash {Hex64}),
            expected_version bigint NOT NULL CHECK (expected_version >= 0),
            recorded_at timestamptz NOT NULL,
            UNIQUE (account_id, operation_key)
        );
        CREATE TABLE public.complimentary_grant_shops (
            grant_id uuid NOT NULL,
            account_id uuid NOT NULL,
            term_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            affiliation_request_id uuid NOT NULL REFERENCES public.shop_affiliations(request_id),
            starts_at timestamptz NOT NULL,
            ends_at timestamptz NOT NULL,
            PRIMARY KEY (grant_id, shop_id),
            FOREIGN KEY (grant_id, account_id, term_id, starts_at, ends_at)
                REFERENCES public.complimentary_grants(grant_id, account_id, term_id, starts_at, ends_at)
        );
        CREATE INDEX complimentary_grant_shops_shop ON public.complimentary_grant_shops(shop_id, starts_at, ends_at);
        CREATE INDEX complimentary_grant_shops_term ON public.complimentary_grant_shops(term_id);
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Rollback);
}
