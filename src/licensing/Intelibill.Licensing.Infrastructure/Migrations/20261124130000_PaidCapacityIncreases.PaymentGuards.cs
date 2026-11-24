namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidCapacityIncreases
{
    private const string PaymentGuards = """
        CREATE OR REPLACE FUNCTION public.guard_payment_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.checkout_orders q
                JOIN public.checkout_order_acceptances c ON c.account_id = q.account_id AND c.order_id = q.order_id
                WHERE q.account_id = NEW.account_id AND q.order_id = NEW.order_id
                    AND ((q.term_id IS NULL AND q.snapshot->>'startRule' = 'Fulfilment')
                        OR (q.term_id IS NOT NULL AND (q.snapshot->>'startRule' = 'PriorPaidThrough'
                            OR q.snapshot->>'startRule' = 'CapacityIncrease' AND EXISTS (SELECT FROM public.capacity_change_quotes z
                                WHERE z.account_id=q.account_id AND z.quote_id=q.order_id AND z.term_id=q.term_id
                                    AND z.snapshot_hash=q.snapshot->'increase'->>'quoteHash' AND z.amount_paise=q.amount_paise
                                    AND z.outcome='Payable' AND NEW.created_at < z.paid_through
                                    AND (NEW.provider_id, NEW.provider_account_id, NEW.environment, NEW.configuration_version) =
                                    (z.provider_id, z.provider_account_id, z.provider_environment, z.provider_configuration_version)
                                    AND coalesce(NEW.selection_revision,0)=z.routing_revision)
                            OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
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
                        OR (q.term_id IS NOT NULL AND (q.snapshot->>'startRule' = 'PriorPaidThrough'
                            OR q.snapshot->>'startRule' = 'CapacityIncrease' AND EXISTS (SELECT FROM public.capacity_change_quotes z
                                WHERE z.account_id=q.account_id AND z.quote_id=q.order_id AND z.term_id=q.term_id
                                    AND z.snapshot_hash=q.snapshot->'increase'->>'quoteHash' AND z.amount_paise=q.amount_paise
                                    AND z.outcome='Payable')
                            OR q.snapshot->>'startRule' = 'RestoredAccess' AND q.snapshot->'freshStart'->>'policy' = 'paid-fresh-start/1')))
                    AND q.amount_paise = NEW.amount_paise AND q.snapshot->>'currency' = NEW.currency
                    AND q.quote_revision = NEW.quote_revision AND q.snapshot_hash = NEW.snapshot_hash
                    AND a.accepted_at < q.expires_at) THEN
                RAISE EXCEPTION 'Manual receipt must match the accepted order' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;

        """;

}
