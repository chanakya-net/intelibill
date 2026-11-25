using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261124150000_AddMerchantCoverageSlotConsents")]
public sealed class AddMerchantCoverageSlotConsents : Migration
{
    private const string PreviousKinds = "'Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK(kind IN ({PreviousKinds},'CoverageSlotConsent'));
        CREATE INDEX coverage_slot_accepted_shop ON public.entitlement_messages(account_id,shop_id)
            WHERE kind='CoverageSlotConsent';
        CREATE TABLE public.coverage_slot_consent_events (
            id uuid PRIMARY KEY, shop_id uuid NOT NULL REFERENCES public.shops(id), account_id uuid NOT NULL,
            assignment_id uuid NOT NULL, affiliation_request_id uuid NOT NULL, term_id uuid NOT NULL,
            actor_id uuid NOT NULL, membership_id uuid NOT NULL, authority_version bigint NOT NULL,
            operation_key uuid NOT NULL, kind text NOT NULL CHECK(kind IN ('Granted','Revoked','Accepted')),
            recorded_at timestamptz NOT NULL, expires_at timestamptz NOT NULL CHECK(recorded_at<expires_at),
            coverage_ends_at timestamptz NOT NULL CHECK(expires_at<=coverage_ends_at),
            member_allowance int NOT NULL CHECK(member_allowance>0), active_members int, seat_hash text,
            CHECK((kind='Accepted')=(active_members IS NOT NULL)), CHECK((kind='Accepted')=(seat_hash IS NOT NULL)),
            CHECK(active_members BETWEEN 1 AND member_allowance), CHECK(length(seat_hash)=64 AND seat_hash !~ '[^0-9a-f]'),
            UNIQUE(assignment_id,kind), UNIQUE(shop_id,actor_id,operation_key)
        );
        CREATE TABLE public.coverage_slot_activations (
            assignment_id uuid PRIMARY KEY, shop_id uuid NOT NULL REFERENCES public.shops(id),
            account_id uuid NOT NULL, event_id uuid NOT NULL UNIQUE, activated_at timestamptz NOT NULL
        );
        ALTER TABLE public.coverage_slot_consent_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_consent_events FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_activations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_activations FORCE ROW LEVEL SECURITY;
        CREATE POLICY shop_read ON public.coverage_slot_consent_events FOR SELECT USING
            (shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        CREATE POLICY owner_insert ON public.coverage_slot_consent_events FOR INSERT WITH CHECK(
            shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid
            AND actor_id=nullif(current_setting('app.current_user_id',true),'')::uuid
            AND EXISTS(SELECT FROM public.shop_memberships m WHERE m.user_id=actor_id AND m.id=membership_id
                AND m.shop_id=coverage_slot_consent_events.shop_id AND m.authority_version=coverage_slot_consent_events.authority_version
                AND m.role='Owner' AND m.status='Active'));
        CREATE POLICY shop_read ON public.coverage_slot_activations FOR SELECT USING
            (shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        CREATE POLICY shop_insert ON public.coverage_slot_activations FOR INSERT WITH CHECK(
            shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        CREATE TRIGGER retain_slot_consent BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_slot_consent_events
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
        CREATE TRIGGER retain_slot_activation BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_slot_activations
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.coverage_slot_consent_events, public.coverage_slot_activations IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_slot_consent_events NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_slot_activations NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN IF EXISTS(SELECT FROM public.coverage_slot_consent_events) OR EXISTS(SELECT FROM public.coverage_slot_activations) THEN
            RAISE EXCEPTION 'Retained coverage slot consent prevents downgrade' USING ERRCODE='55000';
        END IF; END $$;
        DROP TABLE public.coverage_slot_activations, public.coverage_slot_consent_events;
        DROP INDEX public.coverage_slot_accepted_shop;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK(kind IN ({PreviousKinds}));
        """);
}
