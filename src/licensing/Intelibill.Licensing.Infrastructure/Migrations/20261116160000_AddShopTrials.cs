using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>Retain history alongside the existing SQL-owned affiliation and coverage records.</summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261116160000_AddShopTrials")]
public sealed class AddShopTrials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        foreach (var table in new[] { "shop_trial_delivery_uses", "shop_trial_inbox", "trial_history" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY trial_shop_read ON public.{table} FOR SELECT USING (
                    shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid);
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }
        migrationBuilder.Sql(WritePolicies);
        migrationBuilder.Sql(ReadPolicies);
        migrationBuilder.Sql(ReserveCoverage);
    }

    private const string Tables = """
        CREATE TABLE public.shop_trial_delivery_uses (
            replay_id uuid PRIMARY KEY,
            event_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
            expires_at timestamptz NOT NULL,
            UNIQUE (replay_id, event_id, shop_id, digest)
        );
        CREATE TABLE public.shop_trial_inbox (
            event_id uuid PRIMARY KEY,
            shop_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            account_id uuid REFERENCES public.billing_accounts(id),
            term_id uuid UNIQUE,
            replay_id uuid NOT NULL UNIQUE,
            digest text NOT NULL,
            status text NOT NULL,
            reason varchar(40),
            UNIQUE (shop_id, actor_id, operation_key),
            FOREIGN KEY (replay_id, event_id, shop_id, digest)
                REFERENCES public.shop_trial_delivery_uses(replay_id, event_id, shop_id, digest),
            CHECK ((status = 'Applied' AND account_id IS NOT NULL AND term_id IS NOT NULL AND reason IS NULL)
                OR (status = 'NeedsReview' AND term_id IS NULL AND reason IS NOT NULL))
        );
        ALTER TABLE public.subscription_coverage_terms ADD CONSTRAINT coverage_term_interval_identity
            UNIQUE (account_id, term_id, starts_at, ends_at);
        CREATE TABLE public.trial_history (
            shop_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            term_id uuid NOT NULL UNIQUE,
            event_id uuid NOT NULL UNIQUE REFERENCES public.shop_trial_inbox(event_id),
            operation_key uuid NOT NULL,
            actor_id uuid NOT NULL,
            membership_id uuid NOT NULL,
            authority_version bigint NOT NULL CHECK (authority_version >= 0),
            policy_id uuid NOT NULL REFERENCES public.trial_policy_versions(id),
            policy_revision integer NOT NULL CHECK (policy_revision > 0),
            terms_version text NOT NULL CHECK (terms_version ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'),
            member_cap integer NOT NULL CHECK (member_cap BETWEEN 1 AND 100000),
            account_trial_cap integer NOT NULL CHECK (account_trial_cap BETWEEN 1 AND 100000),
            active_members integer NOT NULL CHECK (active_members BETWEEN 1 AND member_cap),
            starts_at timestamptz NOT NULL,
            ends_at timestamptz NOT NULL CHECK (ends_at - starts_at = interval '336 hours'),
            applied_at timestamptz NOT NULL CHECK (applied_at >= starts_at AND applied_at < ends_at),
            consent_digest text NOT NULL CHECK (consent_digest ~ '^[0-9a-f]{64}$'),
            development_fixture boolean NOT NULL,
            computation_version text NOT NULL CHECK (computation_version = 'shop-trial/1'),
            FOREIGN KEY (account_id, term_id, starts_at, ends_at)
                REFERENCES public.subscription_coverage_terms(account_id, term_id, starts_at, ends_at)
        );
        CREATE INDEX trial_history_account_interval ON public.trial_history(account_id, ends_at, starts_at);
        ALTER TABLE public.shop_trial_inbox ADD CONSTRAINT applied_trial_has_history
            FOREIGN KEY (term_id) REFERENCES public.trial_history(term_id) DEFERRABLE INITIALLY DEFERRED;
        """;

    private const string WritePolicies = """
        CREATE POLICY trial_delivery ON public.shop_trial_delivery_uses FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid
            AND replay_id = nullif(current_setting('app.trial_delivery_id', true), '')::uuid);
        CREATE POLICY trial_inbox ON public.shop_trial_inbox FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid
            AND replay_id = nullif(current_setting('app.trial_delivery_id', true), '')::uuid
            AND account_id IS NOT DISTINCT FROM (SELECT a.account_id FROM public.shop_affiliations a
                WHERE a.shop_id = shop_trial_inbox.shop_id));
        CREATE POLICY trial_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (
            mode = 'Independent' AND capacity = 1 AND EXISTS (
                SELECT FROM public.shop_trial_inbox i
                WHERE i.replay_id = nullif(current_setting('app.trial_delivery_id', true), '')::uuid
                    AND i.account_id = subscription_coverage_terms.account_id
                    AND i.term_id = subscription_coverage_terms.term_id AND i.status = 'Applied'));
        CREATE POLICY trial_history_insert ON public.trial_history FOR INSERT WITH CHECK (EXISTS (
            SELECT FROM public.shop_trial_inbox i
            JOIN public.shop_affiliations a ON a.shop_id = i.shop_id AND a.account_id = i.account_id
            JOIN public.trial_policy_versions p ON p.id = trial_history.policy_id
            WHERE i.replay_id = nullif(current_setting('app.trial_delivery_id', true), '')::uuid
                AND i.event_id = trial_history.event_id AND i.status = 'Applied' AND a.term_id IS NULL
                AND a.applied_at <= trial_history.starts_at
                AND (i.account_id, i.shop_id, i.term_id, i.actor_id, i.operation_key, i.digest)
                    = (trial_history.account_id, trial_history.shop_id, trial_history.term_id, trial_history.actor_id,
                        trial_history.operation_key, trial_history.consent_digest)
                AND p.status = 2 AND p.duration_days = 14
                AND (p.revision, p.trial_terms_version, p.active_member_cap, p.max_concurrent_trial_shops, p.source = 1)
                    = (trial_history.policy_revision, trial_history.terms_version, trial_history.member_cap,
                        trial_history.account_trial_cap, trial_history.development_fixture)));
        """;

    private const string ReadPolicies = """
        CREATE POLICY trial_account_read ON public.trial_history FOR SELECT USING (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
        CREATE POLICY trial_shop_read ON public.subscription_coverage_terms FOR SELECT USING (EXISTS (
            SELECT FROM public.trial_history h WHERE h.term_id = subscription_coverage_terms.term_id
                AND h.shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid));
        CREATE POLICY trial_policy_read ON public.trial_policy_versions FOR SELECT USING (
            status = 2 AND source IN (1, 2) AND (EXISTS (
                SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.replay_id = nullif(current_setting('app.trial_preview_replay', true), '')::uuid
                    AND u.account_id IS NULL AND u.action = 'licensing.shop-trial.preview'
                    AND u.actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid)
                OR EXISTS (SELECT FROM public.shop_trial_delivery_uses u
                    WHERE u.replay_id = nullif(current_setting('app.trial_delivery_id', true), '')::uuid)));
        """;

    private const string ReserveCoverage = """
        CREATE FUNCTION public.reserve_trial_coverage() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.trial_history h
                WHERE h.account_id = NEW.account_id AND h.term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Trial coverage belongs to its originating shop' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER reserve_trial_coverage BEFORE INSERT ON public.shop_affiliation_requests
            FOR EACH ROW EXECUTE FUNCTION public.reserve_trial_coverage();
        CREATE TRIGGER reserve_trial_coverage BEFORE INSERT ON public.shop_affiliations
            FOR EACH ROW EXECUTE FUNCTION public.reserve_trial_coverage();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.trial_history, public.shop_trial_inbox, public.shop_trial_delivery_uses IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.shop_trial_delivery_uses NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.shop_trial_delivery_uses) THEN
                RAISE EXCEPTION 'Trial evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER reserve_trial_coverage ON public.shop_affiliation_requests;
        DROP TRIGGER reserve_trial_coverage ON public.shop_affiliations;
        DROP FUNCTION public.reserve_trial_coverage();
        DROP POLICY trial_policy_read ON public.trial_policy_versions;
        DROP POLICY trial_shop_read ON public.subscription_coverage_terms;
        DROP POLICY trial_term_insert ON public.subscription_coverage_terms;
        DROP TABLE public.trial_history, public.shop_trial_inbox, public.shop_trial_delivery_uses;
        ALTER TABLE public.subscription_coverage_terms DROP CONSTRAINT coverage_term_interval_identity;
        """);
}
