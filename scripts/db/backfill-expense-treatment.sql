-- Provenance only: same-shop descendants of supplier-linked roots are settlements.
-- UNION deduplicates IDs, so malformed historical cycles terminate safely.
WITH RECURSIVE settlement_ancestry AS (
    SELECT id, shop_id FROM expenses WHERE supplier_ledger_entry_id IS NOT NULL
    UNION
    SELECT child.id, child.shop_id
    FROM expenses child
    JOIN settlement_ancestry parent
      ON child.original_expense_id = parent.id AND child.shop_id = parent.shop_id
)
UPDATE expenses e SET treatment = 2
FROM settlement_ancestry source
WHERE e.id = source.id AND e.shop_id = source.shop_id AND e.treatment <> 2;
