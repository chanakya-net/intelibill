namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class RefundRequests
{
    // Invoker under forced RLS: confirmation must belong to the same account and order and carry the frozen pin/amount.
    private const string Guards = """
        CREATE FUNCTION public.guard_refund_request() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.payment_confirmations c
                WHERE c.confirmation_id = NEW.confirmation_id
                    AND c.account_id = NEW.account_id
                    AND c.order_id = NEW.order_id
                    AND c.attempt_id IS NOT DISTINCT FROM NEW.attempt_id
                    AND c.manual_receipt_id IS NOT DISTINCT FROM NEW.manual_receipt_id
                    AND (c.facts->'receipt'->>'amountPaise')::bigint = NEW.payment_amount_paise
                    AND c.facts->'receipt'->>'currency' = NEW.currency
                    AND c.facts->>'providerId' = NEW.provider_id
                    AND c.facts->>'providerAccountId' = NEW.provider_account_id
                    AND c.facts->>'environment' = NEW.environment
                    AND c.facts->>'configurationVersion' = NEW.configuration_version) THEN
                RAISE EXCEPTION 'Refund request must match a verified payment confirmation for the same account and order'
                    USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER refund_request_guard BEFORE INSERT ON public.refund_requests
            FOR EACH ROW EXECUTE FUNCTION public.guard_refund_request();
        """;
}
