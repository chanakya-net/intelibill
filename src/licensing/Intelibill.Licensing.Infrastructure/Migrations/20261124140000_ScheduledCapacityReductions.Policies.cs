using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class ScheduledCapacityReductions
{
    private static string LiveOwner(string qualifier) => $"""
        EXISTS (SELECT FROM public.billing_account_members m
            WHERE m.account_id = {qualifier}.account_id AND m.merchant_user_id = {Actor} AND m.status = 1 AND m.role = 1)
        """;

    private static string InboxReceipt(string alias) => $"""
        EXISTS (SELECT FROM public.entitlement_merchant_inbox e WHERE e.kind = 'ReductionReceipt'
            AND e.account_id = {alias}.account_id AND e.xmin = pg_current_xact_id()::xid
            AND e.event_id IN ({alias}.reserve_event, {alias}.consume_event))
        """;

    private static string AppliedInbox(string table, string match) => $"""
        account_id = {Account} AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
            JOIN public.entitlement_merchant_inbox e ON e.account_id = s.account_id AND e.event_id = s.consume_event
                AND e.kind = 'ReductionReceipt' AND e.xmin = pg_current_xact_id()::xid
            WHERE s.account_id = {table}.account_id AND s.state = 'Applied' AND s.xmin = pg_current_xact_id()::xid
                AND {match})
        """;

    private static string ReviewDecision => $"""
        account_id = {Account} AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
            WHERE s.account_id = unmatched_payments.account_id AND s.order_id = unmatched_payments.order_id
                AND s.state = 'NeedsReview' AND s.reason = unmatched_payments.classification
                AND s.xmin = pg_current_xact_id()::xid
                AND (s.receipt_id = unmatched_payments.manual_receipt_id OR s.attempt_id = unmatched_payments.attempt_id))
        """;

    private static void ConfigurePolicies(MigrationBuilder builder)
    {
        foreach (var table in new[] { "capacity_reduction_previews", "capacity_reduction_preview_shops" })
        {
            builder.Sql($"""
                ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY reduction_preview_read ON public.{table} FOR SELECT USING (
                    account_id = {Account} AND {LiveOwner(table)});
                CREATE POLICY reduction_preview_insert ON public.{table} FOR INSERT WITH CHECK (
                    account_id = {Account} AND {LiveOwner(table)} AND EXISTS (
                        SELECT FROM public.merchant_billing_delegation_uses u
                        WHERE u.replay_id = {table}.replay_id AND u.account_id = {table}.account_id
                            AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid
                            AND u.action = 'billing.capacity-reduction.preview'));
                CREATE TRIGGER reduction_preview_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.{table}
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
                """);
        }

        builder.Sql($"""
            CREATE POLICY reduction_catalogue_read ON public.published_catalogue_offers FOR SELECT USING (
                EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = nullif(current_setting('app.catalogue_public_read', true), '')::uuid
                        AND u.account_id = {Account} AND u.actor_id = {Actor}
                        AND u.xmin = pg_current_xact_id()::xid
                        AND u.action = 'billing.capacity-reduction.preview' AND {LiveOwner("u")}));

            ALTER TABLE public.scheduled_capacity_reductions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.scheduled_capacity_reductions FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                EXECUTE format('CREATE POLICY reduction_due_worker ON public.scheduled_capacity_reductions
                    FOR SELECT TO %I USING (true)', current_user);
            END $$;
            CREATE POLICY reduction_read ON public.scheduled_capacity_reductions FOR SELECT USING (
                account_id = {Account} AND ({LiveOwner("scheduled_capacity_reductions")}
                    OR {DueScan}
                    OR {Attempt} IS NOT NULL OR ({ManualOrder} IS NOT NULL AND nullif(current_setting('app.manual_admin_id', true), '') IS NOT NULL)
                    OR {InboxReceipt("scheduled_capacity_reductions")}
                    OR EXISTS (SELECT FROM public.billing_account_members m WHERE m.account_id = scheduled_capacity_reductions.account_id
                        AND m.merchant_user_id = {Actor} AND m.status = 1 AND m.role IN (1, 2))));
            CREATE POLICY reduction_insert ON public.scheduled_capacity_reductions FOR INSERT WITH CHECK (
                account_id = {Account} AND state = 'Scheduled' AND order_id IS NULL AND {LiveOwner("scheduled_capacity_reductions")}
                AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = scheduled_capacity_reductions.replay_id
                        AND u.account_id = scheduled_capacity_reductions.account_id
                        AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid
                        AND u.action = 'billing.capacity-reduction.schedule'));
            CREATE POLICY reduction_update ON public.scheduled_capacity_reductions FOR UPDATE USING (
                account_id = {Account} AND (
                    ({Attempt} IS NOT NULL AND (attempt_id IS NULL OR attempt_id = {Attempt}))
                    OR ({Receipt} IS NOT NULL AND (receipt_id IS NULL OR receipt_id = {Receipt}))
                    OR ({DueScan} AND state = 'Funded')
                    OR {InboxReceipt("scheduled_capacity_reductions")}
                    OR ({LiveOwner("scheduled_capacity_reductions")} AND state = 'NeedsResolution' AND EXISTS (
                        SELECT FROM public.merchant_billing_delegation_uses u
                        WHERE u.account_id = scheduled_capacity_reductions.account_id AND u.actor_id = {Actor}
                            AND u.xmin = pg_current_xact_id()::xid AND u.action = 'billing.capacity-reduction.resolve'))))
                WITH CHECK (account_id = {Account});
            CREATE TRIGGER reduction_retained BEFORE DELETE OR TRUNCATE ON public.scheduled_capacity_reductions
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();

            ALTER TABLE public.scheduled_capacity_reduction_shops ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.scheduled_capacity_reduction_shops FORCE ROW LEVEL SECURITY;
            CREATE POLICY reduction_shop_read ON public.scheduled_capacity_reduction_shops FOR SELECT USING (
                account_id = {Account} AND ({LiveOwner("scheduled_capacity_reduction_shops")}
                    OR {DueScan}
                    OR {Attempt} IS NOT NULL OR ({ManualOrder} IS NOT NULL AND nullif(current_setting('app.manual_admin_id', true), '') IS NOT NULL)
                    OR EXISTS (SELECT FROM public.scheduled_capacity_reductions s
                        WHERE s.account_id = scheduled_capacity_reduction_shops.account_id
                            AND s.schedule_id = scheduled_capacity_reduction_shops.schedule_id
                            AND {InboxReceipt("s")})));
            CREATE POLICY reduction_shop_insert ON public.scheduled_capacity_reduction_shops FOR INSERT WITH CHECK (
                account_id = {Account} AND {LiveOwner("scheduled_capacity_reduction_shops")} AND EXISTS (
                    SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = scheduled_capacity_reduction_shops.replay_id
                        AND u.account_id = scheduled_capacity_reduction_shops.account_id
                        AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid
                        AND u.action = 'billing.capacity-reduction.schedule'));
            CREATE TRIGGER reduction_shop_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.scheduled_capacity_reduction_shops
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();

            ALTER TABLE public.scheduled_capacity_reduction_resolutions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.scheduled_capacity_reduction_resolutions FORCE ROW LEVEL SECURITY;
            CREATE POLICY reduction_resolution_read ON public.scheduled_capacity_reduction_resolutions FOR SELECT USING (
                account_id = {Account} AND {LiveOwner("scheduled_capacity_reduction_resolutions")});
            CREATE POLICY reduction_resolution_insert ON public.scheduled_capacity_reduction_resolutions FOR INSERT WITH CHECK (
                account_id = {Account} AND actor_id = {Actor} AND {LiveOwner("scheduled_capacity_reduction_resolutions")}
                AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                    WHERE u.replay_id = scheduled_capacity_reduction_resolutions.replay_id
                        AND u.account_id = scheduled_capacity_reduction_resolutions.account_id
                        AND u.actor_id = {Actor} AND u.xmin = pg_current_xact_id()::xid
                        AND u.action = 'billing.capacity-reduction.resolve'));
            CREATE TRIGGER reduction_resolution_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.scheduled_capacity_reduction_resolutions
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            """);

        foreach (var table in new[] { "checkout_orders", "checkout_order_shops", "checkout_order_acceptances" })
        {
            builder.Sql($"""
                CREATE POLICY reduction_inbox_order_read ON public.{table} FOR SELECT USING (
                    account_id = {Account} AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
                        WHERE s.account_id = {table}.account_id AND s.order_id = {table}.order_id
                            AND ({InboxReceipt("s")} OR {DueScan})));
                """);
        }

        builder.Sql($"""
            CREATE POLICY reduction_inbox_fulfilment_insert ON public.payment_fulfilments FOR INSERT WITH CHECK (
                {AppliedInbox("payment_fulfilments", "s.order_id = payment_fulfilments.order_id AND s.successor_term_id = payment_fulfilments.term_id")});
            CREATE POLICY reduction_inbox_shops_insert ON public.payment_fulfilment_shops FOR INSERT WITH CHECK (
                {AppliedInbox("payment_fulfilment_shops", "s.order_id = payment_fulfilment_shops.order_id AND s.successor_term_id = payment_fulfilment_shops.term_id")});
            CREATE POLICY reduction_inbox_deliveries_insert ON public.payment_fulfilment_deliveries FOR INSERT WITH CHECK (
                {AppliedInbox("payment_fulfilment_deliveries", "s.order_id = payment_fulfilment_deliveries.order_id")});
            CREATE POLICY reduction_inbox_term_insert ON public.subscription_coverage_terms FOR INSERT WITH CHECK (
                {AppliedInbox("subscription_coverage_terms", "s.successor_term_id = subscription_coverage_terms.term_id")});
            CREATE POLICY reduction_inbox_facts_insert ON public.subscription_term_facts FOR INSERT WITH CHECK (
                basis = 'Paid' AND source_reference = 'payment-fulfilment/1' AND
                {AppliedInbox("subscription_term_facts", "s.successor_term_id = subscription_term_facts.term_id")});
            CREATE POLICY reduction_inbox_event_insert ON public.payment_events FOR INSERT WITH CHECK (
                account_id = {Account} AND (
                    (kind = 'OrderFulfilled' AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
                        JOIN public.entitlement_merchant_inbox e ON e.account_id = s.account_id AND e.event_id = s.consume_event
                            AND e.kind = 'ReductionReceipt' AND e.xmin = pg_current_xact_id()::xid
                        WHERE s.account_id = payment_events.account_id AND s.order_id = payment_events.order_id
                            AND s.attempt_id = payment_events.attempt_id AND s.state = 'Applied' AND s.xmin = pg_current_xact_id()::xid))
                    OR (kind = 'PaidNeedsReview' AND EXISTS (SELECT FROM public.scheduled_capacity_reductions s
                        WHERE s.account_id = payment_events.account_id AND s.order_id = payment_events.order_id
                            AND s.attempt_id = payment_events.attempt_id AND s.state = 'NeedsReview'
                            AND s.xmin = pg_current_xact_id()::xid))));
            CREATE POLICY reduction_review_insert ON public.unmatched_payments FOR INSERT WITH CHECK ({ReviewDecision});
            CREATE POLICY shop_status_fulfilment_read ON public.payment_fulfilments FOR SELECT USING (
                EXISTS (SELECT FROM public.shop_affiliations a
                    WHERE a.account_id = payment_fulfilments.account_id
                        AND a.shop_id = nullif(current_setting('app.status_shop_id', true), '')::uuid
                        AND (a.term_id IN (payment_fulfilments.term_id, payment_fulfilments.predecessor_term_id)
                            OR EXISTS (SELECT FROM public.payment_fulfilment_shops l
                                WHERE l.account_id = a.account_id AND l.shop_id = a.shop_id
                                    AND l.affiliation_request_id = a.request_id
                                    AND l.term_id IN (payment_fulfilments.term_id, payment_fulfilments.predecessor_term_id)))));
            CREATE POLICY shop_status_fresh_read ON public.paid_fresh_starts FOR SELECT USING (
                EXISTS (SELECT FROM public.payment_fulfilments f
                    WHERE f.account_id = paid_fresh_starts.account_id AND f.order_id = paid_fresh_starts.order_id));
            DROP POLICY capacity_reason_requires_decision ON public.unmatched_payments;
            CREATE POLICY capacity_reason_requires_decision ON public.unmatched_payments AS RESTRICTIVE FOR INSERT
                WITH CHECK (classification NOT IN ('ReservationExpired','ReservationChanged','SourceChanged','ReservationBusy')
                    OR (account_id = {Account} AND EXISTS (SELECT FROM public.capacity_increases i
                        WHERE i.account_id = unmatched_payments.account_id AND i.order_id = unmatched_payments.order_id
                            AND i.state = 'NeedsReview' AND i.reason = unmatched_payments.classification AND i.xmin = pg_current_xact_id()::xid
                            AND (i.receipt_id = unmatched_payments.manual_receipt_id OR i.attempt_id = unmatched_payments.attempt_id)))
                    OR ({ReviewDecision}));
            """);
    }

    private static void DropPolicies(MigrationBuilder builder)
    {
        builder.Sql($"""
            DROP POLICY capacity_reason_requires_decision ON public.unmatched_payments;
            CREATE POLICY capacity_reason_requires_decision ON public.unmatched_payments AS RESTRICTIVE FOR INSERT
                WITH CHECK (classification NOT IN ('ReservationExpired','ReservationChanged','SourceChanged','ReservationBusy')
                    OR (account_id = {Account} AND EXISTS (SELECT FROM public.capacity_increases i
                        WHERE i.account_id = unmatched_payments.account_id AND i.order_id = unmatched_payments.order_id
                            AND i.state = 'NeedsReview' AND i.reason = unmatched_payments.classification AND i.xmin = pg_current_xact_id()::xid
                            AND (i.receipt_id = unmatched_payments.manual_receipt_id OR i.attempt_id = unmatched_payments.attempt_id))));
            DROP POLICY reduction_catalogue_read ON public.published_catalogue_offers;
            DROP POLICY reduction_inbox_order_read ON public.checkout_orders;
            DROP POLICY reduction_inbox_order_read ON public.checkout_order_shops;
            DROP POLICY reduction_inbox_order_read ON public.checkout_order_acceptances;
            DROP POLICY reduction_inbox_fulfilment_insert ON public.payment_fulfilments;
            DROP POLICY reduction_inbox_shops_insert ON public.payment_fulfilment_shops;
            DROP POLICY reduction_inbox_deliveries_insert ON public.payment_fulfilment_deliveries;
            DROP POLICY reduction_inbox_term_insert ON public.subscription_coverage_terms;
            DROP POLICY reduction_inbox_facts_insert ON public.subscription_term_facts;
            DROP POLICY reduction_inbox_event_insert ON public.payment_events;
            DROP POLICY reduction_review_insert ON public.unmatched_payments;
            DROP POLICY shop_status_fresh_read ON public.paid_fresh_starts;
            DROP POLICY shop_status_fulfilment_read ON public.payment_fulfilments;
            """);
    }
}
