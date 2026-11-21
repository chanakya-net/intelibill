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
                opens_shop boolean NOT NULL,
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
        migrationBuilder.Sql(PublisherCutover);
        migrationBuilder.Sql(Guards);
    }

    private const string Guards = """
        CREATE FUNCTION public.guard_closure_reopening() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT public.reopening_publishers_ready() THEN
                RAISE EXCEPTION 'Reopening publisher cutover is required' USING ERRCODE = '55000';
            END IF;
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
            ) OR NEW.opens_shop <> (NOT EXISTS (
                SELECT FROM public.active_operational_closure_targets t
                WHERE t.account_id = NEW.account_id AND t.shop_id = NEW.shop_id
                    AND t.affiliation_request_id = NEW.affiliation_request_id AND t.closure_id <> NEW.closure_id
            )) THEN
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

    private const string PublisherCutover = """
        ALTER TABLE public.entitlement_snapshots
            ADD COLUMN closure_revision bigint CHECK (closure_revision > 1 AND closure_revision <= shop_revision),
            ADD COLUMN publisher_protocol smallint NOT NULL DEFAULT 1 CHECK (publisher_protocol IN (1, 2));
        CREATE INDEX entitlement_snapshots_closure_cycle
            ON public.entitlement_snapshots (shop_id, closure_revision, shop_revision DESC)
            WHERE closure_revision IS NOT NULL;
        CREATE VIEW public.applied_entitlement_shop_receipts WITH (security_invoker = true) AS
            SELECT s.account_id, s.shop_id, s.affiliation_request_id, s.shop_revision, s.closure_revision,
                (receipt.body->>'appliedAt')::timestamptz AS applied_at
            FROM public.entitlement_snapshots s
            JOIN public.entitlement_merchant_inbox ack ON ack.event_id = s.event_id
                AND ack.account_id = s.account_id AND ack.kind = 'Acknowledgement'
            CROSS JOIN LATERAL (SELECT convert_from(ack.payload, 'UTF8')::jsonb AS body) receipt
            WHERE receipt.body->>'disposition' = 'Applied'
                AND (receipt.body->>'appliedShopRevision')::bigint = s.shop_revision;
        CREATE INDEX operational_closure_reopenings_shop_history
            ON public.operational_closure_reopenings (account_id, shop_id, affiliation_request_id, version DESC);

        CREATE TABLE public.reopening_publisher_cutover (
            singleton boolean PRIMARY KEY CHECK (singleton),
            enabled boolean NOT NULL
        );
        INSERT INTO public.reopening_publisher_cutover VALUES (true, false);
        CREATE FUNCTION public.guard_reopening_cutover() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
        BEGIN
            IF TG_OP <> 'UPDATE' OR NOT NEW.enabled OR NOT pg_has_role(current_user,
                (SELECT relowner FROM pg_class WHERE oid = 'public.reopening_publisher_cutover'::regclass), 'USAGE')
            THEN RAISE EXCEPTION 'Reopening cutover requires the schema owner' USING ERRCODE = '42501';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_cutover_row BEFORE INSERT OR UPDATE OR DELETE ON public.reopening_publisher_cutover
            FOR EACH ROW EXECUTE FUNCTION public.guard_reopening_cutover();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.reopening_publisher_cutover
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();

        CREATE FUNCTION public.reopening_publishers_ready() RETURNS boolean
        LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
            SELECT enabled FROM public.reopening_publisher_cutover WHERE singleton FOR SHARE
        $$;
        CREATE FUNCTION public.guard_entitlement_publisher() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
        BEGIN
            IF NEW.publisher_protocol <> 2 AND public.reopening_publishers_ready() THEN
                RAISE EXCEPTION 'Retire the legacy entitlement publisher' USING ERRCODE = '55000';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_publisher BEFORE INSERT ON public.entitlement_snapshots
            FOR EACH ROW EXECUTE FUNCTION public.guard_entitlement_publisher();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.operational_closure_reopenings IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.operational_closure_reopenings NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.operational_closure_reopenings) THEN
                RAISE EXCEPTION 'Retained reopening evidence prevents downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER guard_publisher ON public.entitlement_snapshots;
        DROP FUNCTION public.guard_entitlement_publisher();
        DROP FUNCTION public.reopening_publishers_ready();
        DROP TABLE public.reopening_publisher_cutover;
        DROP FUNCTION public.guard_reopening_cutover();
        DROP VIEW public.applied_entitlement_shop_receipts;
        ALTER TABLE public.entitlement_snapshots DROP COLUMN closure_revision, DROP COLUMN publisher_protocol;
        DROP VIEW public.active_operational_closure_targets;
        DROP TABLE public.operational_closure_reopenings;
        DROP FUNCTION public.guard_closure_reopening();
        """);
}
