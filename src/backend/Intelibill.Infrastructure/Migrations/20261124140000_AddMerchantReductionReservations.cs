using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261124140000_AddMerchantReductionReservations")]
public sealed class AddMerchantReductionReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ('Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'));

            CREATE TABLE public.reduction_manifest_pages (
                account_id uuid NOT NULL, schedule_id uuid NOT NULL, generation bigint NOT NULL CHECK (generation > 0),
                page_index int NOT NULL CHECK (page_index >= 0), page_count int NOT NULL CHECK (page_count > 0 AND page_index < page_count),
                event_id uuid UNIQUE NOT NULL, order_id uuid NOT NULL, term_id uuid NOT NULL,
                total_source_shops int NOT NULL CHECK (total_source_shops BETWEEN 1 AND 100000),
                retained_shop_count int NOT NULL CHECK (retained_shop_count BETWEEN 1 AND total_source_shops),
                source_binding_digest text NOT NULL CHECK (source_binding_digest ~ '^[0-9a-f]{64}$'),
                retained_binding_digest text NOT NULL CHECK (retained_binding_digest ~ '^[0-9a-f]{64}$'),
                expires_at timestamptz NOT NULL CHECK (isfinite(expires_at)),
                digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
                payload bytea NOT NULL CHECK (octet_length(payload) BETWEEN 1 AND 8192),
                assertion text NOT NULL, public_key text NOT NULL,
                PRIMARY KEY (account_id, schedule_id, generation, page_index)
            );

            CREATE TABLE public.reduction_manifest_shops (
                account_id uuid NOT NULL, schedule_id uuid NOT NULL, generation bigint NOT NULL CHECK (generation > 0),
                page_index int NOT NULL CHECK (page_index >= 0), shop_id uuid NOT NULL,
                affiliation_request_id uuid NOT NULL, retained boolean NOT NULL,
                expected_active_members int NOT NULL CHECK (expected_active_members >= 0),
                expected_seat_hash text NOT NULL CHECK (expected_seat_hash ~ '^[0-9a-f]{64}$'),
                PRIMARY KEY (account_id, schedule_id, generation, shop_id),
                FOREIGN KEY (account_id, schedule_id, generation, page_index)
                    REFERENCES public.reduction_manifest_pages(account_id, schedule_id, generation, page_index)
            );

            CREATE TABLE public.reduction_reservation_receipts (
                account_id uuid NOT NULL, term_id uuid NOT NULL, schedule_id uuid NOT NULL, order_id uuid NOT NULL,
                generation bigint NOT NULL CHECK (generation > 0), event_id uuid PRIMARY KEY,
                phase text NOT NULL CHECK (phase IN ('ReductionReserve','ReductionConsume')),
                expires_at timestamptz NOT NULL CHECK (isfinite(expires_at)),
                digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'), fence_hash text,
                outcome text NOT NULL CHECK (outcome IN ('Reserved','Applied','NeedsResolution','NeedsReview')),
                reason text,
                command bytea NOT NULL CHECK (octet_length(command) BETWEEN 1 AND 8192),
                assertion text NOT NULL, public_key text NOT NULL,
                UNIQUE (account_id, schedule_id, generation, phase),
                CHECK ((phase = 'ReductionReserve' OR outcome <> 'Reserved') AND (phase = 'ReductionConsume' OR outcome <> 'Applied'))
            );
            CREATE UNIQUE INDEX single_applied_reduction_term
                ON public.reduction_reservation_receipts(account_id, term_id) WHERE outcome = 'Applied';

            CREATE TABLE public.applied_shop_reductions (
                shop_id uuid NOT NULL REFERENCES public.shops(id), account_id uuid NOT NULL,
                predecessor_term_id uuid NOT NULL, successor_term_id uuid NOT NULL,
                affiliation_id uuid NOT NULL, schedule_id uuid NOT NULL, generation bigint NOT NULL CHECK (generation > 0),
                applied_event uuid NOT NULL REFERENCES public.reduction_reservation_receipts(event_id),
                members int NOT NULL CHECK (members BETWEEN 1 AND 100000),
                starts_at timestamptz NOT NULL CHECK (isfinite(starts_at)),
                paid_through timestamptz NOT NULL CHECK (isfinite(paid_through) AND paid_through > starts_at),
                PRIMARY KEY (shop_id, predecessor_term_id, successor_term_id)
            );
            """);

        foreach (var table in new[] { "reduction_manifest_pages", "reduction_manifest_shops", "reduction_reservation_receipts" })
        {
            migrationBuilder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY signed_reduction_event ON public.{table} USING
                    (account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid)
                    WITH CHECK (account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid AND
                        EXISTS (SELECT FROM public.entitlement_request_uses u WHERE u.xmin = pg_current_xact_id()::xid));
                """);
        }

        migrationBuilder.Sql("""
            ALTER TABLE public.applied_shop_reductions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.applied_shop_reductions FORCE ROW LEVEL SECURITY;
            CREATE POLICY applied_reduction_read ON public.applied_shop_reductions FOR SELECT USING
                (shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
            CREATE POLICY applied_reduction_insert ON public.applied_shop_reductions FOR INSERT WITH CHECK
                (account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid AND
                    EXISTS (SELECT FROM public.reduction_reservation_receipts r
                        WHERE r.account_id = applied_shop_reductions.account_id
                            AND r.term_id = applied_shop_reductions.predecessor_term_id
                            AND r.schedule_id = applied_shop_reductions.schedule_id
                            AND r.generation = applied_shop_reductions.generation
                            AND r.event_id = applied_shop_reductions.applied_event
                            AND r.outcome = 'Applied' AND r.xmin = pg_current_xact_id()::xid));
            CREATE POLICY reduction_fence_capacity_read ON public.applied_shop_capacity FOR SELECT USING
                (account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid);
            """);

        foreach (var table in new[] { "reduction_manifest_pages", "reduction_manifest_shops", "reduction_reservation_receipts", "applied_shop_reductions" })
            migrationBuilder.Sql($"CREATE TRIGGER retain_reduction BEFORE UPDATE OR DELETE OR TRUNCATE ON public.{table} FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence()");

        migrationBuilder.Sql(ObserveFunction);
        migrationBuilder.Sql(FenceFunction);
    }

    // Aggregate active-member count + version hash only; never exposes member identities.
    private const string ObserveFunction = """
        CREATE FUNCTION public.reduction_observe_shops(account uuid, shop_ids uuid[])
        RETURNS TABLE(shop_id uuid, affiliation_id uuid, active_members int, seat_hash text, has_pending_affiliation boolean)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT requested.shop_id,
                accepted.request_id AS affiliation_id,
                coalesce(m.active_members, 0) AS active_members,
                coalesce(m.seat_hash, encode(sha256(''::bytea), 'hex')) AS seat_hash,
                EXISTS (SELECT FROM public.shop_affiliation_consent_events pending
                    LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = pending.id
                    WHERE pending.account_id = account AND pending.shop_id = requested.shop_id
                        AND pending.kind = 'Accepted' AND r.status IS NULL) AS has_pending_affiliation
            FROM unnest(shop_ids) AS requested(shop_id)
            LEFT JOIN LATERAL (
                SELECT count(*) FILTER (WHERE sm.status = 'Active')::int AS active_members,
                    encode(sha256(convert_to(coalesce(string_agg(sm.id::text || '/' || sm.authority_version::text, ','
                        ORDER BY sm.id), ''), 'UTF8')), 'hex') AS seat_hash
                FROM public.shop_memberships sm WHERE sm.shop_id = requested.shop_id
            ) m ON true
            LEFT JOIN LATERAL (
                SELECT e.request_id FROM public.shop_affiliation_consent_events e
                JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id AND r.status = 'Applied'
                WHERE e.shop_id = requested.shop_id AND e.account_id = account AND e.kind = 'Accepted'
                ORDER BY e.recorded_at DESC, e.id DESC LIMIT 1
            ) accepted ON true
            WHERE account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
                AND EXISTS (SELECT FROM public.entitlement_request_uses u WHERE u.replay_id =
                    nullif(current_setting('app.entitlement_replay_id', true), '')::uuid AND u.xmin = pg_current_xact_id()::xid)
            ORDER BY requested.shop_id
        $$;
        """;

    // Semantic coverage + effective capacity + restriction + seat versions under the exclusive account fence.
    private const string FenceFunction = """
        CREATE FUNCTION public.reduction_fence_shops(account uuid, term uuid)
        RETURNS TABLE(shop_id uuid, affiliation_id uuid, state text, starts_at timestamptz, access_ends_at timestamptz,
            capacity int, members int, active_members int, seat_hash text, suspension_mode text, closed boolean,
            account_restriction text)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT s.shop_id, s.affiliation_request_id, s.state, s.starts_at, s.access_ends_at,
                s.shop_capacity,
                coalesce((SELECT a.members FROM public.applied_shop_capacity a
                    WHERE a.shop_id = s.shop_id AND a.account_id = s.account_id AND a.term_id = s.term_id
                        AND a.affiliation_id = s.affiliation_request_id ORDER BY a.revision DESC LIMIT 1),
                    s.member_allowance) AS members,
                m.active_members, m.seat_hash, s.suspension_mode, (s.state = 'Closed') AS closed,
                coalesce((SELECT ar.restriction FROM public.account_license_restrictions ar WHERE ar.account_id = account), 'Unrestricted') AS account_restriction
            FROM public.shop_license_states s
            CROSS JOIN LATERAL (
                SELECT count(*) FILTER (WHERE sm.status = 'Active')::int AS active_members,
                    encode(sha256(convert_to(coalesce(string_agg(sm.id::text || '/' || sm.authority_version::text, ','
                        ORDER BY sm.id), ''), 'UTF8')), 'hex') AS seat_hash
                FROM public.shop_memberships sm WHERE sm.shop_id = s.shop_id
            ) m
            JOIN public.shop_affiliation_consent_events accepted ON accepted.shop_id = s.shop_id AND accepted.account_id = s.account_id
                AND accepted.request_id = s.affiliation_request_id AND accepted.kind = 'Accepted'
            JOIN public.shop_affiliation_delivery_receipts receipt ON receipt.event_id = accepted.id AND receipt.status = 'Applied'
            WHERE NOT EXISTS (SELECT FROM public.shop_affiliation_consent_events pending
                LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = pending.id
                WHERE pending.account_id = account AND pending.term_id = term AND pending.kind = 'Accepted' AND r.status IS NULL)
                AND s.term_id = term AND s.account_id = account AND s.basis = 'Paid'
                AND account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
                AND EXISTS (SELECT FROM public.entitlement_request_uses u WHERE u.replay_id =
                    nullif(current_setting('app.entitlement_replay_id', true), '')::uuid AND u.xmin = pg_current_xact_id()::xid)
            ORDER BY s.shop_id
        $$;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.reduction_reservation_receipts, public.reduction_manifest_pages IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.reduction_reservation_receipts NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.reduction_manifest_pages NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.reduction_reservation_receipts)
                OR EXISTS (SELECT FROM public.reduction_manifest_pages) THEN
                RAISE EXCEPTION 'Retained merchant reduction decisions prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP POLICY reduction_fence_capacity_read ON public.applied_shop_capacity;
        DROP TABLE public.applied_shop_reductions, public.reduction_reservation_receipts,
            public.reduction_manifest_shops, public.reduction_manifest_pages;
        DROP FUNCTION public.reduction_fence_shops(uuid, uuid), public.reduction_observe_shops(uuid, uuid[]);
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK (kind IN ('Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt'));
        """);
}
