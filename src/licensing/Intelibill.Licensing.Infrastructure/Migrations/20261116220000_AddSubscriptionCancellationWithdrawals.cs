using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// A withdrawal is the next immutable revision of a term's cancellation history: odd revisions schedule, even
/// revisions withdraw the immediately preceding schedule before its unchanged boundary. Nothing is updated.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261116220000_AddSubscriptionCancellationWithdrawals")]
public sealed class AddSubscriptionCancellationWithdrawals : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Shop = "nullif(current_setting('app.status_shop_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Columns);
        migrationBuilder.Sql(Guards);
        migrationBuilder.Sql($"""
            DROP POLICY cancellation_read ON public.scheduled_subscription_changes;
            CREATE POLICY cancellation_read ON public.scheduled_subscription_changes FOR SELECT USING (
                account_id = {Account} OR EXISTS (
                    SELECT FROM public.scheduled_subscription_change_shops s
                    WHERE s.change_id = coalesce(scheduled_subscription_changes.withdraws_change_id,
                        scheduled_subscription_changes.change_id) AND s.shop_id = {Shop}));
            """);
    }

    private const string Columns = """
        ALTER TABLE public.scheduled_subscription_changes
            DROP CONSTRAINT scheduled_subscription_changes_revision_check,
            DROP CONSTRAINT scheduled_subscription_changes_kind_check,
            ADD COLUMN withdraws_change_id uuid,
            ADD CONSTRAINT scheduled_subscription_changes_revision_check CHECK (revision >= 1),
            ADD CONSTRAINT scheduled_subscription_changes_kind_check
                CHECK (kind IN ('ScheduleCancellation', 'WithdrawCancellation')),
            ADD CONSTRAINT scheduled_subscription_changes_withdrawal_link
                CHECK ((kind = 'WithdrawCancellation') = (withdraws_change_id IS NOT NULL)),
            ADD CONSTRAINT scheduled_subscription_changes_revision_parity
                CHECK ((kind = 'ScheduleCancellation') = (revision % 2 = 1)),
            ADD CONSTRAINT scheduled_subscription_changes_withdraws_once UNIQUE (withdraws_change_id),
            ADD CONSTRAINT scheduled_subscription_changes_withdrawn_change
                FOREIGN KEY (account_id, withdraws_change_id)
                REFERENCES public.scheduled_subscription_changes(account_id, change_id);
        """;

    internal const string Guards = """
        CREATE OR REPLACE FUNCTION public.guard_subscription_cancellation() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.subscription_coverage_terms t JOIN public.subscription_term_facts f USING (account_id, term_id)
                WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND f.basis = 'Paid'
                    AND f.price_paise IS NOT NULL AND t.starts_at <= NEW.recorded_at
                    AND t.ends_at = NEW.effective_at AND t.capacity >= NEW.shop_count
            ) OR NOT EXISTS (
                SELECT FROM public.subscription_cancellation_receipts r
                JOIN public.billing_account_members m ON m.account_id = r.account_id AND m.merchant_user_id = r.actor_id
                WHERE r.change_id = NEW.change_id AND m.status = 1 AND m.role IN (1, 2)
                    AND m.revision = r.membership_revision
            ) THEN
                RAISE EXCEPTION 'Cancellation requires the paid boundary and an acceptance receipt' USING ERRCODE = '23514';
            END IF;
            IF NEW.kind = 'WithdrawCancellation' THEN
                IF NOT EXISTS (
                    SELECT FROM public.scheduled_subscription_changes s
                    WHERE s.account_id = NEW.account_id AND s.change_id = NEW.withdraws_change_id
                        AND s.term_id = NEW.term_id AND s.kind = 'ScheduleCancellation' AND s.revision = NEW.revision - 1
                        AND s.effective_at = NEW.effective_at AND s.policy_version = NEW.policy_version
                        AND s.shop_count = NEW.shop_count AND s.shop_hash = NEW.shop_hash AND s.recorded_at <= NEW.recorded_at
                ) OR EXISTS (SELECT FROM public.scheduled_subscription_change_shops x WHERE x.change_id = NEW.change_id) THEN
                    RAISE EXCEPTION 'Withdrawal must follow the scheduled cancellation it withdraws' USING ERRCODE = '23514';
                END IF;
            ELSIF (NEW.revision > 1 AND NOT EXISTS (
                    SELECT FROM public.scheduled_subscription_changes w
                    WHERE w.account_id = NEW.account_id AND w.term_id = NEW.term_id
                        AND w.revision = NEW.revision - 1 AND w.kind = 'WithdrawCancellation'))
                OR NEW.shop_count <> (SELECT count(*) FROM public.scheduled_subscription_change_shops s WHERE s.change_id = NEW.change_id)
                OR NEW.shop_count <> (SELECT count(*) FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) THEN
                RAISE EXCEPTION 'Cancellation requires complete shop scope after any earlier withdrawal' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;

        CREATE OR REPLACE FUNCTION public.guard_subscription_cancellation_shop() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.scheduled_subscription_changes c
                JOIN public.shop_affiliations a ON a.account_id = c.account_id AND a.term_id = c.term_id
                WHERE c.change_id = NEW.change_id AND c.kind = 'ScheduleCancellation' AND a.shop_id = NEW.shop_id
                    AND a.request_id = NEW.request_id AND a.shop_name = NEW.shop_name
                    AND a.applied_at <= c.recorded_at AND a.coverage_ends_at = c.effective_at
            ) OR EXISTS (SELECT FROM public.subscription_cancellation_receipts r WHERE r.change_id = NEW.change_id) THEN
                RAISE EXCEPTION 'Cancellation shop must belong to the active scope before acceptance' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.scheduled_subscription_changes IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.scheduled_subscription_changes NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.scheduled_subscription_changes WHERE revision > 1) THEN
                RAISE EXCEPTION 'Subscription cancellation withdrawal evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.scheduled_subscription_changes FORCE ROW LEVEL SECURITY;
        DROP POLICY cancellation_read ON public.scheduled_subscription_changes;
        CREATE POLICY cancellation_read ON public.scheduled_subscription_changes FOR SELECT USING (
            account_id = {Account} OR EXISTS (
                SELECT FROM public.scheduled_subscription_change_shops s
                WHERE s.change_id = scheduled_subscription_changes.change_id AND s.shop_id = {Shop}));
        DROP TRIGGER require_cancellation_evidence ON public.scheduled_subscription_changes;
        DROP TRIGGER require_cancellation_shop ON public.scheduled_subscription_change_shops;
        DROP FUNCTION public.guard_subscription_cancellation_shop();
        DROP FUNCTION public.guard_subscription_cancellation();
        ALTER TABLE public.scheduled_subscription_changes
            DROP CONSTRAINT scheduled_subscription_changes_withdrawn_change,
            DROP CONSTRAINT scheduled_subscription_changes_withdraws_once,
            DROP CONSTRAINT scheduled_subscription_changes_revision_parity,
            DROP CONSTRAINT scheduled_subscription_changes_withdrawal_link,
            DROP CONSTRAINT scheduled_subscription_changes_kind_check,
            DROP CONSTRAINT scheduled_subscription_changes_revision_check,
            DROP COLUMN withdraws_change_id,
            ADD CONSTRAINT scheduled_subscription_changes_revision_check CHECK (revision = 1),
            ADD CONSTRAINT scheduled_subscription_changes_kind_check CHECK (kind = 'ScheduleCancellation');
        {AddScheduledSubscriptionCancellations.Guards}
        """);
}
