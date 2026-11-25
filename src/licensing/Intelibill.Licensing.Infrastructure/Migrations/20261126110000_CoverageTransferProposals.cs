using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-07a: SQL-owned inter-account coverage-transfer proposals, private per-account subscription evidence and
/// append-only purpose-bound owner consents. Nothing here writes coverage, affiliation, grant or payment facts.
/// Apply before licensing writers; Down refuses while any proposal, evidence or consent row is retained.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261126110000_CoverageTransferProposals")]
public sealed partial class CoverageTransferProposals : Migration
{
    private const string PreviousInboxKinds =
        "'Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent'," +
        "'CoverageReplacementConsent','CoverageReplacementExceptionConsent','CoverageReplacementExceptionReceipt'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(Functions);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK (kind IN ({PreviousInboxKinds},'CoverageTransferConsent'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.coverage_transfer_proposals, public.coverage_transfer_evidence,
            public.coverage_transfer_consents IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_transfer_proposals NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_evidence NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_consents NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.coverage_transfer_proposals)
                OR EXISTS (SELECT FROM public.coverage_transfer_evidence)
                OR EXISTS (SELECT FROM public.coverage_transfer_consents) THEN
                RAISE EXCEPTION 'Retained coverage transfer proposals prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
            ADD CHECK (kind IN ({PreviousInboxKinds}));
        DROP FUNCTION public.coverage_transfer_blocker(uuid,timestamptz);
        DROP FUNCTION public.coverage_transfer_owner_current(uuid,uuid,uuid,bigint);
        DROP FUNCTION public.coverage_transfer_freeze_evidence(uuid);
        DROP FUNCTION public.coverage_transfer_evidence_digest(uuid,uuid);
        DROP FUNCTION public.coverage_transfer_party_terms(uuid);
        DROP FUNCTION public.coverage_transfer_permit_digest(uuid,uuid,uuid,timestamptz);
        DROP FUNCTION public.coverage_transfer_account_exists(uuid);
        DROP POLICY coverage_transfer_function_read ON public.billing_accounts;
        DROP TABLE public.coverage_transfer_consents;
        DROP TABLE public.coverage_transfer_evidence;
        DROP TABLE public.coverage_transfer_proposals;
        DROP FUNCTION public.guard_coverage_transfer_proposal();
        """);

    private const string Tables = """
        CREATE TABLE public.coverage_transfer_proposals (
            transfer_id uuid PRIMARY KEY,
            proposal_revision bigint NOT NULL CHECK (proposal_revision > 0),
            source_account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            source_account_name varchar(120) NOT NULL CHECK (length(source_account_name) >= 1),
            destination_account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            source_term_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            shop_name text NOT NULL CHECK (length(shop_name) BETWEEN 1 AND 250),
            source_affiliation_id uuid NOT NULL,
            treatment text NOT NULL CHECK (treatment IN ('RetainUntilPaidThrough', 'ReviewedAllocation')),
            cutover_at timestamptz CHECK (cutover_at IS NULL OR isfinite(cutover_at)),
            source_paid_through_at timestamptz NOT NULL CHECK (isfinite(source_paid_through_at)),
            observed_permit_count int NOT NULL CHECK (observed_permit_count >= 0),
            observed_permit_deadline timestamptz CHECK (observed_permit_deadline IS NULL OR isfinite(observed_permit_deadline)),
            last_contact_at timestamptz CHECK (last_contact_at IS NULL OR isfinite(last_contact_at)),
            observed_at timestamptz NOT NULL CHECK (isfinite(observed_at)),
            permit_digest text NOT NULL CHECK (permit_digest ~ '^[0-9a-f]{64}$'),
            evidence_digest text NOT NULL CHECK (evidence_digest ~ '^[0-9a-f]{64}$'),
            terms_digest text NOT NULL CHECK (terms_digest ~ '^[0-9a-f]{64}$'),
            request_reason varchar(500) NOT NULL CHECK (length(btrim(request_reason)) BETWEEN 1 AND 500 AND request_reason !~ '[[:cntrl:]]'),
            requester_id uuid NOT NULL,
            requester_member_id uuid NOT NULL,
            requester_revision int NOT NULL CHECK (requester_revision > 0),
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            expires_at timestamptz NOT NULL CHECK (isfinite(expires_at)),
            state text NOT NULL CHECK (state IN ('Open', 'Retired')),
            retired_reason text CHECK (retired_reason IS NULL OR length(retired_reason) BETWEEN 1 AND 60),
            retired_at timestamptz CHECK (retired_at IS NULL OR isfinite(retired_at)),
            CHECK (source_account_id <> destination_account_id),
            CHECK ((treatment = 'ReviewedAllocation') = (cutover_at IS NOT NULL)),
            CHECK (cutover_at IS NULL OR (created_at < cutover_at AND cutover_at < source_paid_through_at)),
            CHECK (created_at < expires_at AND expires_at <= source_paid_through_at),
            CHECK (cutover_at IS NULL OR expires_at <= cutover_at),
            CHECK ((state = 'Retired') = (retired_reason IS NOT NULL) AND (state = 'Retired') = (retired_at IS NOT NULL)),
            UNIQUE (source_account_id, requester_id, operation_key),
            FOREIGN KEY (source_account_id, source_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            FOREIGN KEY (source_account_id, shop_id, source_affiliation_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id)
        );
        -- One open proposal per named shop: competing destinations or directions yield one winner.
        CREATE UNIQUE INDEX coverage_transfer_open_shop ON public.coverage_transfer_proposals(shop_id) WHERE state = 'Open';
        CREATE INDEX coverage_transfer_destination ON public.coverage_transfer_proposals(destination_account_id, created_at);

        -- One private row per independent subscription on each side. Only its own account can read it.
        CREATE TABLE public.coverage_transfer_evidence (
            transfer_id uuid NOT NULL REFERENCES public.coverage_transfer_proposals(transfer_id),
            side text NOT NULL CHECK (side IN ('Source', 'Destination')),
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            term_id uuid NOT NULL,
            mode text NOT NULL,
            capacity int NOT NULL CHECK (capacity > 0),
            starts_at timestamptz NOT NULL CHECK (isfinite(starts_at)),
            ends_at timestamptz NOT NULL CHECK (isfinite(ends_at)),
            paid_through_at timestamptz CHECK (paid_through_at IS NULL OR isfinite(paid_through_at)),
            access_ends_at timestamptz CHECK (access_ends_at IS NULL OR isfinite(access_ends_at)),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            PRIMARY KEY (transfer_id, side, term_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );

        CREATE TABLE public.coverage_transfer_consents (
            event_id uuid PRIMARY KEY,
            transfer_id uuid NOT NULL REFERENCES public.coverage_transfer_proposals(transfer_id),
            source_account_id uuid NOT NULL,
            destination_account_id uuid NOT NULL,
            purpose text NOT NULL CHECK (purpose IN ('SourceBillingOwner', 'DestinationBillingOwner', 'ShopOwner')),
            generation int NOT NULL CHECK (generation > 0),
            kind text NOT NULL CHECK (kind IN ('Accepted', 'Revoked')),
            account_id uuid NOT NULL,
            account_name varchar(120),
            actor_id uuid NOT NULL,
            member_id uuid NOT NULL,
            authority_revision bigint NOT NULL CHECK (authority_revision >= 0),
            proposal_revision bigint NOT NULL CHECK (proposal_revision > 0),
            terms_digest text NOT NULL CHECK (terms_digest ~ '^[0-9a-f]{64}$'),
            operation_key uuid,
            request_hash text CHECK (request_hash IS NULL OR request_hash ~ '^[0-9a-f]{64}$'),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            CHECK ((purpose = 'ShopOwner') = (operation_key IS NULL) AND (operation_key IS NULL) = (request_hash IS NULL)),
            CHECK (account_id = CASE purpose WHEN 'DestinationBillingOwner' THEN destination_account_id ELSE source_account_id END),
            UNIQUE (transfer_id, purpose, generation, kind)
        );
        CREATE UNIQUE INDEX coverage_transfer_consent_key ON public.coverage_transfer_consents(account_id, actor_id, operation_key)
            WHERE operation_key IS NOT NULL;
        """;
}
