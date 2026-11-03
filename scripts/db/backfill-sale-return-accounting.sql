-- Backfills the accounting snapshot of every return that has none (plan 17).
-- Same statement as migration 20261103120000_AddReturnAccountingSnapshots. Idempotent: re-run it as
-- the migrator role once every API instance runs the new version, to cover returns an old instance
-- recorded between the migration and the rollout. A second run affects no rows.
-- Each line is split by proportional-line-tax/1 (recorded as backfill:proportional-line-tax/1). A
-- return is BALANCED only when every line is classifiable, its lines add up to its total and its
-- payout has evidence: a refund destination, or exactly one credit note of the payout amount issued
-- for it. Anything else is NEEDS_REVIEW with the reason, and no settlement is ever inferred.
WITH pending AS (
    SELECT r.id, r.shop_id, r.return_number, r.total_refund_amount, r.due_reduction_amount,
        r.payout_amount, r.payout_destination, s.id AS sale_id, s.customer_id, s.invoice_number
    FROM sale_returns r
    JOIN sales s ON s.id = r.sale_id
    WHERE r.accounting_status IS NULL
),
split AS (
    SELECT i.id, i.sale_return_id, i.approved_refund_amount AS approved, share.supported,
        share.within_share - share.tax AS taxable,
        share.tax,
        i.approved_refund_amount - share.within_share AS non_tax
    FROM sale_return_items i
    JOIN pending p ON p.id = i.sale_return_id
    CROSS JOIN LATERAL (
        SELECT
            i.approved_refund_amount = 0
                OR (i.max_refund_amount > 0 AND i.tax_amount >= 0 AND i.tax_amount <= i.max_refund_amount) AS supported,
            LEAST(i.approved_refund_amount, i.max_refund_amount) AS within_share) basis
    CROSS JOIN LATERAL (
        SELECT basis.supported, basis.within_share,
            CASE WHEN basis.supported AND i.approved_refund_amount > 0
                THEN round(basis.within_share * i.tax_amount / i.max_refund_amount, 2)
                ELSE 0
            END AS tax) share
),
totals AS (
    SELECT x.sale_return_id, count(*) AS line_count, bool_and(x.supported) AS supported,
        sum(x.approved) AS approved, sum(x.taxable) AS taxable, sum(x.tax) AS tax, sum(x.non_tax) AS non_tax
    FROM split x
    GROUP BY x.sale_return_id
),
notes AS (
    SELECT n.sale_return_id, count(*) AS matches, min(n.id::text)::uuid AS credit_note_id
    FROM credit_notes n
    JOIN pending p ON p.id = n.sale_return_id
    WHERE n.original_amount = p.payout_amount
    GROUP BY n.sale_return_id
),
receivables AS (
    SELECT p.id AS sale_return_id, count(*) AS matches, min(e.id::text)::uuid AS ledger_entry_id
    FROM pending p
    JOIN customer_ledger_entries e
        ON e.sale_id = p.sale_id
        AND e.entry_type = 'RETURN_CREDIT'
        AND e.amount = p.due_reduction_amount
        AND e.notes = 'Return credit from ' || p.return_number || ' for sale ' || p.invoice_number
    WHERE p.due_reduction_amount > 0
    GROUP BY p.id
),
verdict AS (
    SELECT p.*, t.taxable, t.tax, t.non_tax, n.credit_note_id,
        CASE WHEN r.matches = 1 THEN r.ledger_entry_id END AS ledger_entry_id,
        CASE
            WHEN t.sale_return_id IS NULL THEN
                'Backfill: the return has no lines to classify.'
            WHEN NOT t.supported THEN
                'Backfill: a returned line has no paid value or its tax is outside its gross, so its tax cannot be split.'
            WHEN t.approved <> p.total_refund_amount THEN
                'Backfill: the line refunds do not add up to the return''s total refund.'
            WHEN p.payout_amount > 0 AND p.payout_destination IS NULL THEN
                'Backfill: the payout has no recorded destination, so where the money went is not evidenced.'
            WHEN p.payout_amount > 0 AND p.payout_destination = 1 AND COALESCE(n.matches, 0) <> 1 THEN
                'Backfill: the payout went to store credit, but no single credit note of that amount was issued for the return.'
            WHEN p.payout_amount > 0 AND p.payout_destination NOT IN (1, 2) THEN
                'Backfill: the payout destination is not recognised.'
        END AS review_reason
    FROM pending p
    LEFT JOIN totals t ON t.sale_return_id = p.id
    LEFT JOIN notes n ON n.sale_return_id = p.id
    LEFT JOIN receivables r ON r.sale_return_id = p.id
),
line_postings AS (
    UPDATE sale_return_items i
    SET adjustment_taxable_amount = x.taxable,
        adjustment_tax_amount = x.tax,
        adjustment_non_tax_amount = x.non_tax
    FROM split x
    JOIN verdict v ON v.id = x.sale_return_id
    WHERE i.id = x.id AND v.review_reason IS NULL
    RETURNING i.id
),
settlement_postings AS (
    INSERT INTO sale_return_settlements (
        id, shop_id, sale_return_id, sequence, kind, amount,
        customer_id, credit_note_id, customer_ledger_entry_id, created_at)
    SELECT gen_random_uuid(), v.shop_id, v.id, 1, 'RECEIVABLE_REDUCTION', v.due_reduction_amount,
        v.customer_id, NULL, v.ledger_entry_id, now()
    FROM verdict v
    WHERE v.review_reason IS NULL AND v.due_reduction_amount > 0
    UNION ALL
    SELECT gen_random_uuid(), v.shop_id, v.id,
        CASE WHEN v.due_reduction_amount > 0 THEN 2 ELSE 1 END,
        CASE WHEN v.payout_destination = 1 THEN 'STORE_CREDIT_ISSUED' ELSE 'EXTERNAL_REFUND' END,
        v.payout_amount,
        v.customer_id,
        CASE WHEN v.payout_destination = 1 THEN v.credit_note_id END,
        NULL,
        now()
    FROM verdict v
    WHERE v.review_reason IS NULL AND v.payout_amount > 0
    RETURNING sale_return_id
)
UPDATE sale_returns r
SET accounting_status = CASE WHEN v.review_reason IS NULL THEN 'BALANCED' ELSE 'NEEDS_REVIEW' END,
    accounting_policy_version = 'backfill:proportional-line-tax/1',
    accounting_review_reason = v.review_reason,
    adjustment_taxable_amount = CASE WHEN v.review_reason IS NULL THEN v.taxable END,
    adjustment_tax_amount = CASE WHEN v.review_reason IS NULL THEN v.tax END,
    adjustment_non_tax_amount = CASE WHEN v.review_reason IS NULL THEN v.non_tax END
FROM verdict v
WHERE r.id = v.id;
