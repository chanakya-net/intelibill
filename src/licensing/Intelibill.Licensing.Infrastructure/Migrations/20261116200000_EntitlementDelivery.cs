using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261116200000_EntitlementDelivery")]
public sealed class EntitlementDelivery : Migration
{
    private const string AccountScope = "account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string ReplayScope = "replay_id = nullif(current_setting('app.entitlement_replay_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Create);
        migrationBuilder.Sql("""
            CREATE TABLE public.entitlement_account_heads (
                account_id uuid PRIMARY KEY REFERENCES public.billing_accounts(id),
                revision bigint NOT NULL CHECK (revision > 0),
                upstream_verified_at timestamptz NOT NULL CHECK (isfinite(upstream_verified_at)),
                restriction text NOT NULL CHECK (restriction IN ('Unrestricted', 'Trading', 'Security')),
                released_revision bigint CHECK (released_revision IS NULL OR
                    (restriction = 'Unrestricted' AND released_revision > 0 AND released_revision < revision))
            );
            CREATE TABLE public.entitlement_shop_heads (
                shop_id uuid PRIMARY KEY,
                account_id uuid NOT NULL REFERENCES public.entitlement_account_heads(account_id),
                revision bigint NOT NULL CHECK (revision > 0),
                next_verification_at timestamptz NOT NULL CHECK (isfinite(next_verification_at))
            );
            CREATE INDEX entitlement_shop_heads_due ON public.entitlement_shop_heads(next_verification_at, shop_id);
            CREATE TABLE public.entitlement_snapshots (
                event_id uuid PRIMARY KEY,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                affiliation_request_id uuid NOT NULL,
                account_revision bigint NOT NULL CHECK (account_revision > 0),
                shop_revision bigint NOT NULL CHECK (shop_revision > 0),
                UNIQUE (shop_id, shop_revision),
                FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id)
            );
            CREATE TABLE public.entitlement_merchant_inbox (
                event_id uuid NOT NULL,
                kind text NOT NULL CHECK (kind IN ('Acknowledgement', 'Reconciliation')),
                account_id uuid NOT NULL,
                digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
                payload bytea NOT NULL CHECK (octet_length(payload) BETWEEN 1 AND 8192),
                assertion text NOT NULL CHECK (length(assertion) BETWEEN 1 AND 4096),
                public_key text NOT NULL CHECK (length(public_key) BETWEEN 1 AND 2048),
                PRIMARY KEY (event_id, kind)
            );
            CREATE TABLE public.entitlement_request_uses (
                replay_id uuid PRIMARY KEY,
                expires_at timestamptz NOT NULL CHECK (isfinite(expires_at))
            );
            """);
        foreach (var table in new[] { "entitlement_account_heads", "entitlement_shop_heads", "entitlement_snapshots",
            "entitlement_messages", "entitlement_outbox", "entitlement_merchant_inbox" })
            migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope(table, AccountScope));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope("entitlement_request_uses", ReplayScope));
        foreach (var table in new[] { "entitlement_snapshots", "entitlement_merchant_inbox" })
            migrationBuilder.Sql($"""
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
                """);
        foreach (var table in new[] { "entitlement_messages", "entitlement_outbox", "entitlement_shop_heads", "shop_affiliations" })
            migrationBuilder.Sql(LeasedHttpOutboxSchema.WorkerAccess(table));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Workers);
        migrationBuilder.Sql(VerificationScan);
        migrationBuilder.Sql(RevisionGuards);
    }

    private static string VerificationScan => $$"""
        CREATE FUNCTION public.due_entitlement_verification(p_now timestamptz)
        RETURNS TABLE (account_id uuid, shop_id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT a.account_id, a.shop_id FROM public.shop_affiliations a
            LEFT JOIN public.entitlement_shop_heads h USING (account_id, shop_id)
            WHERE (h.shop_id IS NULL OR h.next_verification_at <= p_now) AND {{LeasedHttpOutboxSchema.Unscoped}}
            ORDER BY h.next_verification_at NULLS FIRST, a.shop_id LIMIT 20
        $$;
        REVOKE ALL ON FUNCTION public.due_entitlement_verification(timestamptz) FROM PUBLIC;
        """;

    private const string RevisionGuards = """
        CREATE FUNCTION public.guard_entitlement_source_revision() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.revision <> OLD.revision + 1 THEN
                RAISE EXCEPTION 'Entitlement source revisions must advance by one' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_source_account_revision BEFORE UPDATE ON public.entitlement_account_heads
            FOR EACH ROW EXECUTE FUNCTION public.guard_entitlement_source_revision();
        CREATE TRIGGER guard_source_shop_revision BEFORE UPDATE ON public.entitlement_shop_heads
            FOR EACH ROW EXECUTE FUNCTION public.guard_entitlement_source_revision();
        CREATE TRIGGER retain_source_account_head BEFORE DELETE ON public.entitlement_account_heads
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_source_shop_head BEFORE DELETE ON public.entitlement_shop_heads
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_source_shop_head_table BEFORE TRUNCATE ON public.entitlement_shop_heads
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.entitlement_messages, public.entitlement_account_heads, public.entitlement_merchant_inbox
                IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.entitlement_messages NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.entitlement_account_heads NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.entitlement_merchant_inbox NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.entitlement_messages) OR EXISTS (SELECT FROM public.entitlement_account_heads)
                    OR EXISTS (SELECT FROM public.entitlement_merchant_inbox)
                    THEN RAISE EXCEPTION 'Retained entitlement evidence prevents downgrade' USING ERRCODE = '55000'; END IF;
            END $$;
            DROP FUNCTION public.due_entitlement_verification(timestamptz);
            DROP POLICY entitlement_worker ON public.shop_affiliations;
            DROP TABLE public.entitlement_request_uses, public.entitlement_merchant_inbox, public.entitlement_snapshots,
                public.entitlement_shop_heads, public.entitlement_account_heads;
            DROP FUNCTION public.guard_entitlement_source_revision();
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Drop);
    }
}
