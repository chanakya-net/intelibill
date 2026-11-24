using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-03: SQL-owned owner-selected capacity reduction previews, scheduled reductions, resolutions and renewal-boundary
/// fulfilment exceptions. Apply before licensing writers; Down refuses while any reduction evidence or order exists.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261124140000_ScheduledCapacityReductions")]
public sealed partial class ScheduledCapacityReductions : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id', true), '')::uuid";
    private const string Receipt = "nullif(current_setting('app.manual_receipt_id', true), '')::uuid";
    private const string ManualOrder = "nullif(current_setting('app.manual_order_id', true), '')::uuid";
    private const string DueScan = "current_setting('app.reduction_due_scan', true) = '1'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        ConfigurePolicies(migrationBuilder);
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ('Snapshot','Acknowledgement','Reconciliation','CapacityReserve','CapacityConsume',
                    'ReductionManifest','ReductionReserve','ReductionConsume'));
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK (kind IN ('Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt'));
            ALTER TABLE public.manual_payment_receipts DROP CONSTRAINT manual_payment_receipts_review_reason_check,
                ADD CHECK (review_reason IN ('DuplicateFunds','CoverageChanged','TermUnavailable','RestorationPending',
                    'CapacityPending','ReductionPending'));
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Claim(true, recoverReductions: true));
        migrationBuilder.Sql(DueFunction);
        migrationBuilder.Sql(ReductionGuards);
        migrationBuilder.Sql(CheckoutOrderGuard);
        migrationBuilder.Sql(PaymentGuards);
        migrationBuilder.Sql(Confirmation);
        migrationBuilder.Sql(FulfilmentCompleteness());
        migrationBuilder.Sql(UnmatchedPaymentReviews.SourceGuard(PaidCapacityIncreases.ManualCapacityReason + ManualReductionReason));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.checkout_orders, public.capacity_reduction_previews, public.capacity_reduction_preview_shops,
                public.scheduled_capacity_reductions, public.scheduled_capacity_reduction_shops,
                public.scheduled_capacity_reduction_resolutions IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.checkout_orders NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.capacity_reduction_previews NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.scheduled_capacity_reductions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.scheduled_capacity_reduction_resolutions NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.capacity_reduction_previews)
                    OR EXISTS (SELECT FROM public.scheduled_capacity_reductions)
                    OR EXISTS (SELECT FROM public.scheduled_capacity_reduction_resolutions)
                    OR EXISTS (SELECT FROM public.checkout_orders WHERE snapshot->'reduction' IS NOT NULL) THEN
                    RAISE EXCEPTION 'Retained capacity-reduction evidence prevents downgrade' USING ERRCODE = '55000';
                END IF;
            END $$;
            ALTER TABLE public.checkout_orders FORCE ROW LEVEL SECURITY;
            """);
        DropPolicies(migrationBuilder);
        migrationBuilder.Sql(UnmatchedPaymentReviews.SourceGuard(PaidCapacityIncreases.ManualCapacityReason));
        migrationBuilder.Sql(RestoreFulfilmentCompleteness());
        migrationBuilder.Sql(PaidCapacityIncreases.PaymentGuards);
        migrationBuilder.Sql(RestoreCheckoutOrderGuard);
        migrationBuilder.Sql("""
            DROP FUNCTION public.payment_confirmation_snapshot(uuid,uuid,uuid,uuid,uuid,timestamptz,text);
            ALTER FUNCTION public.payment_confirmation_before_reductions(uuid,uuid,uuid,uuid,uuid,timestamptz,text)
                RENAME TO payment_confirmation_snapshot;
            DROP FUNCTION public.due_scheduled_capacity_reductions(timestamptz);
            DROP TABLE public.scheduled_capacity_reduction_resolutions,
                public.scheduled_capacity_reduction_shops,
                public.scheduled_capacity_reductions,
                public.capacity_reduction_preview_shops,
                public.capacity_reduction_previews;
            DROP FUNCTION public.guard_reduction_preview_shop(), public.require_reduction_preview_seal(),
                public.guard_reduction_preview(), public.guard_scheduled_reduction_shop(),
                public.require_scheduled_reduction_seal(), public.guard_scheduled_reduction();
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ('Snapshot','Acknowledgement','Reconciliation','CapacityReserve','CapacityConsume'));
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK (kind IN ('Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt'));
            ALTER TABLE public.manual_payment_receipts DROP CONSTRAINT manual_payment_receipts_review_reason_check,
                ADD CHECK (review_reason IN ('DuplicateFunds','CoverageChanged','TermUnavailable','RestorationPending','CapacityPending'));
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Claim(true));
    }

    private static readonly string DueFunction = $$"""
        CREATE FUNCTION public.due_scheduled_capacity_reductions(p_now timestamptz)
        RETURNS TABLE (account_id uuid, schedule_id uuid)
        LANGUAGE sql SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT r.account_id, r.schedule_id
            FROM public.scheduled_capacity_reductions r
            WHERE {{LeasedHttpOutboxSchema.Unscoped}}
                AND r.state = 'Funded' AND r.effective_at <= p_now
            ORDER BY r.effective_at, r.schedule_id LIMIT 50
        $$;
        REVOKE ALL ON FUNCTION public.due_scheduled_capacity_reductions(timestamptz) FROM PUBLIC;
        """;

    private const string Tables = """
        CREATE TABLE public.capacity_reduction_previews (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            preview_id uuid NOT NULL,
            term_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            source_plan_version_id uuid,
            target_plan_version_id uuid NOT NULL,
            prior_capacity int NOT NULL CHECK (prior_capacity BETWEEN 1 AND 100000),
            target_capacity int NOT NULL CHECK (target_capacity BETWEEN 1 AND prior_capacity),
            prior_member_allowance int NOT NULL CHECK (prior_member_allowance BETWEEN 1 AND 100000),
            target_member_allowance int NOT NULL CHECK (target_member_allowance BETWEEN 1 AND prior_member_allowance),
            CHECK (target_capacity < prior_capacity OR target_member_allowance < prior_member_allowance),
            target_mode text NOT NULL CHECK (target_mode IN ('Independent', 'Grouped')),
            source_shop_count int NOT NULL CHECK (source_shop_count BETWEEN 1 AND prior_capacity),
            source_shop_hash text NOT NULL CHECK (source_shop_hash ~ '^[a-f0-9]{64}$'),
            source_hash text NOT NULL CHECK (source_hash ~ '^[a-f0-9]{64}$'),
            observation_digest text NOT NULL CHECK (observation_digest ~ '^[a-f0-9]{64}$'),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            current_starts_at timestamptz NOT NULL CHECK (isfinite(current_starts_at)),
            current_paid_through timestamptz NOT NULL CHECK (isfinite(current_paid_through) AND current_paid_through > current_starts_at),
            renewal_ends_at timestamptz NOT NULL CHECK (isfinite(renewal_ends_at) AND renewal_ends_at > current_paid_through),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            expires_at timestamptz NOT NULL CHECK (expires_at = created_at + interval '15 minutes'),
            snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[a-f0-9]{64}$'),
            snapshot jsonb NOT NULL CHECK (jsonb_typeof(snapshot) = 'object'),
            PRIMARY KEY (account_id, preview_id), UNIQUE (account_id, operation_key), UNIQUE (replay_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );

        CREATE TABLE public.capacity_reduction_preview_shops (
            account_id uuid NOT NULL,
            preview_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            shop_name text NOT NULL CHECK (length(shop_name) BETWEEN 1 AND 250),
            affiliation_request_id uuid NOT NULL,
            active_members int NOT NULL CHECK (active_members >= 0),
            seat_hash text NOT NULL CHECK (seat_hash ~ '^[a-f0-9]{64}$'),
            exceeds_allowance boolean NOT NULL,
            replay_id uuid NOT NULL,
            PRIMARY KEY (account_id, preview_id, shop_id),
            FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, preview_id) REFERENCES public.capacity_reduction_previews(account_id, preview_id)
                DEFERRABLE INITIALLY DEFERRED
        );

        CREATE TABLE public.scheduled_capacity_reductions (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            schedule_id uuid NOT NULL,
            preview_id uuid NOT NULL,
            term_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            state text NOT NULL CHECK (state IN ('Scheduled','Funded','Applying','Applied','NeedsResolution','NeedsReview')),
            reason text CHECK (length(reason) BETWEEN 1 AND 80),
            conflicts jsonb CHECK (conflicts IS NULL OR jsonb_typeof(conflicts) = 'array'),
            target_plan_version_id uuid NOT NULL,
            prior_capacity int NOT NULL CHECK (prior_capacity BETWEEN 1 AND 100000),
            target_capacity int NOT NULL CHECK (target_capacity BETWEEN 1 AND prior_capacity),
            prior_member_allowance int NOT NULL CHECK (prior_member_allowance BETWEEN 1 AND 100000),
            target_member_allowance int NOT NULL CHECK (target_member_allowance BETWEEN 1 AND prior_member_allowance),
            CHECK (target_capacity < prior_capacity OR target_member_allowance < prior_member_allowance),
            target_mode text NOT NULL CHECK (target_mode IN ('Independent', 'Grouped')),
            source_shop_count int NOT NULL CHECK (source_shop_count BETWEEN 1 AND prior_capacity),
            source_shop_hash text NOT NULL CHECK (source_shop_hash ~ '^[a-f0-9]{64}$'),
            retained_shop_count int NOT NULL CHECK (retained_shop_count BETWEEN 1 AND target_capacity AND retained_shop_count <= source_shop_count),
            retained_shop_hash text NOT NULL CHECK (retained_shop_hash ~ '^[a-f0-9]{64}$'),
            source_hash text NOT NULL CHECK (source_hash ~ '^[a-f0-9]{64}$'),
            observation_digest text NOT NULL CHECK (observation_digest ~ '^[a-f0-9]{64}$'),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND 1000000000000),
            effective_at timestamptz NOT NULL CHECK (isfinite(effective_at)),
            renewal_ends_at timestamptz NOT NULL CHECK (isfinite(renewal_ends_at) AND renewal_ends_at > effective_at),
            expected_source_revision bigint NOT NULL CHECK (expected_source_revision >= 0),
            resolution_generation bigint NOT NULL DEFAULT 0 CHECK (resolution_generation >= 0),
            order_id uuid,
            attempt_id uuid UNIQUE REFERENCES public.verified_payments(attempt_id),
            receipt_id uuid UNIQUE,
            payment_effective_at timestamptz CHECK (isfinite(payment_effective_at)),
            grace_days_included int CHECK (grace_days_included BETWEEN 0 AND 7),
            funded_at timestamptz CHECK (isfinite(funded_at)),
            reserve_event uuid UNIQUE REFERENCES public.entitlement_messages(event_id),
            consume_event uuid UNIQUE REFERENCES public.entitlement_messages(event_id),
            reserve_expires_at timestamptz CHECK (isfinite(reserve_expires_at)),
            successor_term_id uuid,
            applied_at timestamptz CHECK (isfinite(applied_at)),
            created_at timestamptz NOT NULL CHECK (isfinite(created_at)),
            snapshot_hash text NOT NULL CHECK (snapshot_hash ~ '^[a-f0-9]{64}$'),
            snapshot jsonb NOT NULL CHECK (jsonb_typeof(snapshot) = 'object'),
            PRIMARY KEY (account_id, schedule_id), UNIQUE (account_id, operation_key), UNIQUE (replay_id),
            CHECK (order_id IS NULL OR num_nonnulls(attempt_id, receipt_id) = 1),
            CHECK ((state = 'Scheduled') = (order_id IS NULL)),
            CHECK ((state IN ('NeedsResolution', 'NeedsReview')) = (reason IS NOT NULL)),
            CHECK ((state = 'Applied') = (applied_at IS NOT NULL)),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            FOREIGN KEY (account_id, preview_id) REFERENCES public.capacity_reduction_previews(account_id, preview_id),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_orders(account_id, order_id),
            FOREIGN KEY (account_id, order_id, receipt_id) REFERENCES public.manual_payment_receipts(account_id, order_id, receipt_id)
        );
        CREATE UNIQUE INDEX single_active_scheduled_reduction
            ON public.scheduled_capacity_reductions(account_id, term_id)
            WHERE state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview');
        CREATE UNIQUE INDEX scheduled_reduction_order_unique
            ON public.scheduled_capacity_reductions(account_id, order_id) WHERE order_id IS NOT NULL;

        CREATE TABLE public.scheduled_capacity_reduction_shops (
            account_id uuid NOT NULL,
            schedule_id uuid NOT NULL,
            shop_id uuid NOT NULL,
            shop_name text NOT NULL CHECK (length(shop_name) BETWEEN 1 AND 250),
            affiliation_request_id uuid NOT NULL,
            retained boolean NOT NULL,
            active_members int NOT NULL CHECK (active_members >= 0),
            seat_hash text NOT NULL CHECK (seat_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            PRIMARY KEY (account_id, schedule_id, shop_id),
            FOREIGN KEY (account_id, shop_id, affiliation_request_id)
                REFERENCES public.shop_affiliation_requests(account_id, shop_id, request_id),
            FOREIGN KEY (account_id, schedule_id) REFERENCES public.scheduled_capacity_reductions(account_id, schedule_id)
                DEFERRABLE INITIALLY DEFERRED
        );

        CREATE TABLE public.scheduled_capacity_reduction_resolutions (
            account_id uuid NOT NULL,
            resolution_id uuid NOT NULL,
            schedule_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash text NOT NULL CHECK (request_hash ~ '^[a-f0-9]{64}$'),
            replay_id uuid NOT NULL,
            generation bigint NOT NULL CHECK (generation > 0),
            observation_digest text NOT NULL CHECK (observation_digest ~ '^[a-f0-9]{64}$'),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            PRIMARY KEY (account_id, resolution_id),
            UNIQUE (account_id, operation_key),
            UNIQUE (replay_id),
            UNIQUE (account_id, schedule_id, generation),
            FOREIGN KEY (account_id, schedule_id) REFERENCES public.scheduled_capacity_reductions(account_id, schedule_id)
        );
        """;
}
