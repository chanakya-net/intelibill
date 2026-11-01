-- Backfills purchase_receipt_values for active batches that have none (plan 15).
-- Same statement as migration 20261101190000_AddPurchaseReceiptValuesAndPriceRevisions. Idempotent:
-- re-run it as the migrator role once every API instance runs the new version, to cover batches an
-- old instance created between the migration and the rollout.
-- A receipt is RESOLVED only on unambiguous evidence; anything else is LEGACY_UNRESOLVED with no
-- amount, never a total inferred from remaining stock.
INSERT INTO purchase_receipt_values (
    id, shop_id, inventory_batch_id, item_id, supplier_id, received_quantity,
    original_total_cost, effective_total_cost, source, status, source_reference,
    purchase_order_receipt_line_id, received_at, created_by, created_at)
SELECT
    gen_random_uuid(), b.shop_id, b.id, b.item_id,
    COALESCE(gr.supplier_id, b.supplier_id),
    b.original_quantity,
    CASE WHEN ev.resolved THEN ev.total END,
    CASE WHEN ev.resolved THEN ev.total END,
    CASE WHEN pol.id IS NOT NULL THEN 'PURCHASE_ORDER' ELSE 'LEGACY_BACKFILL' END,
    CASE WHEN ev.resolved THEN 'RESOLVED' ELSE 'LEGACY_UNRESOLVED' END,
    left(opening.reference_number, 80),
    pol.id,
    COALESCE(opening.performed_at, b.created_at),
    b.created_by,
    now()
FROM inventory_batches b
CROSS JOIN LATERAL (
    SELECT count(*) AS entries, min(e.amount) AS amount, min(e.supplier_id::text)::uuid AS supplier_id
    FROM supplier_ledger_entries e
    WHERE e.batch_id = b.id AND e.entry_type = 'GOODS_RECEIVED') gr
LEFT JOIN purchase_order_receipt_lines pol ON pol.inventory_batch_id = b.id
LEFT JOIN LATERAL (
    SELECT t.performed_at, t.reference_number
    FROM stock_transactions t
    WHERE t.inventory_batch_id = b.id
      AND t.transaction_type = 'IN'
      AND t.quantity = b.original_quantity
      AND t.reference_number IS DISTINCT FROM 'Batch Correction'
    ORDER BY t.performed_at
    LIMIT 1) opening ON true
CROSS JOIN LATERAL (
    SELECT CASE
        WHEN pol.id IS NOT NULL THEN round(pol.total_purchase_cost, 2)
        WHEN gr.entries = 1 THEN gr.amount
        WHEN gr.entries = 0 AND b.cost_price = 0 THEN 0
    END AS total) receipt_total
CROSS JOIN LATERAL (
    SELECT
        COALESCE(opening.performed_at IS NOT NULL
            AND gr.entries <= 1
            AND receipt_total.total >= 0
            AND COALESCE(gr.amount, 0) = receipt_total.total, false) AS resolved,
        receipt_total.total) ev
WHERE b.is_voided = false
  AND b.original_quantity > 0
  AND NOT EXISTS (SELECT 1 FROM purchase_receipt_values r WHERE r.inventory_batch_id = b.id);
