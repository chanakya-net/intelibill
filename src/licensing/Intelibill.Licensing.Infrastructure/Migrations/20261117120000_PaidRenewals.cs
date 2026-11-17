using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// Early and grace renewals of a paid term. A renewal fulfilment names the paid term it extends and records the
/// verified effective payment time and the grace days it included; at most one successor exists per predecessor.
/// A shop keeps one paid link per term (its whole history), so lifetime shop uniqueness becomes per-term
/// uniqueness. Quote, attempt, manual receipt and link guards admit only whole, adjacent, same-anchor renewals of
/// the latest paid period. Existing rows keep null renewal provenance; nothing historical is rewritten.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261117120000_PaidRenewals")]
public sealed partial class PaidRenewals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Columns);
        migrationBuilder.Sql(CheckoutOrderGuard);
        migrationBuilder.Sql(PaymentGuards);
        migrationBuilder.Sql(ShopLinkGuard);
        migrationBuilder.Sql(Completeness);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.payment_fulfilments, public.payment_attempts, public.manual_payment_receipts,
                public.checkout_orders IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.payment_fulfilments NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.payment_attempts NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.manual_payment_receipts NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.checkout_orders NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.payment_fulfilments WHERE predecessor_term_id IS NOT NULL)
                    OR EXISTS (SELECT FROM public.checkout_orders q WHERE q.term_id IS NOT NULL AND (
                        EXISTS (SELECT FROM public.payment_attempts a WHERE a.account_id = q.account_id AND a.order_id = q.order_id)
                        OR EXISTS (SELECT FROM public.manual_payment_receipts r
                            WHERE r.account_id = q.account_id AND r.order_id = q.order_id))) THEN
                    RAISE EXCEPTION 'Paid renewal evidence exists' USING ERRCODE = '55000';
                END IF;
            END $$;
            ALTER TABLE public.payment_fulfilments FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.payment_attempts FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.manual_payment_receipts FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.checkout_orders FORCE ROW LEVEL SECURITY;
            """);
        migrationBuilder.Sql(RollbackSql);
    }

    private const string Columns = """
        ALTER TABLE public.payment_fulfilments
            ADD COLUMN predecessor_term_id uuid,
            ADD COLUMN payment_effective_at timestamptz CHECK (isfinite(payment_effective_at)),
            ADD COLUMN grace_days_included integer CHECK (grace_days_included BETWEEN 0 AND 7),
            ADD COLUMN renewal_policy_version text CHECK (renewal_policy_version = 'renewal/1'),
            ADD CONSTRAINT payment_fulfilment_renewal_evidence CHECK (
                (predecessor_term_id IS NULL AND payment_effective_at IS NULL AND grace_days_included IS NULL
                    AND renewal_policy_version IS NULL)
                OR (predecessor_term_id IS NOT NULL AND payment_effective_at IS NOT NULL
                    AND grace_days_included IS NOT NULL AND renewal_policy_version IS NOT NULL
                    AND predecessor_term_id <> term_id)),
            ADD CONSTRAINT payment_fulfilment_one_successor UNIQUE (account_id, predecessor_term_id),
            ADD CONSTRAINT payment_fulfilment_predecessor FOREIGN KEY (account_id, predecessor_term_id)
                REFERENCES public.subscription_coverage_terms(account_id, term_id);
        ALTER TABLE public.payment_fulfilment_shops DROP CONSTRAINT payment_fulfilment_shops_shop_id_key,
            ADD CONSTRAINT payment_fulfilment_shops_term_shop UNIQUE (term_id, shop_id);
        CREATE INDEX payment_fulfilment_shops_account_shop ON public.payment_fulfilment_shops(account_id, shop_id);
        """;

    // The quoted term's covered shops are its affiliation-assigned shops or its paid links; a superseded term is
    // never quoted again.
    private const string CheckoutOrderGuard = """
        CREATE OR REPLACE FUNCTION public.guard_checkout_order() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash
            FROM public.checkout_order_shops s JOIN public.shop_affiliations a
                ON a.request_id = s.affiliation_request_id
            WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id AND s.replay_id = NEW.replay_id
                AND a.applied_at <= NEW.created_at
                AND (a.term_id IS NOT DISTINCT FROM NEW.term_id OR (NEW.term_id IS NOT NULL AND EXISTS (
                    SELECT FROM public.payment_fulfilment_shops p WHERE p.account_id = s.account_id
                        AND p.term_id = NEW.term_id AND p.shop_id = s.shop_id
                        AND p.affiliation_request_id = s.affiliation_request_id)));
            IF NEW.actor_id IS DISTINCT FROM nullif(current_setting('app.billing_actor_id', true), '')::uuid
                OR named <> NEW.shop_count OR hash IS DISTINCT FROM NEW.shop_hash
                OR (SELECT count(*) FROM public.checkout_order_shops s
                    WHERE s.account_id = NEW.account_id AND s.order_id = NEW.order_id) <> named
                OR (NEW.term_id IS NOT NULL AND (SELECT count(*) FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.term_id = NEW.term_id) + (SELECT count(*)
                    FROM public.payment_fulfilment_shops p WHERE p.account_id = NEW.account_id AND p.term_id = NEW.term_id) <> named)
                OR (NEW.term_id IS NOT NULL AND EXISTS (SELECT FROM public.payment_fulfilments f
                    WHERE f.account_id = NEW.account_id AND f.predecessor_term_id = NEW.term_id))
                OR (NEW.snapshot->>'accountId')::uuid IS DISTINCT FROM NEW.account_id
                OR (NEW.snapshot->>'orderId')::uuid IS DISTINCT FROM NEW.order_id
                OR (NEW.snapshot->>'termId')::uuid IS DISTINCT FROM NEW.term_id
                OR (NEW.snapshot->>'amountPaise')::bigint IS DISTINCT FROM NEW.amount_paise
                OR (NEW.snapshot->>'revision')::int IS DISTINCT FROM NEW.quote_revision
                OR coalesce((NEW.snapshot->>'capacity')::int, 0) NOT BETWEEN NEW.shop_count AND 100000
                OR coalesce((NEW.snapshot->>'membersPerShop')::int, 0) NOT BETWEEN 1 AND 100000
                OR (NEW.snapshot->>'shopCount')::int IS DISTINCT FROM NEW.shop_count
                OR NEW.snapshot->>'shopHash' IS DISTINCT FROM NEW.shop_hash
                OR NEW.snapshot->>'snapshotHash' IS DISTINCT FROM NEW.snapshot_hash
                OR NEW.snapshot->>'currency' IS DISTINCT FROM 'INR'
                OR NEW.snapshot->>'priceBasis' IS DISTINCT FROM 'TaxInclusiveFinal'
                OR NEW.snapshot->>'acceptance' IS NOT NULL
                OR (NEW.snapshot->>'createdAt')::timestamptz IS DISTINCT FROM NEW.created_at
                OR (NEW.snapshot->>'expiresAt')::timestamptz IS DISTINCT FROM NEW.expires_at THEN
                RAISE EXCEPTION 'Checkout quote must contain complete coherent evidence' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        """;

    // Money may fund a first purchase or a prior-paid-through renewal; a fulfilment names exactly its quoted term.
    private const string PaymentGuards = """
        CREATE OR REPLACE FUNCTION public.guard_payment_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND (q.term_id IS NULL OR q.snapshot->>'startRule' = 'PriorPaidThrough')
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.snapshot_hash = NEW.snapshot_hash AND NEW.created_at < q.expires_at) THEN
                RAISE EXCEPTION 'Payment attempt must match its accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;

        CREATE OR REPLACE FUNCTION public.guard_manual_payment_receipt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q JOIN public.checkout_order_acceptances a USING (account_id, order_id)
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                        OR (q.term_id IS NOT NULL AND q.snapshot->>'startRule' = 'PriorPaidThrough'))
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND a.accepted_at < q.expires_at) THEN
                RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;

        CREATE FUNCTION public.guard_payment_fulfilment_renewal() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            -- A row without exactly one money source is refused by payment_fulfilment_one_source itself.
            IF (NEW.attempt_id IS NULL) = (NEW.manual_receipt_id IS NULL) THEN
                RETURN NEW;
            END IF;
            IF NOT EXISTS (SELECT FROM public.checkout_orders q WHERE q.account_id = NEW.account_id
                    AND q.order_id = NEW.order_id AND q.term_id IS NOT DISTINCT FROM NEW.predecessor_term_id) THEN
                RAISE EXCEPTION 'A renewal fulfilment must extend exactly its quoted term' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_payment_fulfilment_renewal BEFORE INSERT ON public.payment_fulfilments
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_fulfilment_renewal();
        """;
}
