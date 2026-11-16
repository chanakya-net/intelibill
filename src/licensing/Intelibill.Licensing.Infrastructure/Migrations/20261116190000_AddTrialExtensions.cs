using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261116190000_AddTrialExtensions")]
public sealed class AddTrialExtensions : Migration
{
    private const string Admin = "nullif(current_setting('app.trial_extension_admin_id', true), '')::uuid";
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Shop = "nullif(current_setting('app.status_shop_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "trial_extensions", "trial_extension_receipts" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(Guards);
    }

    private const string Tables = """
        CREATE TABLE public.trial_extensions (
            extension_id uuid PRIMARY KEY,
            shop_id uuid NOT NULL REFERENCES public.trial_history(shop_id),
            account_id uuid NOT NULL,
            term_id uuid NOT NULL,
            revision bigint NOT NULL CHECK (revision > 0),
            previous_ends_at timestamptz NOT NULL CHECK (isfinite(previous_ends_at)),
            ends_at timestamptz NOT NULL CHECK (isfinite(ends_at) AND ends_at > previous_ends_at AND ends_at > recorded_at
                AND ends_at < '9999-01-01 00:00:00+00'::timestamptz),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            policy_version text NOT NULL CHECK (policy_version = 'trial-extension/1'),
            UNIQUE (shop_id, revision),
            UNIQUE (extension_id, account_id, shop_id)
        );
        CREATE TABLE public.trial_extension_receipts (
            extension_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            admin_id uuid NOT NULL REFERENCES public.admin_identities(id),
            session_id uuid NOT NULL,
            shop_name text NOT NULL,
            reason varchar(500) NOT NULL CHECK (length(btrim(reason)) > 0 AND reason !~ '[[:cntrl:]]'),
            review_reference varchar(80) NOT NULL CHECK (length(btrim(review_reference)) > 0 AND review_reference !~ '[[:cntrl:]]'),
            payload_digest text NOT NULL CHECK (payload_digest ~ '^[0-9a-f]{64}$'),
            source_hash text NOT NULL CHECK (source_hash ~ '^[0-9a-f]{64}$'),
            expected_version bigint NOT NULL CHECK (expected_version >= 0),
            account_trial_cap_reached boolean NOT NULL,
            UNIQUE (account_id, operation_key),
            FOREIGN KEY (extension_id, account_id, shop_id) REFERENCES public.trial_extensions(extension_id, account_id, shop_id)
        );
        """;

    private const string Policies = $"""
        CREATE POLICY scoped_extension_read ON public.trial_extensions FOR SELECT
            USING (account_id = {Account} OR shop_id = {Shop});
        CREATE POLICY provider_extension_insert ON public.trial_extensions FOR INSERT
            WITH CHECK (account_id = {Account} AND {Admin} IS NOT NULL);
        CREATE POLICY provider_extension_receipt_read ON public.trial_extension_receipts FOR SELECT
            USING (account_id = {Account} AND admin_id = {Admin});
        CREATE POLICY provider_extension_receipt_insert ON public.trial_extension_receipts FOR INSERT
            WITH CHECK (account_id = {Account} AND admin_id = {Admin});
        """;

    private const string Guards = """
        CREATE FUNCTION public.guard_trial_extension() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE original_end timestamptz; last_end timestamptz; last_revision bigint;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('t' || NEW.shop_id::text, 0));
            SELECT h.ends_at INTO original_end FROM public.trial_history h
            JOIN public.shop_affiliations a ON a.shop_id = h.shop_id AND a.account_id = h.account_id
            WHERE h.shop_id = NEW.shop_id AND h.account_id = NEW.account_id AND h.term_id = NEW.term_id AND a.term_id IS NULL;
            SELECT e.ends_at, e.revision INTO last_end, last_revision FROM public.trial_extensions e
            WHERE e.shop_id = NEW.shop_id AND e.account_id = NEW.account_id ORDER BY e.revision DESC LIMIT 1;
            IF original_end IS NULL OR NEW.revision <> coalesce(last_revision, 0) + 1
                OR NEW.previous_ends_at <> coalesce(last_end, original_end) THEN
                RAISE EXCEPTION 'Trial extension must advance its original trial' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER trial_extension_chain BEFORE INSERT ON public.trial_extensions
            FOR EACH ROW EXECUTE FUNCTION public.guard_trial_extension();

        CREATE FUNCTION public.require_trial_extension_receipt() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.trial_extension_receipts r WHERE r.extension_id = NEW.extension_id
                AND r.expected_version = NEW.revision - 1) THEN
                RAISE EXCEPTION 'Trial extension requires a matching receipt' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER trial_extension_receipt AFTER INSERT ON public.trial_extensions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_trial_extension_receipt();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.trial_extensions, public.trial_extension_receipts IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.trial_extensions NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.trial_extensions) THEN
                RAISE EXCEPTION 'Trial extension evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.trial_extension_receipts, public.trial_extensions;
        DROP FUNCTION public.require_trial_extension_receipt();
        DROP FUNCTION public.guard_trial_extension();
        """);
}
