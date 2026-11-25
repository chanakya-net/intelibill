using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

/// <summary>
/// P06-07a: append-only shop-owner coverage-transfer consent events and the review-time inspection licensing asks
/// for. Evidence only: no licence state, affiliation, membership or permit is changed. Apply before API writers;
/// Down refuses while any consent event is retained.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261126110000_AddMerchantCoverageTransferConsents")]
public sealed class AddMerchantCoverageTransferConsents : Migration
{
    private const string PreviousKinds =
        "'Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent'," +
        "'CoverageReplacementConsent','CoverageReplacementExceptionConsent','CoverageReplacementExceptionReceipt'";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($$"""
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK (kind IN ({{PreviousKinds}},'CoverageTransferConsent'));

        CREATE TABLE public.coverage_transfer_consent_events (
            id uuid PRIMARY KEY,
            shop_id uuid NOT NULL REFERENCES public.shops(id),
            account_id uuid NOT NULL,
            destination_account_id uuid NOT NULL,
            transfer_id uuid NOT NULL,
            proposal_revision bigint NOT NULL CHECK (proposal_revision > 0),
            terms_digest text NOT NULL CHECK (terms_digest ~ '^[0-9a-f]{64}$'),
            affiliation_request_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            membership_id uuid NOT NULL,
            authority_version bigint NOT NULL CHECK (authority_version >= 0),
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
            generation int NOT NULL CHECK (generation > 0),
            kind text NOT NULL CHECK (kind IN ('Accepted', 'Revoked')),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            CHECK (account_id <> destination_account_id),
            UNIQUE (transfer_id, generation, kind),
            UNIQUE (shop_id, actor_id, operation_key)
        );
        ALTER TABLE public.coverage_transfer_consent_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_consent_events FORCE ROW LEVEL SECURITY;
        CREATE POLICY shop_read ON public.coverage_transfer_consent_events FOR SELECT USING (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
        CREATE POLICY owner_insert ON public.coverage_transfer_consent_events FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.current_user_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.shop_memberships m WHERE m.user_id = actor_id AND m.id = membership_id
                AND m.shop_id = coverage_transfer_consent_events.shop_id
                AND m.authority_version = coverage_transfer_consent_events.authority_version
                AND m.role = 'Owner' AND m.status = 'Active'));
        CREATE TRIGGER retain_transfer_consent BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_transfer_consent_events
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();

        -- One fenced read for licensing's signed review check. NULL reason means every bound fact is still current.
        CREATE FUNCTION public.coverage_transfer_shop_inspect(
            p_account uuid, p_shop uuid, p_affiliation uuid, p_transfer uuid, p_event uuid, p_actor uuid,
            p_membership uuid, p_version bigint, p_revision bigint, p_digest text, p_at timestamptz)
        RETURNS TABLE (reason text, permit_count int, max_permit_valid_until timestamptz)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH gate AS (
                SELECT p_account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
                    AND p_shop = nullif(current_setting('app.active_shop_id', true), '')::uuid
                    AND EXISTS (SELECT FROM public.entitlement_request_uses u
                        WHERE u.replay_id = nullif(current_setting('app.entitlement_replay_id', true), '')::uuid
                            AND u.xmin = pg_current_xact_id()::xid) AS ok
            ), consent AS (
                SELECT e.generation FROM public.coverage_transfer_consent_events e
                WHERE e.id = p_event AND e.transfer_id = p_transfer AND e.kind = 'Accepted' AND e.shop_id = p_shop
                    AND e.account_id = p_account AND e.affiliation_request_id = p_affiliation
                    AND e.proposal_revision = p_revision AND e.terms_digest = p_digest AND e.actor_id = p_actor
                    AND e.membership_id = p_membership AND e.authority_version = p_version
            ), permits AS (
                SELECT count(*)::int AS cnt, max(p.valid_until) AS max_end
                FROM public.offline_license_permits p WHERE p.shop_id = p_shop AND p.valid_until > p_at
            )
            SELECT CASE
                WHEN NOT EXISTS (SELECT FROM consent) THEN 'ShopConsentMissing'
                WHEN EXISTS (SELECT FROM public.coverage_transfer_consent_events v, consent c
                    WHERE v.transfer_id = p_transfer
                        AND (v.generation > c.generation OR (v.generation = c.generation AND v.kind = 'Revoked')))
                    THEN 'ShopConsentSuperseded'
                WHEN NOT EXISTS (SELECT FROM public.shop_memberships m WHERE m.id = p_membership AND m.shop_id = p_shop
                    AND m.user_id = p_actor AND m.role = 'Owner' AND m.status = 'Active' AND m.authority_version = p_version)
                    THEN 'ShopOwnerChanged'
                WHEN NOT EXISTS (SELECT FROM public.shop_license_states s WHERE s.shop_id = p_shop
                    AND s.account_id = p_account AND s.affiliation_request_id = p_affiliation AND s.state <> 'Closed')
                    THEN 'AffiliationChanged'
            END, pm.cnt, pm.max_end
            FROM gate g CROSS JOIN permits pm WHERE g.ok
        $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($$"""
        LOCK TABLE public.coverage_transfer_consent_events IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_transfer_consent_events NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.coverage_transfer_consent_events) THEN
                RAISE EXCEPTION 'Retained coverage transfer consents prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP FUNCTION public.coverage_transfer_shop_inspect(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,timestamptz);
        DROP TABLE public.coverage_transfer_consent_events;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK (kind IN ({{PreviousKinds}}));
        """);
}
