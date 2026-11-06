-- Backfill payment receipts for ledger payments recorded without one.
--
-- The AddPaymentReceiptsAndAllocations migration runs this same statement. Re-run it once after
-- the new API version is serving every request, to cover payments an older instance recorded
-- during the rolling deploy. Idempotent: a ledger entry that already has a receipt is skipped.
--
-- Evidence rules (plan 25): a legacy payment carries no tender and no invoice link, so its method
-- is UNKNOWN_RECORDED_METHOD (never guessed as cash) and it gets no allocation. Its whole amount
-- shows as unallocated credit until an operator allocates it explicitly. The account balance is
-- unchanged: it is still the sum of ledger entries.
--
-- Runs shop by shop with app.active_shop_id set, because the deployed migrator is neither
-- superuser nor BYPASSRLS: FORCE row level security hides every row from a session with no shop.
--
--   psql "host=<server> user=<migrator> dbname=<database>" -f scripts/db/backfill-payment-receipts.sql

DO $backfill$
DECLARE
    target_shop uuid;
BEGIN
    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
        PERFORM set_config('app.active_shop_id', target_shop::text, true);

        INSERT INTO payment_receipts (
            id, shop_id, party_type, party_id, amount, method, bank_account_id, external_reference,
            business_date, received_at, notes, ledger_entry_id, expense_id, is_legacy, created_by, created_at)
        SELECT
            gen_random_uuid(), e.shop_id, 'CUSTOMER', e.customer_id, e.amount, 'UNKNOWN_RECORDED_METHOD', NULL, NULL,
            e.entry_date, e.created_at, e.notes, e.id, NULL, true, e.created_by, now()
        FROM customer_ledger_entries e
        WHERE e.shop_id = target_shop
          AND e.entry_type = 'PAYMENT_RECEIVED'
          AND e.amount > 0
          AND NOT EXISTS (
              SELECT 1 FROM payment_receipts r WHERE r.party_type = 'CUSTOMER' AND r.ledger_entry_id = e.id);

        INSERT INTO payment_receipts (
            id, shop_id, party_type, party_id, amount, method, bank_account_id, external_reference,
            business_date, received_at, notes, ledger_entry_id, expense_id, is_legacy, created_by, created_at)
        SELECT
            gen_random_uuid(), e.shop_id, 'SUPPLIER', e.supplier_id, e.amount, 'UNKNOWN_RECORDED_METHOD', NULL, NULL,
            e.entry_date, e.created_at, e.notes, e.id, x.id, true, e.created_by, now()
        FROM supplier_ledger_entries e
        LEFT JOIN LATERAL (
            SELECT ex.id
            FROM expenses ex
            WHERE ex.supplier_ledger_entry_id = e.id AND ex.shop_id = e.shop_id
            ORDER BY ex.created_at
            LIMIT 1) x ON true
        WHERE e.shop_id = target_shop
          AND e.entry_type = 'PAYMENT_MADE'
          AND e.amount > 0
          AND NOT EXISTS (
              SELECT 1 FROM payment_receipts r WHERE r.party_type = 'SUPPLIER' AND r.ledger_entry_id = e.id);
    END LOOP;

    PERFORM set_config('app.active_shop_id', '', true);
END
$backfill$;
