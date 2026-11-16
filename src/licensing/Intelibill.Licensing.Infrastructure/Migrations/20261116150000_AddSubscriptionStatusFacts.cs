using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// Append-only supplement to the SQL-owned coverage terms: the frozen basis, calendar anchor, policy versions,
/// allowance and selected commercial amount a status evaluation needs. No backfill, no seed, no runtime insert
/// policy (the owning lifecycle writers add theirs). Adds narrow transaction-local status reads by shop.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261116150000_AddSubscriptionStatusFacts")]
public sealed class AddSubscriptionStatusFacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Table);
        migrationBuilder.Sql(Security);
    }

    private const string Table = """
        CREATE TABLE public.subscription_term_facts (
            term_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            basis text NOT NULL CHECK (basis IN ('Paid', 'Trial', 'Complimentary', 'Temporary', 'Transition')),
            billing_period text NOT NULL CHECK (billing_period IN ('Month', 'Year')),
            anchor_local timestamp without time zone NOT NULL CHECK (anchor_local = date_trunc('second', anchor_local)),
            time_zone text NOT NULL CHECK (time_zone = 'Asia/Kolkata'),
            period_ordinal integer NOT NULL CHECK (period_ordinal >= 0),
            calendar_version varchar(40) NOT NULL CHECK (length(calendar_version) > 0),
            access_policy_version varchar(40) NOT NULL CHECK (length(access_policy_version) > 0),
            member_allowance integer NOT NULL CHECK (member_allowance BETWEEN 1 AND 100000),
            price_paise bigint CHECK (price_paise BETWEEN 0 AND 1000000000000),
            currency text CHECK (currency = 'INR'),
            plan_version_id uuid REFERENCES public.plan_versions(id),
            source_reference varchar(80) NOT NULL CHECK (length(source_reference) > 0),
            development_fixture boolean NOT NULL,
            recorded_at timestamptz NOT NULL,
            CHECK ((price_paise IS NULL) = (currency IS NULL)),
            CHECK (basis <> 'Paid' OR price_paise IS NOT NULL),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );
        CREATE INDEX subscription_term_facts_account ON public.subscription_term_facts(account_id);
        """;

    // app.status_shop_id is set only inside a verified shop-status transaction, after the merchant checked live
    // shop membership and the signed path named this shop. It reveals that shop's own coverage and nothing else.
    private const string Security = """
        ALTER TABLE public.subscription_term_facts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.subscription_term_facts FORCE ROW LEVEL SECURITY;
        CREATE POLICY account_read ON public.subscription_term_facts FOR SELECT USING (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
        CREATE POLICY status_shop_read ON public.subscription_term_facts FOR SELECT USING (
            EXISTS (SELECT FROM public.shop_affiliations a WHERE a.term_id = subscription_term_facts.term_id
                AND a.shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid));
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.subscription_term_facts
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.subscription_term_facts
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();

        CREATE POLICY status_shop_read ON public.shop_affiliations FOR SELECT USING (
            shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid);
        CREATE POLICY status_shop_read ON public.subscription_coverage_terms FOR SELECT USING (
            EXISTS (SELECT FROM public.shop_affiliations a WHERE a.term_id = subscription_coverage_terms.term_id
                AND a.shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid));
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.subscription_term_facts IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.subscription_term_facts NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.subscription_term_facts) THEN
                RAISE EXCEPTION 'Subscription term facts exist' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP POLICY status_shop_read ON public.subscription_coverage_terms;
        DROP POLICY status_shop_read ON public.shop_affiliations;
        DROP TABLE public.subscription_term_facts;
        """);
}
