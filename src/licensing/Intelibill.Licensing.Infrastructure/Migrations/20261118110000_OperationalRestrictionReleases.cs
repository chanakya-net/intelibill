using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261118110000_OperationalRestrictionReleases")]
public sealed class OperationalRestrictionReleases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_account_heads ADD COLUMN previous_release_revision bigint
                CHECK (previous_release_revision IS NULL OR (restriction IN ('Trading', 'Security')
                    AND previous_release_revision > 0 AND previous_release_revision < revision));
            ALTER TABLE public.operational_restrictions DROP CONSTRAINT operational_restrictions_account_id_shop_id_key;
            CREATE TABLE public.operational_restriction_releases (
                id uuid PRIMARY KEY,
                restriction_id uuid NOT NULL UNIQUE,
                account_id uuid NOT NULL,
                version bigint NOT NULL CHECK (version > 1),
                prior_revision bigint NOT NULL CHECK (prior_revision > 0),
                released_at timestamptz NOT NULL CHECK (isfinite(released_at)),
                released_by uuid NOT NULL REFERENCES public.admin_identities(id),
                session_id uuid NOT NULL,
                mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
                reason text NOT NULL CHECK (length(trim(reason)) BETWEEN 1 AND 1000),
                operation_key uuid NOT NULL,
                payload_digest text NOT NULL CHECK (payload_digest ~ '^[0-9a-f]{64}$'),
                UNIQUE (account_id, operation_key),
                UNIQUE (id, account_id),
                FOREIGN KEY (restriction_id, account_id) REFERENCES public.operational_restrictions(id, account_id)
            );
            CREATE TABLE public.operational_restriction_release_deliveries (
                release_id uuid NOT NULL,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                event_id uuid NOT NULL UNIQUE,
                PRIMARY KEY (release_id, shop_id),
                FOREIGN KEY (release_id, account_id) REFERENCES public.operational_restriction_releases(id, account_id),
                FOREIGN KEY (event_id, account_id, shop_id) REFERENCES public.entitlement_messages(event_id, account_id, shop_id)
            );
            CREATE VIEW public.active_operational_restrictions WITH (security_invoker = true) AS
                SELECT r.* FROM public.operational_restrictions r WHERE NOT EXISTS
                    (SELECT FROM public.operational_restriction_releases l WHERE l.restriction_id = r.id);
            """);
        foreach (var table in new[] { "operational_restriction_releases", "operational_restriction_release_deliveries" })
        {
            migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope(table,
                "account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid"));
            migrationBuilder.Sql($"""
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
                """);
        }

        migrationBuilder.Sql(TransitionGuards);
        migrationBuilder.Sql(OperationalRestrictions.FreshStartGuard(ScheduledRestriction("active_operational_restrictions")));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.operational_restrictions, public.operational_restriction_releases,
                public.entitlement_account_heads IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.operational_restriction_releases NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.entitlement_account_heads NO FORCE ROW LEVEL SECURITY;
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM public.operational_restriction_releases)
                    OR EXISTS (SELECT FROM public.entitlement_account_heads WHERE previous_release_revision IS NOT NULL) THEN
                    RAISE EXCEPTION 'Operational restriction release evidence must be retained' USING ERRCODE = '55000';
                END IF;
            END $$;
            """);
        migrationBuilder.Sql(OperationalRestrictions.FreshStartGuard(ScheduledRestriction("operational_restrictions")));
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_account_heads DROP COLUMN previous_release_revision;
            ALTER TABLE public.entitlement_account_heads FORCE ROW LEVEL SECURITY;
            DROP TRIGGER sequence_restriction ON public.operational_restrictions;
            DROP VIEW public.active_operational_restrictions;
            DROP TABLE public.operational_restriction_release_deliveries, public.operational_restriction_releases;
            DROP FUNCTION public.guard_suspension_apply(), public.guard_suspension_release();
            ALTER TABLE public.operational_restrictions ADD UNIQUE NULLS NOT DISTINCT (account_id, shop_id);
            """);
    }

    private static string ScheduledRestriction(string table) => $"""
        OR EXISTS (SELECT FROM public.{table} r
            WHERE r.account_id = h.account_id AND r.shop_id IS NULL AND r.mode = h.restriction
                AND r.recorded_at <= NEW.ready_at AND NEW.ready_at < r.effective_at)
        """;

    private const string TransitionGuards = """
        CREATE FUNCTION public.guard_suspension_apply() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE next_version bigint;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            SELECT COALESCE(max(l.version), 0) + 1 INTO next_version
            FROM public.operational_restrictions r LEFT JOIN public.operational_restriction_releases l ON l.restriction_id = r.id
            WHERE r.account_id = NEW.account_id AND r.shop_id IS NOT DISTINCT FROM NEW.shop_id;
            IF NEW.version <> next_version OR EXISTS (
                SELECT FROM public.active_operational_restrictions r
                WHERE r.account_id = NEW.account_id AND r.shop_id IS NOT DISTINCT FROM NEW.shop_id
            ) THEN
                RAISE EXCEPTION 'Suspension requires next scope version' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE FUNCTION public.guard_suspension_release() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT EXISTS (
                SELECT FROM public.active_operational_restrictions r
                WHERE r.id = NEW.restriction_id AND r.account_id = NEW.account_id
                    AND NEW.version = r.version + 1 AND NEW.released_at >= r.recorded_at
                    AND NEW.prior_revision = CASE WHEN r.shop_id IS NULL THEN
                        (SELECT revision FROM public.entitlement_account_heads h WHERE h.account_id = r.account_id)
                    ELSE (SELECT revision FROM public.entitlement_shop_heads h
                        WHERE h.account_id = r.account_id AND h.shop_id = r.shop_id) END
            ) THEN
                RAISE EXCEPTION 'Release requires current suspension and revision' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER sequence_restriction BEFORE INSERT ON public.operational_restrictions
            FOR EACH ROW EXECUTE FUNCTION public.guard_suspension_apply();
        CREATE TRIGGER sequence_release BEFORE INSERT ON public.operational_restriction_releases
            FOR EACH ROW EXECUTE FUNCTION public.guard_suspension_release();
        """;
}
