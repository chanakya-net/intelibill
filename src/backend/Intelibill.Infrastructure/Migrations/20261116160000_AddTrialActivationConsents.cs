using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261116160000_AddTrialActivationConsents")]
public sealed class AddTrialActivationConsents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public.shop_trial_consents (
                event_id uuid PRIMARY KEY,
                shop_id uuid NOT NULL REFERENCES public.shops(id),
                actor_id uuid NOT NULL,
                membership_id uuid NOT NULL,
                authority_version bigint NOT NULL CHECK (authority_version >= 0),
                operation_key uuid NOT NULL,
                accepted_at timestamptz NOT NULL,
                active_members integer NOT NULL CHECK (active_members > 0),
                payload jsonb NOT NULL,
                UNIQUE (shop_id, actor_id, operation_key),
                UNIQUE (event_id, shop_id),
                CHECK ((payload->>'eventId')::uuid IS NOT DISTINCT FROM event_id
                    AND (payload->>'shopId')::uuid IS NOT DISTINCT FROM shop_id
                    AND (payload->>'actorId')::uuid IS NOT DISTINCT FROM actor_id
                    AND (payload->>'membershipId')::uuid IS NOT DISTINCT FROM membership_id
                    AND (payload->>'authorityVersion')::bigint IS NOT DISTINCT FROM authority_version
                    AND (payload->>'activeMembers')::int IS NOT DISTINCT FROM active_members
                    AND (payload->>'acceptedAt')::timestamptz IS NOT DISTINCT FROM accepted_at
                    AND (payload->'acceptance'->>'operationKey')::uuid IS NOT DISTINCT FROM operation_key
                    AND payload->'acceptance'->'termsAccepted' IS NOT DISTINCT FROM 'true'::jsonb)
            );
            CREATE TABLE public.shop_trial_receipts (
                event_id uuid PRIMARY KEY,
                shop_id uuid NOT NULL,
                status text NOT NULL,
                reason varchar(40),
                CHECK ((status = 'Applied' AND reason IS NULL) OR (status = 'NeedsReview' AND reason IS NOT NULL)),
                FOREIGN KEY (event_id, shop_id) REFERENCES public.shop_trial_consents(event_id, shop_id)
            );
            """);
        foreach (var table in new[] { "shop_trial_consents", "shop_trial_receipts" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY shop_read ON public.{table} FOR SELECT USING (
                    shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                    FOR EACH ROW EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
                DO $$ BEGIN
                    EXECUTE format('CREATE POLICY trial_worker_read ON public.{table}
                        FOR SELECT TO %I USING (true)', current_user);
                END $$;
                """);
        }
        migrationBuilder.Sql(Security);
    }

    private const string Security = """
        CREATE POLICY owner_insert ON public.shop_trial_consents FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.current_user_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.shop_memberships m WHERE m.id = membership_id
                AND m.shop_id = shop_trial_consents.shop_id AND m.user_id = actor_id
                AND m.role = 'Owner' AND m.status = 'Active' AND m.authority_version = shop_trial_consents.authority_version)
            AND active_members = (SELECT count(*) FROM public.shop_memberships m
                WHERE m.shop_id = shop_trial_consents.shop_id AND m.status = 'Active'));
        CREATE POLICY delivery_insert ON public.shop_trial_receipts FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
        CREATE FUNCTION public.pending_shop_trial_consents(p_after uuid)
        RETURNS TABLE (event_id uuid, payload text)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT c.event_id, c.payload::text FROM public.shop_trial_consents c
            WHERE has_table_privilege(session_user, 'public.shop_trial_consents', 'INSERT')
                AND c.event_id > p_after
                AND NOT EXISTS (SELECT FROM public.shop_trial_receipts r WHERE r.event_id = c.event_id)
            ORDER BY c.event_id LIMIT 20
        $$;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.shop_trial_consents IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.shop_trial_consents NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.shop_trial_consents) THEN
                RAISE EXCEPTION 'Trial consent evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP FUNCTION public.pending_shop_trial_consents(uuid);
        DROP TABLE public.shop_trial_receipts, public.shop_trial_consents;
        """);
}
