using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-09: SQL-owned immutable refund approvals (the reserved amount, private finance evidence) and their separately
/// recorded, uniquely linked access decisions (operational facts). One approval per request; every approval holds its
/// amount against the original verified payment confirmation and the cumulative total never exceeds that receipt.
/// Apply before the licensing API that approves refunds or reads term access. Down refuses while any row is retained.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261125100918_RefundApprovals")]
public sealed partial class RefundApprovals : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Admin = "nullif(current_setting('app.manual_admin_id', true), '')::uuid";
    private const string Session = "nullif(current_setting('app.manual_session_id', true), '')::uuid";
    private const string Shop = "nullif(current_setting('app.status_shop_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(ApprovalGuards);
        migrationBuilder.Sql(DecisionGuards);
    }

    private const string Tables = """
        CREATE TABLE public.refund_approvals (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            approval_id uuid NOT NULL,
            -- One stable allocation identity per held amount, counted once through any later provider outcome.
            reservation_id uuid NOT NULL UNIQUE,
            refund_request_id uuid NOT NULL,
            order_id uuid NOT NULL,
            confirmation_id uuid NOT NULL,
            attempt_id uuid,
            manual_receipt_id uuid,
            operation_key uuid NOT NULL,
            payload_hash text NOT NULL CHECK (payload_hash ~ '^[a-f0-9]{64}$'),
            impact_digest text NOT NULL CHECK (impact_digest ~ '^[a-f0-9]{64}$'),
            admin_id uuid NOT NULL,
            session_id uuid NOT NULL,
            mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
            authorized_at timestamptz NOT NULL CHECK (isfinite(authorized_at) AND authorized_at >= mfa_verified_at
                AND authorized_at < mfa_verified_at + interval '5 minutes'),
            decided_at timestamptz NOT NULL CHECK (isfinite(decided_at)),
            provider_id text NOT NULL CHECK (length(provider_id) BETWEEN 1 AND 80),
            provider_account_id text NOT NULL CHECK (length(provider_account_id) BETWEEN 1 AND 200),
            environment text NOT NULL CHECK (length(environment) BETWEEN 1 AND 40),
            configuration_version text NOT NULL CHECK (length(configuration_version) BETWEEN 1 AND 200),
            refund_policy_version text NOT NULL CHECK (length(refund_policy_version) BETWEEN 1 AND 80),
            payment_amount_paise bigint NOT NULL CHECK (payment_amount_paise BETWEEN 1 AND 1000000000000),
            requested_amount_paise bigint NOT NULL CHECK (requested_amount_paise BETWEEN 1 AND payment_amount_paise),
            amount_paise bigint NOT NULL CHECK (amount_paise BETWEEN 1 AND requested_amount_paise),
            remaining_before_paise bigint NOT NULL CHECK (remaining_before_paise BETWEEN amount_paise AND payment_amount_paise),
            remaining_after_paise bigint NOT NULL CHECK (remaining_after_paise = remaining_before_paise - amount_paise),
            receipt_revision bigint NOT NULL CHECK (receipt_revision > 0),
            currency text NOT NULL CHECK (currency = 'INR'),
            reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 500 AND reason = btrim(reason) AND reason !~ '[[:cntrl:]]'),
            PRIMARY KEY (account_id, approval_id),
            UNIQUE (account_id, operation_key),
            UNIQUE (account_id, refund_request_id),
            UNIQUE (confirmation_id, receipt_revision),
            CHECK ((attempt_id IS NULL) <> (manual_receipt_id IS NULL)),
            FOREIGN KEY (account_id, refund_request_id) REFERENCES public.refund_requests(account_id, refund_request_id)
        );
        CREATE INDEX refund_approvals_receipt ON public.refund_approvals (confirmation_id);

        CREATE TABLE public.refund_access_decisions (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            decision_id uuid NOT NULL,
            approval_id uuid NOT NULL,
            refund_request_id uuid NOT NULL,
            treatment text NOT NULL CHECK (treatment IN ('Preserve', 'EndAt', 'TermAdjustment')),
            term_id uuid,
            -- Latest effective decision per term wins; Preserve never takes a term revision.
            term_revision bigint CHECK (term_revision > 0),
            effective_at timestamptz CHECK (isfinite(effective_at)),
            previous_paid_through timestamptz,
            previous_access_ends_at timestamptz,
            new_paid_through timestamptz,
            new_access_ends_at timestamptz,
            shop_count integer NOT NULL CHECK (shop_count BETWEEN 0 AND 100000),
            shops jsonb NOT NULL CHECK (jsonb_typeof(shops) = 'array' AND jsonb_array_length(shops) = shop_count),
            decided_at timestamptz NOT NULL CHECK (isfinite(decided_at)),
            PRIMARY KEY (account_id, decision_id),
            UNIQUE (account_id, approval_id),
            UNIQUE (account_id, refund_request_id),
            UNIQUE (account_id, term_id, term_revision),
            FOREIGN KEY (account_id, approval_id) REFERENCES public.refund_approvals(account_id, approval_id),
            FOREIGN KEY (account_id, term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id),
            CHECK ((treatment = 'Preserve') = (effective_at IS NULL) AND (treatment = 'Preserve') = (term_revision IS NULL)),
            CHECK (treatment <> 'Preserve' OR (new_paid_through IS NOT DISTINCT FROM previous_paid_through
                AND new_access_ends_at IS NOT DISTINCT FROM previous_access_ends_at)),
            CHECK (treatment = 'Preserve' OR (term_id IS NOT NULL AND shop_count > 0 AND effective_at > decided_at
                AND previous_paid_through IS NOT NULL AND previous_access_ends_at IS NOT NULL
                AND new_paid_through IS NOT NULL AND new_access_ends_at IS NOT NULL
                AND new_paid_through <= previous_paid_through AND new_access_ends_at <= previous_access_ends_at)),
            CHECK (treatment <> 'EndAt' OR (effective_at <= previous_access_ends_at AND new_access_ends_at = effective_at
                AND new_paid_through = least(previous_paid_through, effective_at))),
            CHECK (treatment <> 'TermAdjustment' OR (effective_at < previous_paid_through AND new_paid_through = effective_at
                AND new_access_ends_at >= effective_at))
        );
        CREATE INDEX refund_access_decisions_term ON public.refund_access_decisions (account_id, term_id, term_revision DESC)
            WHERE treatment <> 'Preserve';
        """;

    private static readonly string Policies = $"""
        ALTER TABLE public.refund_approvals ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.refund_approvals FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.refund_access_decisions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.refund_access_decisions FORCE ROW LEVEL SECURITY;
        -- Private finance evidence: only a provider admin scope of the same account reads it.
        CREATE POLICY finance_read ON public.refund_approvals FOR SELECT USING (
            account_id = {Account} AND {Admin} IS NOT NULL AND {Session} IS NOT NULL);
        CREATE POLICY finance_insert ON public.refund_approvals FOR INSERT WITH CHECK (
            account_id = {Account} AND admin_id = {Admin} AND session_id = {Session}
            AND EXISTS (SELECT FROM public.admin_sessions s JOIN public.admin_identities a ON a.id = s.admin_id
                WHERE s.id = refund_approvals.session_id AND a.id = refund_approvals.admin_id
                    AND s.revoked_at IS NULL AND s.mfa_verified_at = refund_approvals.mfa_verified_at
                    AND a.mfa_enrolled_at IS NOT NULL AND a.deactivated_at IS NULL AND (a.capabilities & 2) = 2));
        -- Operational facts: the account, or a shop of that account reading the effect on its own status.
        CREATE POLICY decision_read ON public.refund_access_decisions FOR SELECT USING (account_id = {Account} OR
            treatment <> 'Preserve' AND EXISTS (SELECT FROM public.shop_affiliations a
                WHERE a.account_id = refund_access_decisions.account_id AND a.shop_id = {Shop}));
        CREATE POLICY decision_insert ON public.refund_access_decisions FOR INSERT WITH CHECK (
            account_id = {Account} AND {Admin} IS NOT NULL AND {Session} IS NOT NULL);
        CREATE TRIGGER refund_approval_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_approvals
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER refund_access_decision_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.refund_access_decisions
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.refund_approvals, public.refund_access_decisions IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.refund_approvals NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.refund_access_decisions NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.refund_approvals) OR EXISTS (SELECT FROM public.refund_access_decisions) THEN
                RAISE EXCEPTION 'Retained refund approvals prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.refund_access_decisions;
        DROP TABLE public.refund_approvals;
        DROP FUNCTION public.guard_refund_approval(), public.require_refund_access_decision(),
            public.guard_refund_access_decision(), public.refund_receipt_occupied_paise(uuid, uuid),
            public.lock_refund_receipt(uuid);
        """);
}
