using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261126130000_MerchantCoverageTransferApplications")]
public sealed class MerchantCoverageTransferApplications : Migration
{
    private const string Old = "'Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'," +
        "'CoverageSlotConsent','CoverageReplacementConsent','CoverageReplacementExceptionConsent','CoverageReplacementExceptionReceipt','CoverageTransferConsent'";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK(kind IN ({Old},'TransferReceipt'));
        CREATE TABLE public.merchant_transfer_decisions (
            event_id uuid PRIMARY KEY, transfer_id uuid NOT NULL, shop_id uuid NOT NULL REFERENCES public.shops,
            source_account_id uuid NOT NULL, destination_account_id uuid NOT NULL CHECK(destination_account_id<>source_account_id),
            affiliation_id uuid NOT NULL, cutover_at timestamptz NOT NULL, paid_through_at timestamptz NOT NULL CHECK(paid_through_at>=cutover_at),
            phase text NOT NULL CHECK(phase IN ('TransferPrepare','TransferConsume','TransferAbort')),
            outcome text NOT NULL CHECK(outcome IN ('Prepared','Consumed','Aborted','Refused')),
            digest text NOT NULL, command bytea NOT NULL, recorded_at timestamptz NOT NULL,
            UNIQUE(transfer_id,phase)
        );
        ALTER TABLE public.merchant_transfer_decisions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_transfer_decisions FORCE ROW LEVEL SECURITY;
        CREATE POLICY shop_read ON public.merchant_transfer_decisions FOR SELECT USING (
            shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid);
        CREATE POLICY verified_command ON public.merchant_transfer_decisions FOR INSERT WITH CHECK (
            source_account_id=nullif(current_setting('app.entitlement_account_id',true),'')::uuid
            AND shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid
            AND EXISTS(SELECT FROM public.entitlement_request_uses u WHERE replay_id=nullif(current_setting('app.entitlement_replay_id',true),'')::uuid
                AND u.xmin=pg_current_xact_id()::xid));
        CREATE TRIGGER retain_transfer_decision BEFORE UPDATE OR DELETE OR TRUNCATE ON public.merchant_transfer_decisions
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        DO $$ BEGIN EXECUTE format('CREATE POLICY scan_bindings ON public.merchant_transfer_decisions FOR SELECT TO %I USING(true)',current_user); END $$;
        CREATE VIEW public.merchant_entitlement_bindings WITH (security_invoker=true) AS
            SELECT e.id,e.shop_id,e.account_id,e.request_id,e.kind FROM public.shop_affiliation_consent_events e
            WHERE NOT EXISTS (SELECT FROM public.shop_license_states s WHERE s.shop_id=e.shop_id
                AND s.affiliation_request_id<>e.request_id)
            UNION ALL SELECT d.transfer_id,d.shop_id,d.destination_account_id,d.transfer_id,'Accepted'
            FROM public.merchant_transfer_decisions d JOIN public.shop_license_states s
                ON s.shop_id=d.shop_id AND s.account_id=d.destination_account_id AND s.affiliation_request_id=d.transfer_id
            WHERE d.outcome='Consumed';
        {BoundMerchantEntitlementReconciliation.MinuteScan.Replace("public.shop_affiliation_consent_events", "public.merchant_entitlement_bindings", StringComparison.Ordinal)}
        ALTER FUNCTION public.merchant_licence_proof_issuance(uuid) RENAME TO original_licence_proof_issuance;
        CREATE FUNCTION public.merchant_licence_proof_issuance(p_shop_id uuid)
        RETURNS TABLE(shop_issued_at timestamptz, account_issued_at timestamptz)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog AS $$
            WITH transferred AS (
                SELECT se.issued_at AS shop_issued_at, ae.issued_at AS account_issued_at
                FROM public.shop_license_states s
                JOIN public.merchant_transfer_decisions d ON d.transfer_id=s.affiliation_request_id
                    AND d.destination_account_id=s.account_id AND d.shop_id=s.shop_id AND d.outcome='Consumed'
                JOIN public.license_grant_evidence se ON se.event_id=s.evidence_id AND se.shop_id=s.shop_id
                LEFT JOIN public.account_license_restrictions a ON a.account_id=s.account_id
                LEFT JOIN public.license_grant_evidence ae ON ae.event_id=a.evidence_id AND ae.account_id=s.account_id
                WHERE s.shop_id=p_shop_id AND s.shop_id=nullif(current_setting('app.active_shop_id',true),'')::uuid
            )
            SELECT * FROM transferred UNION ALL
            SELECT * FROM public.original_licence_proof_issuance(p_shop_id) WHERE NOT EXISTS (SELECT FROM transferred)
        $$;
        CREATE FUNCTION public.transfer_source_cutoff(shop uuid, affiliation uuid) RETURNS timestamptz
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT min(p.cutover_at) FROM public.merchant_transfer_decisions p WHERE p.shop_id=shop
                AND p.affiliation_id=affiliation AND p.outcome='Prepared' AND NOT EXISTS (
                    SELECT FROM public.merchant_transfer_decisions e WHERE e.transfer_id=p.transfer_id AND e.outcome IN ('Aborted','Refused'))
        $$;
        CREATE FUNCTION public.guard_transfer_permit() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF NEW.valid_until > (SELECT public.transfer_source_cutoff(NEW.shop_id,g.affiliation_request_id)
                FROM public.offline_license_grants g WHERE g.id=NEW.grant_id AND g.shop_id=NEW.shop_id) THEN
                RAISE EXCEPTION 'Permit crosses coverage transfer' USING ERRCODE='23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_transfer_permit BEFORE INSERT ON public.offline_license_permits
            FOR EACH ROW EXECUTE FUNCTION public.guard_transfer_permit();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.merchant_transfer_decisions IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.merchant_transfer_decisions NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN IF EXISTS(SELECT FROM public.merchant_transfer_decisions) THEN
            RAISE EXCEPTION 'Retained transfer decisions prevent downgrade' USING ERRCODE='55000'; END IF; END $$;
        DROP TRIGGER guard_transfer_permit ON public.offline_license_permits;
        DROP FUNCTION public.guard_transfer_permit(), public.transfer_source_cutoff(uuid,uuid);
        DROP FUNCTION public.merchant_licence_proof_issuance(uuid);
        ALTER FUNCTION public.original_licence_proof_issuance(uuid) RENAME TO merchant_licence_proof_issuance;
        {BoundMerchantEntitlementReconciliation.MinuteScan}
        DROP VIEW public.merchant_entitlement_bindings;
        DROP TABLE public.merchant_transfer_decisions;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check, ADD CHECK(kind IN ({Old}));
        """);
}
