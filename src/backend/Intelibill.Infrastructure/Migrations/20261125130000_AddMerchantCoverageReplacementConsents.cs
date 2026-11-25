using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261125130000_AddMerchantCoverageReplacementConsents")]
public sealed class AddMerchantCoverageReplacementConsents : Migration
{
    private const string PreviousKinds = "'Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent'";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK(kind IN ({PreviousKinds},'CoverageReplacementConsent'));
        CREATE INDEX coverage_replacement_accepted_shop ON public.entitlement_messages(account_id,shop_id)
            WHERE kind='CoverageReplacementConsent';
        CREATE TABLE public.coverage_replacement_consent_events (
            id uuid PRIMARY KEY, shop_id uuid NOT NULL REFERENCES public.shops(id), account_id uuid NOT NULL,
            replacement_id uuid NOT NULL, affiliation_request_id uuid NOT NULL, term_id uuid NOT NULL,
            actor_id uuid NOT NULL, membership_id uuid NOT NULL, authority_version bigint NOT NULL,
            operation_key uuid NOT NULL, kind text NOT NULL CHECK(kind IN ('Granted','Revoked','Accepted')),
            recorded_at timestamptz NOT NULL, expires_at timestamptz NOT NULL CHECK(kind='Revoked' OR recorded_at<expires_at),
            effective_at timestamptz NOT NULL CHECK(effective_at<=expires_at), outgoing_shop_id uuid NOT NULL,
            member_allowance int NOT NULL CHECK(member_allowance>0), active_members int, seat_hash text,
            CHECK((kind='Accepted')=(active_members IS NOT NULL)), CHECK((kind='Accepted')=(seat_hash IS NOT NULL)),
            CHECK(active_members BETWEEN 1 AND member_allowance), CHECK(length(seat_hash)=64 AND seat_hash !~ '[^0-9a-f]'),
            UNIQUE(replacement_id,kind), UNIQUE(shop_id,actor_id,operation_key)
        );
        CREATE TABLE public.coverage_replacement_activations (
            replacement_id uuid PRIMARY KEY, shop_id uuid NOT NULL REFERENCES public.shops(id),
            account_id uuid NOT NULL, event_id uuid NOT NULL UNIQUE, activated_at timestamptz NOT NULL
        );
        ALTER TABLE public.coverage_replacement_consent_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_consent_events FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_activations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_activations FORCE ROW LEVEL SECURITY;
        CREATE POLICY shop_read ON public.coverage_replacement_consent_events FOR SELECT USING
            (shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        CREATE POLICY owner_insert ON public.coverage_replacement_consent_events FOR INSERT WITH CHECK(
            shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid
            AND actor_id=nullif(current_setting('app.current_user_id',true),'')::uuid
            AND EXISTS(SELECT FROM public.shop_memberships m WHERE m.user_id=actor_id AND m.id=membership_id
                AND m.shop_id=coverage_replacement_consent_events.shop_id AND m.authority_version=coverage_replacement_consent_events.authority_version
                AND m.role='Owner' AND m.status='Active'));
        CREATE POLICY shop_read ON public.coverage_replacement_activations FOR SELECT USING
            (shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        CREATE POLICY shop_insert ON public.coverage_replacement_activations FOR INSERT WITH CHECK(
            shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        -- The projection transaction already holds the account fence exclusively. No outgoing permit can commit
        -- between reading the last issued deadline and recording the incoming activation. The function restores
        -- the caller's scope on return and exposes only the release decision.
        CREATE FUNCTION public.replacement_outgoing_released(change_id uuid, at_time timestamptz)
        RETURNS bool LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE consent public.coverage_replacement_consent_events%ROWTYPE;
                prior_scope text := current_setting('app.active_shop_id',true);
                released bool;
        BEGIN
            SELECT * INTO consent FROM public.coverage_replacement_consent_events e
                WHERE e.replacement_id=change_id AND e.shop_id=nullif(prior_scope,'')::uuid AND e.kind='Accepted';
            IF NOT FOUND OR consent.effective_at>at_time THEN RETURN false; END IF;
            PERFORM set_config('app.active_shop_id',consent.outgoing_shop_id::text,true);
            SELECT EXISTS(SELECT FROM public.shop_license_states s JOIN public.license_grant_evidence e ON e.event_id=s.evidence_id
                WHERE s.shop_id=consent.outgoing_shop_id AND s.account_id=consent.account_id
                    AND convert_from(e.payload,'UTF8')::jsonb->'shop'->>'releasedReplacementId'=consent.replacement_id::text
                    AND (s.state NOT IN ('Active','Grace') OR s.access_ends_at<=at_time))
                AND NOT EXISTS(SELECT FROM public.offline_license_permits p WHERE p.shop_id=consent.outgoing_shop_id
                    AND p.valid_until>at_time) INTO released;
            PERFORM set_config('app.active_shop_id',prior_scope,true);
            RETURN released;
        END $$;
        CREATE TRIGGER retain_replacement_consent BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_replacement_consent_events
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
        CREATE TRIGGER retain_replacement_activation BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_replacement_activations
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.coverage_replacement_consent_events, public.coverage_replacement_activations IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_replacement_consent_events NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_replacement_activations NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN IF EXISTS(SELECT FROM public.coverage_replacement_consent_events) OR EXISTS(SELECT FROM public.coverage_replacement_activations) THEN
            RAISE EXCEPTION 'Retained coverage replacement consent prevents downgrade' USING ERRCODE='55000';
        END IF; END $$;
        DROP FUNCTION public.replacement_outgoing_released(uuid,timestamptz);
        DROP TABLE public.coverage_replacement_activations, public.coverage_replacement_consent_events;
        DROP INDEX public.coverage_replacement_accepted_shop;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK(kind IN ({PreviousKinds}));
        """);
}
