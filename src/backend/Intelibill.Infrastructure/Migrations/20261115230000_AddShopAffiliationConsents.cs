using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261115230000_AddShopAffiliationConsents")]
public sealed class AddShopAffiliationConsents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public.shop_affiliation_consent_events (
                id uuid PRIMARY KEY,
                shop_id uuid NOT NULL REFERENCES public.shops(id),
                account_id uuid NOT NULL,
                request_id uuid NOT NULL,
                actor_id uuid NOT NULL,
                membership_id uuid NOT NULL,
                authority_version bigint NOT NULL CHECK (authority_version >= 0),
                operation_key uuid NOT NULL,
                kind text NOT NULL CHECK (kind IN ('Granted', 'Revoked', 'Accepted')),
                recorded_at timestamptz NOT NULL,
                term_id uuid,
                shop_name text NOT NULL,
                owner_name text NOT NULL,
                request_expires_at timestamptz NOT NULL CHECK (recorded_at < request_expires_at OR kind = 'Revoked'),
                UNIQUE (shop_id, request_id, kind),
                UNIQUE (shop_id, actor_id, operation_key)
            );
            -- The accepted event itself is the durable outbox and shop-local claim.
            CREATE UNIQUE INDEX shop_affiliation_consent_claim ON public.shop_affiliation_consent_events(shop_id)
                WHERE kind = 'Accepted';
            CREATE INDEX shop_affiliation_consent_delivery ON public.shop_affiliation_consent_events(recorded_at, id)
                WHERE kind = 'Accepted';

            CREATE TABLE public.shop_affiliation_delivery_receipts (
                event_id uuid PRIMARY KEY REFERENCES public.shop_affiliation_consent_events(id),
                shop_id uuid NOT NULL REFERENCES public.shops(id),
                status text NOT NULL CHECK (status IN ('Applied', 'NeedsReview'))
            );

            CREATE FUNCTION public.retain_shop_affiliation_evidence() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            BEGIN
                RAISE EXCEPTION 'Shop affiliation evidence is immutable' USING ERRCODE = '55000';
            END $$;
            """);
        foreach (var table in new[] { "shop_affiliation_consent_events", "shop_affiliation_delivery_receipts" })
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
                    EXECUTE format('CREATE POLICY affiliation_worker_read ON public.{table}
                        FOR SELECT TO %I USING (true)', current_user);
                END $$;
                """);
        }
        migrationBuilder.Sql(Delivery);
        migrationBuilder.Sql(ConsentGuard);
    }

    private const string Delivery = """
        CREATE POLICY owner_insert ON public.shop_affiliation_consent_events FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.current_user_id', true), '')::uuid);
        CREATE POLICY matching_receipt ON public.shop_affiliation_delivery_receipts FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.shop_affiliation_consent_events e
                WHERE e.id = event_id AND e.shop_id = shop_affiliation_delivery_receipts.shop_id AND kind = 'Accepted'));

        CREATE FUNCTION public.pending_shop_affiliation_decisions(p_after uuid)
        RETURNS TABLE (event_id uuid, request_id uuid, account_id uuid, shop_id uuid, term_id uuid,
            actor_id uuid, membership_id uuid, authority_version bigint, shop_name text, owner_name text, accepted_at timestamptz)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT e.id, e.request_id, e.account_id, e.shop_id, e.term_id, e.actor_id, e.membership_id,
                e.authority_version, e.shop_name, e.owner_name, e.recorded_at
            FROM public.shop_affiliation_consent_events e
            WHERE has_table_privilege(session_user, 'public.shop_affiliation_consent_events', 'INSERT')
                AND e.kind = 'Accepted' AND e.id > p_after
                AND NOT EXISTS (SELECT FROM public.shop_affiliation_delivery_receipts r WHERE r.event_id = e.id)
            ORDER BY e.id LIMIT 20
        $$;
        """;

    private const string ConsentGuard = """
        CREATE FUNCTION public.guard_shop_affiliation_consent() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            -- The application already holds this shop fence after the user and session locks.
            PERFORM 1 FROM public.shops WHERE id = NEW.shop_id FOR UPDATE;
            IF NOT EXISTS (
                SELECT FROM public.shop_memberships m
                WHERE m.id = NEW.membership_id AND m.user_id = NEW.actor_id AND m.shop_id = NEW.shop_id
                    AND m.role = 'Owner' AND m.status = 'Active' AND m.authority_version = NEW.authority_version
            ) OR (NEW.kind IN ('Accepted', 'Revoked') AND NOT EXISTS (
                SELECT FROM public.shop_affiliation_consent_events g
                WHERE g.shop_id = NEW.shop_id AND g.request_id = NEW.request_id AND g.kind = 'Granted'
                    AND g.account_id = NEW.account_id AND g.term_id IS NOT DISTINCT FROM NEW.term_id
                    AND g.actor_id = NEW.actor_id AND g.membership_id = NEW.membership_id
                    AND g.authority_version = NEW.authority_version AND g.recorded_at <= NEW.recorded_at
                    AND g.request_expires_at = NEW.request_expires_at
            )) OR EXISTS (
                SELECT FROM public.shop_affiliation_consent_events e
                WHERE e.shop_id = NEW.shop_id AND (e.kind = 'Accepted'
                    OR (e.request_id = NEW.request_id AND e.kind = 'Revoked'))
            ) THEN
                RAISE EXCEPTION 'Shop affiliation consent is invalid' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER shop_affiliation_consent_guard BEFORE INSERT ON public.shop_affiliation_consent_events
            FOR EACH ROW EXECUTE FUNCTION public.guard_shop_affiliation_consent();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.shop_affiliation_consent_events IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.shop_affiliation_consent_events NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.shop_affiliation_consent_events) THEN
                RAISE EXCEPTION 'Shop affiliation evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP FUNCTION public.pending_shop_affiliation_decisions(uuid);
        DROP TABLE public.shop_affiliation_delivery_receipts, public.shop_affiliation_consent_events;
        DROP FUNCTION public.retain_shop_affiliation_evidence();
        DROP FUNCTION public.guard_shop_affiliation_consent();
        """);
}
