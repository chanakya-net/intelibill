using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261124130000_PaidCapacityIncreases")]
public sealed partial class PaidCapacityIncreases : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id',true),'')::uuid";
    private const string Attempt = "nullif(current_setting('app.payment_attempt_id',true),'')::uuid";
    private const string Receipt = "nullif(current_setting('app.manual_receipt_id',true),'')::uuid";
    private const string ManualOrder = "nullif(current_setting('app.manual_order_id',true),'')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            CREATE TABLE public.capacity_increases (
                account_id uuid NOT NULL, term_id uuid NOT NULL, order_id uuid NOT NULL,
                attempt_id uuid UNIQUE REFERENCES public.verified_payments(attempt_id), receipt_id uuid UNIQUE,
                reserve_event uuid UNIQUE NOT NULL REFERENCES public.entitlement_messages(event_id),
                consume_event uuid UNIQUE REFERENCES public.entitlement_messages(event_id),
                target_revision bigint NOT NULL CHECK (target_revision > 0),
                state text NOT NULL CHECK (state IN ('Pending','Applied','NeedsReview')), reason text,
                capacity integer NOT NULL CHECK (capacity BETWEEN 1 AND 100000),
                members integer NOT NULL CHECK (members BETWEEN 1 AND 100000),
                gross bigint NOT NULL CHECK (gross BETWEEN 1 AND 1000000000000), plan_version uuid NOT NULL,
                PRIMARY KEY (account_id,order_id), CHECK (num_nonnulls(attempt_id,receipt_id)=1),
                CHECK ((state='NeedsReview')=(reason IS NOT NULL)),
                FOREIGN KEY (account_id,term_id) REFERENCES public.subscription_coverage_terms(account_id,term_id),
                FOREIGN KEY (account_id,order_id) REFERENCES public.checkout_orders(account_id,order_id),
                FOREIGN KEY (account_id,order_id) REFERENCES public.capacity_change_quotes(account_id,quote_id),
                FOREIGN KEY (account_id,order_id,receipt_id) REFERENCES public.manual_payment_receipts(account_id,order_id,receipt_id)
            );
            CREATE UNIQUE INDEX capacity_increase_source ON public.capacity_increases(account_id,term_id,target_revision)
                WHERE state IN ('Pending','Applied');
            ALTER TABLE public.capacity_increases ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.capacity_increases FORCE ROW LEVEL SECURITY;
            CREATE POLICY increase_read ON public.capacity_increases FOR SELECT USING (account_id = {Account} OR
                state='Applied' AND EXISTS (SELECT FROM public.shop_affiliations a WHERE a.account_id=capacity_increases.account_id
                    AND a.shop_id=nullif(current_setting('app.status_shop_id',true),'')::uuid));
            CREATE POLICY increase_fund ON public.capacity_increases FOR INSERT WITH CHECK (account_id={Account}
                AND state='Pending' AND consume_event IS NULL AND (attempt_id={Attempt} OR receipt_id={Receipt}));
            CREATE POLICY increase_ack ON public.capacity_increases FOR UPDATE USING (account_id={Account}
                AND EXISTS (SELECT FROM public.entitlement_merchant_inbox e WHERE e.kind='CapacityReceipt'
                    AND e.account_id=capacity_increases.account_id AND e.xmin=pg_current_xact_id()::xid
                    AND e.event_id IN (capacity_increases.reserve_event,capacity_increases.consume_event)));
            CREATE POLICY capacity_settlement_quote ON public.capacity_change_quotes FOR SELECT USING (account_id={Account}
                AND (EXISTS (SELECT FROM public.payment_attempts a WHERE a.attempt_id={Attempt}
                    AND a.account_id=capacity_change_quotes.account_id AND a.order_id=capacity_change_quotes.quote_id)
                    OR quote_id={ManualOrder} AND nullif(current_setting('app.manual_admin_id',true),'') IS NOT NULL
                    OR EXISTS (SELECT FROM public.capacity_increases i WHERE i.account_id=capacity_change_quotes.account_id
                        AND i.order_id=capacity_change_quotes.quote_id AND i.state='Pending' AND EXISTS
                        (SELECT FROM public.entitlement_merchant_inbox e WHERE e.event_id=coalesce(i.consume_event,i.reserve_event)
                            AND e.account_id=i.account_id AND e.kind='CapacityReceipt' AND e.xmin=pg_current_xact_id()::xid))));
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK(kind IN ('Snapshot','Acknowledgement','Reconciliation','CapacityReserve','CapacityConsume'));
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ('Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt'));
            ALTER TABLE public.manual_payment_receipts DROP CONSTRAINT manual_payment_receipts_review_reason_check,
                ADD CHECK(review_reason IN ('DuplicateFunds','CoverageChanged','TermUnavailable','RestorationPending','CapacityPending'));
            CREATE TRIGGER retain_increase BEFORE DELETE OR TRUNCATE ON public.capacity_increases
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            """);
        migrationBuilder.Sql(PaymentGuards);
        migrationBuilder.Sql(Confirmation);
        migrationBuilder.Sql(Guard);
        migrationBuilder.Sql(CoverageGuard);
    }

    private const string Confirmation = """
        ALTER FUNCTION public.payment_confirmation_snapshot(uuid,uuid,uuid,uuid,uuid,timestamptz,text)
            RENAME TO payment_confirmation_before_increases;
        CREATE FUNCTION public.payment_confirmation_snapshot(cid uuid,account uuid,ordering uuid,attempt uuid,receipt uuid,
            issued timestamptz,basis text) RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT public.payment_confirmation_before_increases(cid,account,ordering,attempt,receipt,issued,basis) ||
                CASE WHEN EXISTS (SELECT FROM public.capacity_increases i WHERE i.account_id=account AND i.order_id=ordering
                    AND (i.attempt_id=attempt OR i.receipt_id=receipt))
                THEN jsonb_build_object('fulfilmentAtIssuance','Pending') ELSE '{}'::jsonb END
        $$;
        """;

    private const string Guard = """
        CREATE FUNCTION public.guard_capacity_increase() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='INSERT' THEN
                IF NOT EXISTS (SELECT FROM public.capacity_change_quotes q JOIN public.checkout_orders o
                    ON o.account_id=q.account_id AND o.order_id=q.quote_id
                    JOIN public.entitlement_messages e ON e.event_id=NEW.reserve_event AND e.account_id=q.account_id
                    WHERE q.quote_id=NEW.order_id AND q.account_id=NEW.account_id AND q.term_id=NEW.term_id AND q.outcome='Payable'
                        AND q.snapshot_hash=o.snapshot->'increase'->>'quoteHash' AND q.amount_paise=o.amount_paise
                        AND NEW.target_revision-1=(o.snapshot->'increase'->>'sourceRevision')::bigint
                        AND NEW.capacity=q.target_capacity AND NEW.members=q.target_member_allowance AND NEW.gross=q.target_gross_paise
                        AND NEW.plan_version=q.target_plan_version_id AND e.kind='CapacityReserve'
                        AND convert_from(e.payload,'UTF8')::jsonb @> jsonb_build_object('orderId',NEW.order_id,
                            'termId',NEW.term_id,'accountId',NEW.account_id,'targetRevision',NEW.target_revision,
                            'capacity',NEW.capacity,'members',NEW.members,'phase',e.kind)
                        AND (EXISTS (SELECT FROM public.verified_payments v WHERE v.attempt_id=NEW.attempt_id
                            AND v.order_id=o.order_id AND v.account_id=o.account_id AND v.amount_paise=q.amount_paise)
                         OR EXISTS (SELECT FROM public.manual_payment_receipts r WHERE r.receipt_id=NEW.receipt_id
                            AND r.account_id=o.account_id AND r.order_id=o.order_id AND r.amount_paise=q.amount_paise))) THEN
                    RAISE EXCEPTION 'Capacity increase requires its verified order' USING ERRCODE='23514';
                END IF;
            ELSIF OLD.state<>'Pending' OR (to_jsonb(NEW)-'state'-'reason'-'consume_event') IS DISTINCT FROM
                (to_jsonb(OLD)-'state'-'reason'-'consume_event') OR OLD.consume_event IS NOT NULL AND
                NEW.consume_event IS DISTINCT FROM OLD.consume_event OR NEW.state='Applied' AND NEW.consume_event IS NULL THEN
                RAISE EXCEPTION 'Capacity receipt conflicts with original operation' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_capacity_increase BEFORE INSERT OR UPDATE ON public.capacity_increases
            FOR EACH ROW EXECUTE FUNCTION public.guard_capacity_increase();
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.checkout_orders,public.capacity_increases IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.checkout_orders NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN IF EXISTS(SELECT FROM public.checkout_orders WHERE snapshot->>'startRule'='CapacityIncrease') THEN
                RAISE EXCEPTION 'Retained capacity order prevents downgrade' USING ERRCODE='55000';
            END IF; END $$;
            ALTER TABLE public.checkout_orders FORCE ROW LEVEL SECURITY;
            DROP FUNCTION public.payment_confirmation_snapshot(uuid,uuid,uuid,uuid,uuid,timestamptz,text);
            ALTER FUNCTION public.payment_confirmation_before_increases(uuid,uuid,uuid,uuid,uuid,timestamptz,text)
                RENAME TO payment_confirmation_snapshot;
            DROP POLICY capacity_settlement_quote ON public.capacity_change_quotes;
            DROP TRIGGER shop_affiliation_coverage_guard ON public.shop_affiliations;
            DROP FUNCTION public.guard_shop_affiliation_coverage();
            ALTER FUNCTION public.guard_shop_affiliation_before_increases() RENAME TO guard_shop_affiliation_coverage;
            CREATE TRIGGER shop_affiliation_coverage_guard BEFORE INSERT ON public.shop_affiliations
                FOR EACH ROW EXECUTE FUNCTION public.guard_shop_affiliation_coverage();
            DROP FUNCTION public.capacity_slot_available(uuid,uuid,int);
            DROP TABLE public.capacity_increases;
            DROP FUNCTION public.guard_capacity_increase();
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK(kind IN ('Snapshot','Acknowledgement','Reconciliation'));
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check,
                ADD CHECK(kind IN ('Acknowledgement','Reconciliation','DeviceObservation'));
            ALTER TABLE public.manual_payment_receipts DROP CONSTRAINT manual_payment_receipts_review_reason_check,
                ADD CHECK(review_reason IN ('DuplicateFunds','CoverageChanged','TermUnavailable','RestorationPending'));
            """);
        migrationBuilder.Sql(PaidFreshStarts.PaymentGuards);
    }
}
