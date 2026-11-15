using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261116100000_AddBillingManagerRemoval")]
public sealed class AddBillingManagerRemoval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Evidence);
        migrationBuilder.Sql(Functions);
    }

    private const string Evidence = """
        CREATE TABLE public.billing_member_removals (
            version integer NOT NULL DEFAULT 1 CHECK (version = 1),
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            member_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            actor_revision integer NOT NULL CHECK (actor_revision > 0),
            operation_key uuid NOT NULL,
            request_id uuid NOT NULL,
            request_hash bytea NOT NULL CHECK (octet_length(request_hash) = 32),
            reason text NOT NULL CHECK (char_length(reason) BETWEEN 1 AND 500),
            removed_at timestamptz NOT NULL,
            before_role integer NOT NULL DEFAULT 2 CHECK (before_role = 2),
            after_role integer NOT NULL DEFAULT 2 CHECK (after_role = 2),
            before_status integer NOT NULL DEFAULT 1 CHECK (before_status = 1),
            after_status integer NOT NULL DEFAULT 2 CHECK (after_status = 2),
            before_revision integer NOT NULL CHECK (before_revision > 0),
            after_revision integer NOT NULL CHECK (after_revision = before_revision + 1),
            PRIMARY KEY (account_id, actor_id, operation_key),
            UNIQUE (member_id, before_revision)
        );

        ALTER TABLE public.billing_member_removals ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_member_removals FORCE ROW LEVEL SECURITY;
        CREATE POLICY billing_removal_read ON public.billing_member_removals FOR SELECT USING (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.billing_account_members m
                WHERE m.account_id = billing_member_removals.account_id
                  AND m.merchant_user_id = actor_id AND m.role = 1 AND m.status = 1)
        );

        DO $$
        BEGIN
            EXECUTE format('CREATE POLICY billing_member_remove ON public.billing_account_members FOR UPDATE TO %I
                USING (account_id = nullif(current_setting(''app.billing_account_id'', true), '''')::uuid
                    AND role = 2 AND status = 1)
                WITH CHECK (account_id = nullif(current_setting(''app.billing_account_id'', true), '''')::uuid
                    AND role = 2 AND status = 2)', current_user);
            EXECUTE format('CREATE POLICY billing_removal_insert ON public.billing_member_removals
                FOR INSERT TO %I WITH CHECK (true)', current_user);
        END $$;

        CREATE TRIGGER billing_removal_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.billing_member_removals
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    private const string Functions = """
        CREATE FUNCTION public.billing_removal_owner(p_account uuid, p_actor uuid) RETURNS integer
        LANGUAGE sql VOLATILE SET search_path = pg_catalog AS $$
            SELECT revision FROM public.billing_account_members
            WHERE has_table_privilege(session_user, 'public.billing_account_members', 'SELECT')
              AND has_table_privilege(session_user, 'public.billing_member_removals', 'INSERT')
              AND account_id = p_account AND merchant_user_id = p_actor AND role = 1 AND status = 1
            FOR SHARE
        $$;

        CREATE FUNCTION public.lock_billing_removal_target(p_member uuid)
        RETURNS TABLE (role integer, status integer, revision integer)
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $$
        DECLARE
            account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
            actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
        BEGIN
            IF public.billing_removal_owner(account, actor) IS NULL THEN RETURN; END IF;
            RETURN QUERY SELECT m.role, m.status, m.revision FROM public.billing_account_members m
                WHERE m.account_id = account AND m.id = p_member AND m.role = 2 FOR NO KEY UPDATE;
            IF NOT FOUND THEN
                -- Report an Owner without upgrading the caller's share lock.
                RETURN QUERY SELECT m.role, m.status, m.revision FROM public.billing_account_members m
                    WHERE m.account_id = account AND m.id = p_member AND m.role = 1;
            END IF;
        END $$;

        CREATE FUNCTION public.remove_billing_manager(p_member uuid, p_revision integer, p_key uuid,
            p_request uuid, p_hash bytea, p_reason text, p_at timestamptz) RETURNS boolean
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $$
        DECLARE
            account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
            actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
            owner_revision integer := public.billing_removal_owner(account, actor);
        BEGIN
            IF owner_revision IS NULL THEN RETURN false; END IF;

            UPDATE public.billing_account_members
            SET status = 2, revision = revision + 1
            WHERE account_id = account AND id = p_member AND role = 2 AND status = 1 AND revision = p_revision;
            IF NOT FOUND THEN RETURN false; END IF;

            INSERT INTO public.billing_member_removals
                (account_id, member_id, actor_id, actor_revision, operation_key, request_id, request_hash, reason,
                 removed_at, before_revision, after_revision)
            VALUES (account, p_member, actor, owner_revision, p_key, p_request, p_hash, p_reason,
                p_at, p_revision, p_revision + 1);
            RETURN true;
        END $$;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.billing_member_removals IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.billing_member_removals NO FORCE ROW LEVEL SECURITY;
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM public.billing_member_removals) THEN
                RAISE EXCEPTION 'Billing removal evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP FUNCTION public.remove_billing_manager(uuid, integer, uuid, uuid, bytea, text, timestamptz);
        DROP FUNCTION public.lock_billing_removal_target(uuid);
        DROP FUNCTION public.billing_removal_owner(uuid, uuid);
        DROP POLICY billing_member_remove ON public.billing_account_members;
        DROP TABLE public.billing_member_removals;
        """);
}
