-- Maintenance-only resume gate. Run as the licensing table owner with all receipt writers drained.
-- psql -X -v ON_ERROR_STOP=1 "$LICENSING_MIGRATOR_CONNECTION" -f scripts/db/check-payment-confirmation-completeness.sql
-- Rollback restores FORCE RLS even on failure; no stored facts or policies are changed.
BEGIN;
SET LOCAL lock_timeout = '5s';
ALTER TABLE public.verified_payments NO FORCE ROW LEVEL SECURITY;
ALTER TABLE public.manual_payment_receipts NO FORCE ROW LEVEL SECURITY;
ALTER TABLE public.payment_confirmations NO FORCE ROW LEVEL SECURITY;
DO $$ BEGIN
    IF EXISTS (
        SELECT FROM public.verified_payments p
        WHERE NOT EXISTS (SELECT FROM public.payment_confirmations c
            WHERE c.attempt_id = p.attempt_id AND c.account_id = p.account_id AND c.order_id = p.order_id)
    ) OR EXISTS (
        SELECT FROM public.manual_payment_receipts p
        WHERE NOT EXISTS (SELECT FROM public.payment_confirmations c
            WHERE c.manual_receipt_id = p.receipt_id AND c.account_id = p.account_id AND c.order_id = p.order_id)
    ) THEN
        RAISE EXCEPTION 'Missing payment confirmations: keep receipt writers paused; reviewed retained catch-up required'
            USING ERRCODE = '23514';
    END IF;
END $$;
ROLLBACK;
