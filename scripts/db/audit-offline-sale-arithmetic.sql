-- Read-only audit of posted offline sales whose stored amounts do not reconcile (backlog 18).
-- Before the offline arithmetic validator, offline sync trusted submitted monetary totals, so a posted
-- offline sale may disagree with its own lines. This lists every such sale and why; it never changes
-- stock, ledgers or the sale. File each row as a review case and correct it through the normal
-- correction workflows. Only invariants that hold under every calculation contract are checked
-- (exact sums and per-line identities); recomputing rounding is the sync validator's job.
-- Runs shop by shop with app.active_shop_id set, so it works under the tenant policies as well as for
-- a role that bypasses them. Run with psql; results are emitted as NOTICE lines.
DO $audit$
DECLARE
    target_shop uuid;
    finding record;
    total_findings integer := 0;
BEGIN
    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
        PERFORM set_config('app.active_shop_id', target_shop::text, true);

        FOR finding IN
            WITH line_sums AS (
                SELECT i.sale_id,
                    sum(i.pre_tax_amount_before_discount) AS pre_tax,
                    sum(i.item_discount_amount + i.sale_discount_amount) AS discounts,
                    sum(i.tax_amount) AS tax,
                    sum(i.total_amount) AS total,
                    count(*) FILTER (
                        WHERE i.taxable_amount <> i.pre_tax_amount_before_discount - i.item_discount_amount - i.sale_discount_amount
                           OR i.total_amount <> i.taxable_amount + i.tax_amount
                           OR i.item_discount_amount + i.sale_discount_amount > i.pre_tax_amount_before_discount) AS broken_lines
                FROM sale_items i
                JOIN sales s ON s.id = i.sale_id
                WHERE s.shop_id = target_shop AND s.source = 2
                GROUP BY i.sale_id
            )
            SELECT s.shop_id, s.id AS sale_id, s.invoice_number, s.client_sale_id, s.device_id,
                concat_ws('; ',
                    CASE WHEN s.total_amount <> l.total THEN format('totalAmount %s, lines %s', s.total_amount, l.total) END,
                    CASE WHEN s.total_tax_amount <> l.tax THEN format('totalTaxAmount %s, lines %s', s.total_tax_amount, l.tax) END,
                    CASE WHEN s.total_discount_amount <> l.discounts THEN format('totalDiscountAmount %s, lines %s', s.total_discount_amount, l.discounts) END,
                    CASE WHEN s.subtotal_before_discount <> l.pre_tax THEN format('subtotalBeforeDiscount %s, lines %s', s.subtotal_before_discount, l.pre_tax) END,
                    CASE WHEN s.paid_amount + s.due_amount <> s.total_amount THEN format('paid %s + due %s <> total %s', s.paid_amount, s.due_amount, s.total_amount) END,
                    CASE WHEN l.broken_lines > 0 THEN format('%s line(s) where taxable, tax and total disagree or discounts exceed the basis', l.broken_lines) END
                ) AS reasons
            FROM sales s
            JOIN line_sums l ON l.sale_id = s.id
            WHERE s.shop_id = target_shop
              AND (s.total_amount <> l.total
                OR s.total_tax_amount <> l.tax
                OR s.total_discount_amount <> l.discounts
                OR s.subtotal_before_discount <> l.pre_tax
                OR s.paid_amount + s.due_amount <> s.total_amount
                OR l.broken_lines > 0)
            ORDER BY s.sold_at, s.id
        LOOP
            total_findings := total_findings + 1;
            RAISE NOTICE 'shop % sale % invoice % (client sale %, device %): %',
                finding.shop_id, finding.sale_id, finding.invoice_number, finding.client_sale_id, finding.device_id, finding.reasons;
        END LOOP;
    END LOOP;

    RAISE NOTICE 'offline sales needing review: %', total_findings;
END
$audit$;
