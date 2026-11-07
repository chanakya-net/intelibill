-- Run as the schema migrator. FORCE RLS also applies to the table owner.
-- UNION deduplicates IDs, so malformed historical cycles terminate safely.
DO $backfill$
DECLARE
    target_shop uuid;
    previous_shop text := current_setting('app.active_shop_id', true);
BEGIN
    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
        PERFORM set_config('app.active_shop_id', target_shop::text, true);
        WITH RECURSIVE settlement_ancestry AS (
            SELECT id, shop_id FROM expenses
            WHERE supplier_ledger_entry_id IS NOT NULL AND shop_id = target_shop
            UNION
            SELECT child.id, child.shop_id
            FROM expenses child JOIN settlement_ancestry parent
              ON child.original_expense_id = parent.id AND child.shop_id = parent.shop_id
        )
        UPDATE expenses e SET treatment = 2
        FROM settlement_ancestry source
        WHERE e.id = source.id AND e.shop_id = source.shop_id AND e.treatment <> 2;
    END LOOP;
    PERFORM set_config('app.active_shop_id', coalesce(previous_shop, ''), true);
END
$backfill$;
