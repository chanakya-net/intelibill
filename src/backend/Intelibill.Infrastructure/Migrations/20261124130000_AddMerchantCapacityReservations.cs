using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261124130000_AddMerchantCapacityReservations")]
public sealed class AddMerchantCapacityReservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK(kind IN ('Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt'));
            CREATE TABLE public.capacity_reservation_receipts (
                account_id uuid NOT NULL,term_id uuid NOT NULL,order_id uuid NOT NULL,event_id uuid PRIMARY KEY,
                phase text NOT NULL CHECK(phase IN ('CapacityReserve','CapacityConsume')),
                revision bigint NOT NULL CHECK(revision>0), expires_at timestamptz NOT NULL CHECK(isfinite(expires_at)),
                digest text NOT NULL CHECK(digest ~ '^[0-9a-f]{64}$'), fence_hash text,
                outcome text NOT NULL CHECK(outcome IN ('Reserved','Applied','NeedsReview')),
                command bytea NOT NULL CHECK(octet_length(command) BETWEEN 1 AND 8192),
                assertion text NOT NULL, public_key text NOT NULL,
                UNIQUE(account_id,order_id,phase),
                CHECK((phase='CapacityReserve' OR outcome<>'Reserved') AND (phase='CapacityConsume' OR outcome<>'Applied'))
            );
            CREATE UNIQUE INDEX single_applied_capacity_revision ON public.capacity_reservation_receipts(account_id,term_id,revision)
                WHERE outcome='Applied';
            CREATE TABLE public.applied_shop_capacity (
                shop_id uuid NOT NULL REFERENCES public.shops(id),account_id uuid NOT NULL,term_id uuid NOT NULL,
                affiliation_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
                applied_event uuid NOT NULL REFERENCES public.capacity_reservation_receipts(event_id),
                members integer NOT NULL CHECK(members BETWEEN 1 AND 100000),
                paid_through timestamptz NOT NULL CHECK(isfinite(paid_through)), PRIMARY KEY(shop_id,term_id,revision)
            );
            ALTER TABLE public.capacity_reservation_receipts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.capacity_reservation_receipts FORCE ROW LEVEL SECURITY;
            CREATE POLICY signed_capacity_event ON public.capacity_reservation_receipts USING
                (account_id=nullif(current_setting('app.entitlement_account_id',true),'')::uuid)
                WITH CHECK(account_id=nullif(current_setting('app.entitlement_account_id',true),'')::uuid AND
                    EXISTS(SELECT FROM public.entitlement_request_uses u WHERE u.xmin=pg_current_xact_id()::xid));
            ALTER TABLE public.applied_shop_capacity ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.applied_shop_capacity FORCE ROW LEVEL SECURITY;
            CREATE POLICY applied_capacity_read ON public.applied_shop_capacity FOR SELECT USING
                (shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
            CREATE POLICY applied_capacity_insert ON public.applied_shop_capacity FOR INSERT WITH CHECK
                (account_id=nullif(current_setting('app.entitlement_account_id',true),'')::uuid AND
                    EXISTS(SELECT FROM public.capacity_reservation_receipts r WHERE r.account_id=applied_shop_capacity.account_id
                        AND r.term_id=applied_shop_capacity.term_id AND r.revision=applied_shop_capacity.revision
                        AND r.event_id=applied_shop_capacity.applied_event AND r.outcome='Applied' AND r.xmin=pg_current_xact_id()::xid));
            """);
        foreach (var table in new[] { "capacity_reservation_receipts", "applied_shop_capacity" })
            migrationBuilder.Sql($"CREATE TRIGGER retain_capacity BEFORE UPDATE OR DELETE OR TRUNCATE ON public.{table} FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence()");
        migrationBuilder.Sql(FenceFacts);
    }

    // Runs after the exclusive account lock and replay consumption. Exposes capacity evidence only to the
    // signed receiver transaction. Membership rows are retained and every status transition advances authority_version.
    private const string FenceFacts = """
        CREATE FUNCTION public.capacity_fence_shops(account uuid, term uuid)
        RETURNS TABLE(shop_id uuid, affiliation_id uuid, shop_revision bigint, state text, starts_at timestamptz,
            next_boundary timestamptz, access_ends_at timestamptz,capacity int,members int,active_members int,seat_hash text)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog AS $$
            SELECT s.shop_id,s.affiliation_request_id,s.revision,s.state,s.starts_at,s.next_boundary,s.access_ends_at,
                s.shop_capacity,s.member_allowance,m.active_members,m.seat_hash
            FROM public.shop_license_states s
            CROSS JOIN LATERAL (SELECT count(*) FILTER(WHERE m.status='Active')::int AS active_members,
                encode(sha256(convert_to(coalesce(string_agg(m.id::text || '/' || m.authority_version::text,','
                    ORDER BY m.id),''),'UTF8')),'hex') AS seat_hash
                FROM public.shop_memberships m WHERE m.shop_id=s.shop_id) m
            JOIN public.shop_affiliation_consent_events accepted ON accepted.shop_id=s.shop_id AND accepted.account_id=s.account_id
                AND accepted.request_id=s.affiliation_request_id AND accepted.kind='Accepted'
            JOIN public.shop_affiliation_delivery_receipts receipt ON receipt.event_id=accepted.id AND receipt.status='Applied'
            WHERE NOT EXISTS (SELECT FROM public.shop_affiliation_consent_events pending
                LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id=pending.id
                WHERE pending.account_id=account AND pending.term_id=term AND pending.kind='Accepted' AND r.status IS NULL)
                AND s.term_id=term AND s.account_id=account AND s.basis='Paid'
                AND account=nullif(current_setting('app.entitlement_account_id',true),'')::uuid
                AND EXISTS(SELECT FROM public.entitlement_request_uses u WHERE u.replay_id=
                    nullif(current_setting('app.entitlement_replay_id',true),'')::uuid AND u.xmin=pg_current_xact_id()::xid)
            ORDER BY s.shop_id
        $$;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.capacity_reservation_receipts IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.capacity_reservation_receipts NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN IF EXISTS(SELECT FROM public.capacity_reservation_receipts) THEN
            RAISE EXCEPTION 'Retained merchant capacity decisions prevent downgrade' USING ERRCODE='55000';
        END IF; END $$;
        DROP TABLE public.applied_shop_capacity,public.capacity_reservation_receipts;
        DROP FUNCTION public.capacity_fence_shops(uuid,uuid);
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK(kind IN ('Snapshot','Acknowledgement','Reconciliation','DeviceObservation'));
        """);
}
