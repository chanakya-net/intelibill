using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261115230000_AddShopAffiliations")]
public sealed class AddShopAffiliations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "subscription_coverage_terms", "shop_affiliation_requests", "shop_affiliations", "shop_affiliation_inbox", "shop_affiliation_delivery_uses" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY account_read ON public.{table} FOR SELECT USING (
                    account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }

        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(CoverageGuard);
    }

    private const string Tables = """
        CREATE TABLE public.subscription_coverage_terms (
            term_id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            mode text NOT NULL,
            capacity integer NOT NULL,
            starts_at timestamptz NOT NULL,
            ends_at timestamptz NOT NULL CHECK (ends_at > starts_at),
            CHECK ((mode = 'Independent' AND capacity = 1) OR (mode = 'Grouped' AND capacity > 1)),
            UNIQUE (account_id, term_id)
        );

        CREATE TABLE public.shop_affiliation_requests (
            request_id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            account_name varchar(120) NOT NULL,
            shop_id uuid NOT NULL,
            requester_id uuid NOT NULL,
            requester_revision integer NOT NULL CHECK (requester_revision > 0),
            operation_key uuid NOT NULL,
            term_id uuid,
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            UNIQUE (account_id, requester_id, operation_key),
            UNIQUE (account_id, shop_id, request_id)
        );
        CREATE INDEX shop_affiliation_requests_account ON public.shop_affiliation_requests(account_id, created_at DESC);

        CREATE TABLE public.shop_affiliations (
            request_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            shop_id uuid NOT NULL UNIQUE,
            term_id uuid,
            event_id uuid NOT NULL UNIQUE,
            actor_id uuid NOT NULL,
            membership_id uuid NOT NULL,
            authority_version bigint NOT NULL CHECK (authority_version >= 0),
            shop_name text NOT NULL,
            owner_name text NOT NULL,
            accepted_at timestamptz NOT NULL,
            applied_at timestamptz NOT NULL CHECK (applied_at >= accepted_at),
            coverage_ends_at timestamptz,
            CHECK ((term_id IS NULL AND coverage_ends_at IS NULL)
                OR (term_id IS NOT NULL AND coverage_ends_at > applied_at)),
            FOREIGN KEY (account_id, shop_id, request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );
        CREATE INDEX shop_affiliations_term ON public.shop_affiliations(term_id);

        CREATE TABLE public.shop_affiliation_inbox (
            event_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            request_id uuid NOT NULL UNIQUE,
            digest text NOT NULL CHECK (length(digest) = 64),
            status text NOT NULL CHECK (status IN ('Applied', 'NeedsReview')),
            FOREIGN KEY (account_id, shop_id, request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id)
        );

        CREATE TABLE public.shop_affiliation_delivery_uses (
            replay_id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            expires_at timestamptz NOT NULL
        );
        """;

    private const string Policies = """
        CREATE POLICY owner_request_read ON public.shop_affiliation_requests FOR SELECT USING (
            shop_id = nullif(current_setting('app.affiliation_shop_id', true), '')::uuid
            AND request_id = nullif(current_setting('app.affiliation_request_id', true), '')::uuid);
        CREATE POLICY owner_applied_read ON public.shop_affiliations FOR SELECT USING (
            shop_id = nullif(current_setting('app.affiliation_shop_id', true), '')::uuid
            AND request_id = nullif(current_setting('app.affiliation_request_id', true), '')::uuid);
        CREATE POLICY owner_inbox_read ON public.shop_affiliation_inbox FOR SELECT USING (
            shop_id = nullif(current_setting('app.affiliation_shop_id', true), '')::uuid
            AND request_id = nullif(current_setting('app.affiliation_request_id', true), '')::uuid);
        CREATE POLICY owner_term_read ON public.subscription_coverage_terms FOR SELECT USING (
            EXISTS (SELECT FROM public.shop_affiliation_requests r WHERE r.term_id = subscription_coverage_terms.term_id
                AND r.request_id = nullif(current_setting('app.affiliation_request_id', true), '')::uuid
                AND r.shop_id = nullif(current_setting('app.affiliation_shop_id', true), '')::uuid));
        CREATE POLICY transport_insert ON public.shop_affiliation_delivery_uses FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
        CREATE POLICY request_insert ON public.shop_affiliation_requests FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND requester_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid);
        CREATE POLICY affiliation_insert ON public.shop_affiliations FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
        CREATE POLICY inbox_insert ON public.shop_affiliation_inbox FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
        """;

    private const string CoverageGuard = """
        CREATE FUNCTION public.guard_shop_affiliation_coverage() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended('b' || NEW.account_id::text, 0));
            IF NOT EXISTS (SELECT FROM public.shop_affiliation_requests r
                    WHERE r.account_id = NEW.account_id AND r.shop_id = NEW.shop_id AND r.request_id = NEW.request_id
                    AND r.term_id IS NOT DISTINCT FROM NEW.term_id
                    AND r.created_at <= NEW.accepted_at AND NEW.accepted_at < r.expires_at)
                OR (NEW.term_id IS NOT NULL AND NOT EXISTS (
                    SELECT FROM public.subscription_coverage_terms t
                    WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id
                    AND t.starts_at <= NEW.applied_at AND NEW.coverage_ends_at = t.ends_at
                    AND t.capacity > (SELECT count(*) FROM public.shop_affiliations a WHERE a.term_id = NEW.term_id)
                )) THEN
                RAISE EXCEPTION 'Shop affiliation selection is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER shop_affiliation_coverage_guard BEFORE INSERT ON public.shop_affiliations
            FOR EACH ROW EXECUTE FUNCTION public.guard_shop_affiliation_coverage();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.subscription_coverage_terms, public.shop_affiliation_requests,
            public.shop_affiliations, public.shop_affiliation_inbox IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.subscription_coverage_terms NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.shop_affiliation_requests NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.shop_affiliation_requests) OR EXISTS (SELECT FROM public.subscription_coverage_terms) THEN
                RAISE EXCEPTION 'Shop affiliation evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.shop_affiliation_delivery_uses, public.shop_affiliation_inbox, public.shop_affiliations,
            public.shop_affiliation_requests, public.subscription_coverage_terms;
        DROP FUNCTION public.guard_shop_affiliation_coverage();
        """);
}
