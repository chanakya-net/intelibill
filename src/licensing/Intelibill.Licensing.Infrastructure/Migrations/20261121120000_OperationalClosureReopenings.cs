using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261121120000_OperationalClosureReopenings")]
public sealed class OperationalClosureReopenings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public.operational_closure_reopenings (
                id uuid PRIMARY KEY,
                closure_id uuid NOT NULL,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                affiliation_request_id uuid NOT NULL,
                version bigint NOT NULL CHECK (version > 1),
                prior_revision bigint NOT NULL CHECK (prior_revision > 0),
                term_id uuid NOT NULL,
                basis text NOT NULL CHECK (basis IN ('Paid', 'Complimentary', 'Temporary', 'Transition')),
                starts_at timestamptz NOT NULL CHECK (isfinite(starts_at)),
                paid_through timestamptz NOT NULL CHECK (isfinite(paid_through)),
                access_ends_at timestamptz NOT NULL CHECK (isfinite(access_ends_at)),
                impact_digest text NOT NULL CHECK (impact_digest ~ '^[0-9a-f]{64}$'),
                owner_consent_action text NOT NULL CHECK (owner_consent_action = 'ReopenShop'),
                owner_consent_user_id uuid NOT NULL,
                owner_consent_at timestamptz NOT NULL CHECK (isfinite(owner_consent_at)),
                owner_consent_reference text NOT NULL CHECK (length(trim(owner_consent_reference)) BETWEEN 1 AND 120),
                owner_consent_evidence text NOT NULL CHECK (length(trim(owner_consent_evidence)) BETWEEN 1 AND 2000),
                reason text NOT NULL CHECK (length(trim(reason)) BETWEEN 1 AND 1000),
                evaluated_at timestamptz NOT NULL CHECK (isfinite(evaluated_at)),
                reopened_at timestamptz NOT NULL CHECK (isfinite(reopened_at)),
                reopened_by uuid NOT NULL REFERENCES public.admin_identities(id),
                session_id uuid NOT NULL,
                mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
                operation_key uuid NOT NULL,
                payload_digest text NOT NULL CHECK (payload_digest ~ '^[0-9a-f]{64}$'),
                event_id uuid NOT NULL UNIQUE,
                CHECK (starts_at <= evaluated_at AND evaluated_at < paid_through
                    AND paid_through <= access_ends_at AND owner_consent_at <= reopened_at),
                UNIQUE (account_id, operation_key),
                UNIQUE (closure_id, shop_id),
                UNIQUE (account_id, shop_id, owner_consent_reference),
                FOREIGN KEY (closure_id, shop_id) REFERENCES public.operational_closure_targets(closure_id, shop_id),
                FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
                FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                    REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
                FOREIGN KEY (event_id, account_id, shop_id)
                    REFERENCES public.entitlement_messages(event_id, account_id, shop_id) DEFERRABLE INITIALLY DEFERRED
            );
            CREATE VIEW public.active_operational_closure_targets WITH (security_invoker = true) AS
                SELECT t.* FROM public.operational_closure_targets t WHERE NOT EXISTS (
                    SELECT FROM public.operational_closure_reopenings r
                    WHERE r.closure_id = t.closure_id AND r.shop_id = t.shop_id);
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope("operational_closure_reopenings",
            "account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid"));
        migrationBuilder.Sql(Guards);
    }

    private const string Guards = """
        CREATE FUNCTION public.guard_closure_reopening() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT EXISTS (
                SELECT FROM public.active_operational_closure_targets t
                JOIN public.operational_closures c ON c.id = t.closure_id AND c.account_id = t.account_id
                JOIN public.shop_affiliations a ON a.account_id = t.account_id AND a.shop_id = t.shop_id
                    AND a.request_id = t.affiliation_request_id
                JOIN public.entitlement_shop_heads h ON h.shop_id = t.shop_id AND h.account_id = t.account_id
                WHERE t.closure_id = NEW.closure_id AND t.account_id = NEW.account_id AND t.shop_id = NEW.shop_id
                    AND t.affiliation_request_id = NEW.affiliation_request_id AND h.revision = NEW.prior_revision
                    AND NEW.owner_consent_at > c.recorded_at
            ) OR NEW.version <> 1 + (
                SELECT count(*) FROM public.operational_closure_targets t
                WHERE t.account_id = NEW.account_id AND t.shop_id = NEW.shop_id
            ) + (
                SELECT count(*) FROM public.operational_closure_reopenings r
                WHERE r.account_id = NEW.account_id AND r.shop_id = NEW.shop_id
            ) THEN
                RAISE EXCEPTION 'Reopening requires the active closure and its current entitlement head' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_reopening BEFORE INSERT ON public.operational_closure_reopenings
            FOR EACH ROW EXECUTE FUNCTION public.guard_closure_reopening();
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.operational_closure_reopenings
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.operational_closure_reopenings
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.operational_closure_reopenings IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.operational_closure_reopenings NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.operational_closure_reopenings) THEN
                RAISE EXCEPTION 'Retained reopening evidence prevents downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP VIEW public.active_operational_closure_targets;
        DROP TABLE public.operational_closure_reopenings;
        DROP FUNCTION public.guard_closure_reopening();
        """);
}
