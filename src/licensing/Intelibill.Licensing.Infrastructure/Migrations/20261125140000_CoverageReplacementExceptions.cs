using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-06: SQL-owned early coverage-replacement exceptions, private provider approvals with MFA/revision evidence,
/// append-only paid-slot handoffs and time-aware effective roster/guard functions. Apply before licensing writers;
/// Down refuses while any exception, approval or paid-slot handoff row is retained.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261125140000_CoverageReplacementExceptions")]
public sealed partial class CoverageReplacementExceptions : Migration
{
    private const string PreviousOutboxKinds =
        "'Snapshot','Acknowledgement','Reconciliation','CapacityReserve','CapacityConsume','ReductionManifest','ReductionReserve','ReductionConsume'";
    private const string PreviousInboxKinds =
        "'Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent','CoverageReplacementConsent'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(RosterFunctions);
        migrationBuilder.Sql(StatusFunctions);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(TransitionGuards);
        migrationBuilder.Sql(UpdatedGuards);
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ({PreviousOutboxKinds},'CoverageExceptionPrepare','CoverageExceptionConsume','CoverageExceptionAbort'));
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK (kind IN ({PreviousInboxKinds},'CoverageReplacementExceptionConsent','CoverageReplacementExceptionReceipt'));
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Claim(true, recoverReductions: true, recoverExceptions: true));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Finish(DeferWhileOutgoingPermissionRemains));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            LOCK TABLE public.coverage_replacement_exceptions, public.coverage_replacement_exception_approvals,
                public.paid_slot_handoffs IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.coverage_replacement_exceptions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.coverage_replacement_exception_approvals NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.paid_slot_handoffs NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.coverage_replacement_exceptions)
                    OR EXISTS (SELECT FROM public.coverage_replacement_exception_approvals)
                    OR EXISTS (SELECT FROM public.paid_slot_handoffs) THEN
                    RAISE EXCEPTION 'Retained coverage replacement exceptions prevent downgrade' USING ERRCODE = '55000';
                END IF;
            END $$;
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ({PreviousOutboxKinds}));
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK (kind IN ({PreviousInboxKinds}));
            """);
        migrationBuilder.Sql(RestorePreviousGuards);
        migrationBuilder.Sql(CoverageReplacements.CheckoutGuard);
        migrationBuilder.Sql(CoverageReplacements.LinkGuard);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Claim(true, recoverReductions: true));
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Finish(CoverageReplacements.DeferWhileOutgoingPermissionRemains));
        migrationBuilder.Sql("""
            DROP POLICY admin_prepare_update ON public.coverage_replacement_exceptions;
            DROP POLICY retire_obsolete ON public.coverage_replacement_exceptions;
            DROP POLICY receipt_insert ON public.paid_slot_handoffs;
            DROP TABLE public.paid_slot_handoffs;
            DROP TABLE public.coverage_replacement_exception_approvals;
            DROP TABLE public.coverage_replacement_exceptions;
            DROP FUNCTION public.guard_paid_slot_handoff();
            DROP FUNCTION public.guard_coverage_exception_approval();
            DROP FUNCTION public.guard_coverage_exception();
            DROP FUNCTION public.coverage_exception_leg_activation(uuid,text,timestamptz);
            DROP FUNCTION public.coverage_exception_stale(uuid,timestamptz);
            DROP FUNCTION public.coverage_exception_owner_current(uuid,uuid);
            DROP FUNCTION public.shop_effective_paid_link(uuid,uuid,uuid,timestamptz);
            DROP FUNCTION public.term_effective_roster(uuid,uuid,timestamptz);
            DROP FUNCTION public.term_paid_slot_intervals(uuid,uuid);
            """);
    }

    private const string Tables = """
        CREATE TABLE public.coverage_replacement_exceptions (
            exception_id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            term_id uuid NOT NULL,
            order_id uuid,
            proposal_revision bigint NOT NULL CHECK (proposal_revision > 0),
            outgoing_shop_id uuid NOT NULL,
            outgoing_shop_name text NOT NULL CHECK (length(outgoing_shop_name) BETWEEN 1 AND 250),
            outgoing_affiliation_id uuid NOT NULL,
            incoming_shop_id uuid NOT NULL,
            incoming_shop_name text NOT NULL CHECK (length(incoming_shop_name) BETWEEN 1 AND 250),
            incoming_affiliation_id uuid NOT NULL,
            capacity int NOT NULL CHECK (capacity > 0),
            member_allowance int NOT NULL CHECK (member_allowance > 0),
            normal_renewal_at timestamptz NOT NULL CHECK (isfinite(normal_renewal_at)),
            requested_early_start_at timestamptz NOT NULL CHECK (isfinite(requested_early_start_at)),
            paid_cutover_at timestamptz NOT NULL CHECK (isfinite(paid_cutover_at)),
            overlap_disposition text NOT NULL CHECK (overlap_disposition IN ('Wait', 'CourtesyBridge')),
            observed_permit_deadline timestamptz CHECK (observed_permit_deadline IS NULL OR isfinite(observed_permit_deadline)),
            observed_at timestamptz NOT NULL CHECK (isfinite(observed_at)),
            last_contact_at timestamptz CHECK (last_contact_at IS NULL OR isfinite(last_contact_at)),
            last_synchronized_at timestamptz CHECK (last_synchronized_at IS NULL OR isfinite(last_synchronized_at)),
            observed_permit_count int NOT NULL DEFAULT 0 CHECK (observed_permit_count >= 0),
            request_reason varchar(500) NOT NULL CHECK (length(btrim(request_reason)) BETWEEN 1 AND 500 AND request_reason !~ '[[:cntrl:]]'),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            expires_at timestamptz NOT NULL CHECK (isfinite(expires_at) AND created_at < expires_at AND expires_at <= paid_cutover_at AND expires_at <= normal_renewal_at),
            requester_id uuid NOT NULL,
            requester_revision int NOT NULL CHECK (requester_revision >= 0),
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[0-9a-f]{64}$'),
            source_hash text NOT NULL CHECK (source_hash ~ '^[0-9a-f]{64}$'),
            terms_digest text NOT NULL CHECK (terms_digest ~ '^[0-9a-f]{64}$'),
            state text NOT NULL CHECK (state IN ('AwaitingConsents','ReadyForReview','Preparing','Consuming','Applied','Refused','Blocked','Aborting')),
            reason text CHECK (reason IS NULL OR length(reason) BETWEEN 1 AND 120),
            outgoing_consent_event uuid UNIQUE,
            outgoing_revoke_event uuid UNIQUE,
            outgoing_actor_id uuid,
            outgoing_authority_version bigint,
            outgoing_active_members int CHECK (outgoing_active_members IS NULL OR outgoing_active_members >= 1),
            outgoing_seat_hash text CHECK (outgoing_seat_hash IS NULL OR outgoing_seat_hash ~ '^[0-9a-f]{64}$'),
            outgoing_consented_at timestamptz,
            incoming_consent_event uuid UNIQUE,
            incoming_revoke_event uuid UNIQUE,
            incoming_actor_id uuid,
            incoming_authority_version bigint,
            incoming_active_members int CHECK (incoming_active_members IS NULL OR incoming_active_members >= 1),
            incoming_seat_hash text CHECK (incoming_seat_hash IS NULL OR incoming_seat_hash ~ '^[0-9a-f]{64}$'),
            incoming_consented_at timestamptz,
            approved_at timestamptz,
            applied_at timestamptz,
            CHECK (outgoing_shop_id <> incoming_shop_id),
            CHECK (created_at < paid_cutover_at AND requested_early_start_at <= paid_cutover_at AND paid_cutover_at < normal_renewal_at),
            CHECK (overlap_disposition <> 'CourtesyBridge' OR requested_early_start_at < paid_cutover_at),
            CHECK (observed_permit_deadline IS NULL OR paid_cutover_at >= observed_permit_deadline),
            CHECK ((state = 'Applied') = (applied_at IS NOT NULL)),
            UNIQUE (account_id, requester_id, operation_key),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            FOREIGN KEY (account_id, outgoing_shop_id, outgoing_affiliation_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, incoming_shop_id, incoming_affiliation_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id)
        );
        CREATE UNIQUE INDEX coverage_exception_open_source ON public.coverage_replacement_exceptions(term_id, outgoing_shop_id)
            WHERE state IN ('AwaitingConsents','ReadyForReview','Preparing','Consuming','Aborting');
        CREATE UNIQUE INDEX coverage_exception_open_incoming ON public.coverage_replacement_exceptions(incoming_shop_id)
            WHERE state IN ('AwaitingConsents','ReadyForReview','Preparing','Consuming','Aborting');
        CREATE UNIQUE INDEX coverage_exception_inflight_term ON public.coverage_replacement_exceptions(term_id)
            WHERE state IN ('Preparing','Consuming','Aborting');

        CREATE TABLE public.coverage_replacement_exception_approvals (
            approval_id uuid PRIMARY KEY,
            exception_id uuid NOT NULL REFERENCES public.coverage_replacement_exceptions(exception_id),
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            term_id uuid NOT NULL,
            generation bigint NOT NULL CHECK (generation > 0),
            admin_id uuid NOT NULL REFERENCES public.admin_identities(id),
            session_id uuid NOT NULL,
            mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
            authorized_at timestamptz NOT NULL CHECK (isfinite(authorized_at) AND authorized_at >= mfa_verified_at
                AND authorized_at < mfa_verified_at + interval '5 minutes'),
            decided_at timestamptz NOT NULL CHECK (isfinite(decided_at)),
            operation_key uuid NOT NULL,
            payload_digest text NOT NULL CHECK (payload_digest ~ '^[0-9a-f]{64}$'),
            expected_proposal_revision bigint NOT NULL CHECK (expected_proposal_revision > 0),
            expected_source_hash text NOT NULL CHECK (expected_source_hash ~ '^[0-9a-f]{64}$'),
            expected_terms_digest text NOT NULL CHECK (expected_terms_digest ~ '^[0-9a-f]{64}$'),
            expected_grant_version bigint NOT NULL CHECK (expected_grant_version >= 0),
            disposition text NOT NULL CHECK (disposition IN ('Wait', 'CourtesyBridge')),
            reason varchar(500) NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 500 AND reason = btrim(reason) AND reason !~ '[[:cntrl:]]'),
            prepare_event uuid UNIQUE NOT NULL REFERENCES public.entitlement_messages(event_id),
            prepare_expires_at timestamptz NOT NULL CHECK (isfinite(prepare_expires_at)),
            consume_event uuid UNIQUE REFERENCES public.entitlement_messages(event_id),
            abort_event uuid UNIQUE REFERENCES public.entitlement_messages(event_id),
            state text NOT NULL CHECK (state IN ('Preparing','Consuming','Applied','Refused','Aborting','Aborted')),
            outcome_reason text CHECK (outcome_reason IS NULL OR length(outcome_reason) BETWEEN 1 AND 120),
            merchant_permit_count int CHECK (merchant_permit_count IS NULL OR merchant_permit_count >= 0),
            merchant_max_permit_end timestamptz CHECK (merchant_max_permit_end IS NULL OR isfinite(merchant_max_permit_end)),
            merchant_permit_digest text CHECK (merchant_permit_digest IS NULL OR merchant_permit_digest ~ '^[0-9a-f]{64}$'),
            consume_receipt_event uuid UNIQUE,
            courtesy_grant_id uuid UNIQUE REFERENCES public.complimentary_grants(grant_id),
            courtesy_term_id uuid UNIQUE,
            applied_at timestamptz CHECK (applied_at IS NULL OR isfinite(applied_at)),
            UNIQUE (account_id, operation_key),
            UNIQUE (exception_id, generation),
            CHECK ((state = 'Applied') = (applied_at IS NOT NULL)),
            CHECK ((courtesy_grant_id IS NULL) = (courtesy_term_id IS NULL)),
            CHECK (disposition <> 'Wait' OR courtesy_grant_id IS NULL)
        );
        CREATE UNIQUE INDEX coverage_exception_active_approval ON public.coverage_replacement_exception_approvals(exception_id)
            WHERE state IN ('Preparing','Consuming','Applied','Aborting');

        CREATE TABLE public.paid_slot_handoffs (
            handoff_id uuid PRIMARY KEY,
            exception_id uuid NOT NULL UNIQUE REFERENCES public.coverage_replacement_exceptions(exception_id),
            approval_id uuid NOT NULL UNIQUE REFERENCES public.coverage_replacement_exception_approvals(approval_id),
            generation bigint NOT NULL CHECK (generation > 0),
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            term_id uuid NOT NULL,
            slot_origin_shop_id uuid NOT NULL,
            slot_generation int NOT NULL CHECK (slot_generation > 0),
            outgoing_shop_id uuid NOT NULL,
            outgoing_affiliation_id uuid NOT NULL,
            incoming_shop_id uuid NOT NULL,
            incoming_affiliation_id uuid NOT NULL,
            previous_starts_at timestamptz NOT NULL CHECK (isfinite(previous_starts_at)),
            cutover_at timestamptz NOT NULL CHECK (isfinite(cutover_at)),
            original_ends_at timestamptz NOT NULL CHECK (isfinite(original_ends_at)),
            courtesy_grant_id uuid UNIQUE REFERENCES public.complimentary_grants(grant_id),
            courtesy_term_id uuid UNIQUE,
            courtesy_starts_at timestamptz CHECK (courtesy_starts_at IS NULL OR isfinite(courtesy_starts_at)),
            consume_receipt_event uuid NOT NULL UNIQUE,
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            CHECK (outgoing_shop_id <> incoming_shop_id),
            CHECK (previous_starts_at < cutover_at AND cutover_at < original_ends_at),
            CHECK ((courtesy_grant_id IS NULL) = (courtesy_term_id IS NULL) AND (courtesy_grant_id IS NULL) = (courtesy_starts_at IS NULL)),
            CHECK (courtesy_starts_at IS NULL OR (courtesy_starts_at >= recorded_at AND courtesy_starts_at < cutover_at)),
            UNIQUE (account_id, term_id, slot_origin_shop_id, slot_generation),
            UNIQUE (account_id, term_id, outgoing_shop_id, previous_starts_at),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            FOREIGN KEY (account_id, outgoing_shop_id, outgoing_affiliation_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, incoming_shop_id, incoming_affiliation_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id)
        );
        CREATE INDEX paid_slot_handoffs_term ON public.paid_slot_handoffs(account_id, term_id, slot_origin_shop_id, slot_generation);
        CREATE INDEX paid_slot_handoffs_incoming ON public.paid_slot_handoffs(account_id, incoming_shop_id, term_id);
        CREATE INDEX paid_slot_handoffs_outgoing ON public.paid_slot_handoffs(account_id, outgoing_shop_id, term_id);
        """;
}
