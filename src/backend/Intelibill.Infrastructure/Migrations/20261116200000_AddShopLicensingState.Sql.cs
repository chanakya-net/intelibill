using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

public partial class AddShopLicensingState
{
    private const string Shop = "shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid";
    private const string Account = "account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid";
    private const string Replay = "replay_id = nullif(current_setting('app.entitlement_replay_id', true), '')::uuid";

    private static void AddDelivery(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Create);
        migrationBuilder.Sql(TablesAndConstraints);
        migrationBuilder.Sql($"""
            ALTER TABLE public.license_grant_evidence ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.license_grant_evidence FORCE ROW LEVEL SECURITY;
            CREATE POLICY license_grant_evidence_shop_isolation ON public.license_grant_evidence
                USING ({Shop}) WITH CHECK ({Shop});
            ALTER TABLE public.shop_license_states ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.shop_license_states FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.account_license_restrictions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.account_license_restrictions FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                EXECUTE format($policy$CREATE POLICY shop_license_states_shop_isolation ON public.shop_license_states
                    USING ({Shop} OR current_user = %L) WITH CHECK ({Shop})$policy$, current_user);
                EXECUTE format($policy$
                    CREATE POLICY account_license_restrictions_shop_isolation ON public.account_license_restrictions
                    USING ({Account} OR current_user = %L OR EXISTS (SELECT FROM public.shop_license_states s
                        WHERE s.account_id = account_license_restrictions.account_id AND s.shop_id =
                            nullif(current_setting('app.active_shop_id', true), '')::uuid))
                    WITH CHECK ({Account})$policy$, current_user);
            END $$;
            """);
        foreach (var table in new[] { "entitlement_messages", "entitlement_outbox", "entitlement_snapshot_inbox",
            "entitlement_reconciliation_requests" })
            migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope(table, Shop));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope("entitlement_request_uses", Replay));
        foreach (var table in new[] { "entitlement_messages", "entitlement_outbox", "entitlement_reconciliation_requests" })
            migrationBuilder.Sql(LeasedHttpOutboxSchema.WorkerAccess(table));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Workers);
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql(ReconciliationScan);
    }

    private const string TablesAndConstraints = """
        ALTER TABLE public.license_grant_evidence ADD CONSTRAINT valid_license_grant_evidence CHECK (
            octet_length(payload) BETWEEN 1 AND 8192 AND digest ~ '^[0-9a-f]{64}$'
            AND isfinite(issued_at) AND isfinite(received_at) AND isfinite(applied_at)
            AND isfinite(account_upstream_verified_at) AND isfinite(shop_upstream_verified_at)
            AND issued_at <= received_at AND received_at <= applied_at);
        ALTER TABLE public.shop_license_states ADD CONSTRAINT valid_shop_license_state CHECK (
            revision > 0 AND isfinite(upstream_verified_at) AND isfinite(starts_at) AND isfinite(access_ends_at)
            AND isfinite(next_boundary) AND isfinite(enforcement_activated_at)
            AND next_boundary > upstream_verified_at AND shop_capacity > 0 AND member_allowance > 0
            AND state IN ('PendingActivation','Scheduled','Active','Grace','Expired','Suspended','EvidenceIncomplete','NeedsReview')
            AND (state NOT IN ('Active', 'Grace') OR (term_id IS NOT NULL AND basis IS NOT NULL
                AND starts_at IS NOT NULL AND access_ends_at IS NOT NULL
                AND starts_at <= upstream_verified_at AND access_ends_at > upstream_verified_at)));
        ALTER TABLE public.account_license_restrictions ADD CONSTRAINT valid_account_license_restriction CHECK (
            revision > 0 AND isfinite(upstream_verified_at) AND restriction IN ('Unrestricted', 'Trading', 'Security')
            AND (released_revision IS NULL OR (restriction = 'Unrestricted' AND released_revision BETWEEN 1 AND revision - 1)));
        CREATE TABLE public.entitlement_snapshot_inbox (
            event_id uuid PRIMARY KEY,
            account_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
            acknowledgement bytea NOT NULL CHECK (octet_length(acknowledgement) BETWEEN 1 AND 8192)
        );
        CREATE TABLE public.entitlement_request_uses (
            replay_id uuid PRIMARY KEY,
            expires_at timestamptz NOT NULL CHECK (isfinite(expires_at))
        );
        CREATE TABLE public.entitlement_reconciliation_requests (
            shop_id uuid PRIMARY KEY REFERENCES public.shops(id),
            event_id uuid NOT NULL REFERENCES public.entitlement_messages(event_id),
            next_check_at timestamptz NOT NULL CHECK (isfinite(next_check_at))
        );
        """;

    private const string Guards = """
        CREATE TRIGGER retain_license_grant_evidence BEFORE UPDATE OR DELETE ON public.license_grant_evidence
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_license_grant_evidence_table BEFORE TRUNCATE ON public.license_grant_evidence
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_entitlement_inbox BEFORE UPDATE OR DELETE ON public.entitlement_snapshot_inbox
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_entitlement_inbox_table BEFORE TRUNCATE ON public.entitlement_snapshot_inbox
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();

        CREATE FUNCTION public.guard_license_projection() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.revision < OLD.revision OR NEW.upstream_verified_at < OLD.upstream_verified_at
                OR (NEW.revision = OLD.revision AND (to_jsonb(NEW) - 'enforcement_activated_at')
                    IS DISTINCT FROM (to_jsonb(OLD) - 'enforcement_activated_at')) THEN
                RAISE EXCEPTION 'Entitlement head cannot go backwards' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_account_license_revision BEFORE UPDATE ON public.account_license_restrictions
            FOR EACH ROW EXECUTE FUNCTION public.guard_license_projection();
        CREATE TRIGGER guard_shop_license_revision BEFORE UPDATE ON public.shop_license_states
            FOR EACH ROW EXECUTE FUNCTION public.guard_license_projection();

        CREATE FUNCTION public.guard_enforcement_activation() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF TG_OP = 'INSERT' THEN
                IF NEW.enforcement_activated_at IS NOT NULL THEN
                    RAISE EXCEPTION 'New projections are observe-only' USING ERRCODE = '55000';
                END IF;
                RETURN NEW;
            END IF;
            IF TG_OP = 'DELETE' OR (OLD.enforcement_activated_at IS NOT NULL
                AND NEW.enforcement_activated_at IS DISTINCT FROM OLD.enforcement_activated_at) THEN
                RAISE EXCEPTION 'Enforcement activation is retained' USING ERRCODE = '55000';
            END IF;
            IF NEW.enforcement_activated_at IS DISTINCT FROM OLD.enforcement_activated_at
                AND current_user <> (SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = TG_RELID) THEN
                RAISE EXCEPTION 'Only the schema owner can preserve historical activation' USING ERRCODE = '55000';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_shop_activation BEFORE INSERT OR UPDATE OR DELETE ON public.shop_license_states
            FOR EACH ROW EXECUTE FUNCTION public.guard_enforcement_activation();
        CREATE TRIGGER retain_account_license_restriction BEFORE DELETE ON public.account_license_restrictions
            FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_account_license_restriction_table BEFORE TRUNCATE ON public.account_license_restrictions
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_shop_activation_table BEFORE TRUNCATE ON public.shop_license_states
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        """;

    internal static string ReconciliationScan => $$"""
        CREATE OR REPLACE FUNCTION public.due_merchant_entitlement_reconciliation(p_now timestamptz)
        RETURNS TABLE (account_id uuid, shop_id uuid, affiliation_request_id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT e.account_id, e.shop_id, e.request_id FROM public.shop_affiliation_consent_events e
            LEFT JOIN public.entitlement_reconciliation_requests r USING (shop_id)
            LEFT JOIN public.entitlement_outbox o ON o.event_id = r.event_id
            LEFT JOIN public.shop_license_states s ON s.shop_id = e.shop_id AND s.account_id = e.account_id
            LEFT JOIN public.account_license_restrictions a ON a.account_id = s.account_id
            WHERE (s.shop_id IS NULL OR a.account_id IS NULL OR least(s.next_boundary,
                s.upstream_verified_at + interval '47 hours', a.upstream_verified_at + interval '47 hours') <= p_now)
                AND e.kind = 'Accepted' AND (r.shop_id IS NULL OR
                (r.next_check_at <= p_now AND o.status IN ('Delivered', 'DeadLetter')))
                AND {{LeasedHttpOutboxSchema.Unscoped}}
                AND NOT EXISTS (SELECT FROM public.shop_affiliation_delivery_receipts f
                    WHERE f.event_id = e.id AND f.status = 'NeedsReview')
            ORDER BY r.next_check_at NULLS FIRST, e.shop_id LIMIT 20
        $$;
        REVOKE ALL ON FUNCTION public.due_merchant_entitlement_reconciliation(timestamptz) FROM PUBLIC;
        """;

    private static void DropDelivery(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.license_grant_evidence, public.shop_license_states, public.entitlement_messages
                IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.license_grant_evidence NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.shop_license_states NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.entitlement_messages NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.license_grant_evidence) OR EXISTS (SELECT FROM public.shop_license_states)
                    OR EXISTS (SELECT FROM public.entitlement_messages) THEN
                    RAISE EXCEPTION 'Retained entitlement evidence prevents downgrade' USING ERRCODE = '55000';
                END IF;
            END $$;
            DROP FUNCTION public.due_merchant_entitlement_reconciliation(timestamptz);
            DROP POLICY account_license_restrictions_shop_isolation ON public.account_license_restrictions;
            DROP TABLE public.entitlement_reconciliation_requests, public.entitlement_request_uses, public.entitlement_snapshot_inbox;
            DROP TRIGGER retain_license_grant_evidence ON public.license_grant_evidence;
            DROP TRIGGER retain_license_grant_evidence_table ON public.license_grant_evidence;
            DROP TRIGGER retain_shop_activation_table ON public.shop_license_states;
            DROP TRIGGER retain_account_license_restriction ON public.account_license_restrictions;
            DROP TRIGGER retain_account_license_restriction_table ON public.account_license_restrictions;
            DROP TRIGGER guard_shop_activation ON public.shop_license_states;
            DROP TRIGGER guard_shop_license_revision ON public.shop_license_states;
            DROP TRIGGER guard_account_license_revision ON public.account_license_restrictions;
            DROP FUNCTION public.guard_enforcement_activation(), public.guard_license_projection();
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Drop);
    }
}
