namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidFreshStarts
{
    private const string GrantAdmin = "nullif(current_setting('app.grant_admin_id', true), '')::uuid";

    private static readonly string AdjustmentGuards = $$"""
        ALTER TABLE public.complimentary_grant_receipts ADD COLUMN payment_order_id uuid,
            ADD FOREIGN KEY (account_id, payment_order_id) REFERENCES public.payment_fulfilments(account_id, order_id);
        CREATE TABLE public.service_restoration_adjustments (
            account_id uuid NOT NULL, order_id uuid NOT NULL, grant_id uuid PRIMARY KEY, paid_term_id uuid NOT NULL,
            FOREIGN KEY (account_id, order_id) REFERENCES public.payment_fulfilments(account_id, order_id),
            FOREIGN KEY (grant_id) REFERENCES public.complimentary_grant_receipts(grant_id),
            FOREIGN KEY (account_id, paid_term_id) REFERENCES public.subscription_coverage_terms(account_id, term_id)
        );
        CREATE INDEX service_restoration_order ON public.service_restoration_adjustments(account_id, order_id);

        CREATE FUNCTION public.guard_service_restoration_adjustment() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.complimentary_grant_receipts r
                    JOIN public.payment_fulfilments f ON f.account_id = r.account_id AND f.order_id = r.payment_order_id
                    JOIN public.complimentary_grants g ON g.grant_id = r.grant_id AND g.account_id = r.account_id
                    WHERE r.grant_id = NEW.grant_id AND r.account_id = NEW.account_id AND f.order_id = NEW.order_id
                        AND f.term_id = NEW.paid_term_id AND r.admin_id = {{GrantAdmin}}
                        AND g.shop_capacity = (SELECT count(*) FROM public.complimentary_grant_shops s
                            JOIN public.payment_fulfilment_shops p ON p.account_id = s.account_id AND p.shop_id = s.shop_id
                                AND p.affiliation_request_id = s.affiliation_request_id
                            WHERE s.grant_id = NEW.grant_id AND p.order_id = NEW.order_id)) THEN
                RAISE EXCEPTION 'Restoration adjustment must match the approved grant and paid coverage' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_service_restoration_adjustment BEFORE INSERT ON public.service_restoration_adjustments
            FOR EACH ROW EXECUTE FUNCTION public.guard_service_restoration_adjustment();

        CREATE FUNCTION public.require_service_restoration_adjustment() RETURNS trigger LANGUAGE plpgsql
        SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.service_restoration_adjustments a
                WHERE a.grant_id = NEW.grant_id AND a.account_id = NEW.account_id AND a.order_id = NEW.payment_order_id) THEN
                RAISE EXCEPTION 'Restoration grant linkage is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER require_service_restoration_adjustment AFTER INSERT ON public.complimentary_grant_receipts
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.payment_order_id IS NOT NULL)
            EXECUTE FUNCTION public.require_service_restoration_adjustment();
        """;
}
