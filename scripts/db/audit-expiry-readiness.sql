-- Read only. Supply BOTH explicit inputs; there is no current-shop/current-day fallback.
-- psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -v shop_id='<shop uuid>' \
--   -v business_date='2026-11-09' -f scripts/db/audit-expiry-readiness.sql
-- Use the same shop-local business date as GET /api/expiry-policies/readiness.
-- A restricted connection still needs its authorized app.active_shop_id session scope.
-- An administrator can inspect a shop explicitly; this query never broadens runtime RLS.
-- Counts follow API readiness: nonvoid positive physical lots, independent missing,
-- expired and quarantine flags. Missing includes optional dates; Mode Ignore does not
-- hide readiness risks. The latest item reset (null settings) inherits the default.
-- Items are ALL affected lots (API pages them); invalid old date pairs are additional
-- diagnostic facts, never repaired. Run inside a READ ONLY transaction if desired.
WITH input AS (
    SELECT :'shop_id'::uuid AS shop_id, :'business_date'::date AS business_date
), policy AS (
    SELECT i.shop_id, i.business_date, s.catalog_version, d.sell_through_expiry_date
    FROM input i
    JOIN shop_expiry_states s ON s.shop_id = i.shop_id
    JOIN LATERAL (
        SELECT r.*
        FROM expiry_policy_revisions r
        WHERE r.shop_id = i.shop_id AND r.item_id IS NULL
        ORDER BY r.catalog_version DESC LIMIT 1
    ) d ON true
    WHERE d.catalog_version = s.default_policy_version AND d.mode BETWEEN 0 AND 2
      AND d.warning_days BETWEEN 0 AND 365 AND d.expiry_required IS NOT NULL
      AND d.sell_through_expiry_date IS NOT NULL AND d.override_allowed IS NOT NULL
      AND EXISTS (
          SELECT 1 FROM expiry_policy_revisions baseline
          WHERE baseline.shop_id = i.shop_id AND baseline.catalog_version = 1
            AND baseline.item_id IS NULL AND baseline.mode = 0 AND NOT baseline.expiry_required
            AND baseline.sell_through_expiry_date AND baseline.warning_days = 7
            AND NOT baseline.override_allowed AND baseline.pre_feature_baseline = s.pre_feature_baseline
      )
), lots AS (
    SELECT b.id AS batch_id, b.item_id, it.name AS item_name, b.batch_number,
           b.expiry_date, b.manufacturing_date, b.quantity AS physical_quantity,
           b.quarantined_quantity,
           b.expiry_date IS NULL AS missing_date,
           b.expiry_date IS NOT NULL AND CASE
               WHEN coalesce(o.sell_through_expiry_date, p.sell_through_expiry_date)
               THEN p.business_date > b.expiry_date ELSE p.business_date >= b.expiry_date
           END AS expired,
           b.manufacturing_date > b.expiry_date AS invalid_date_order
    FROM policy p
    JOIN inventory_batches b ON b.shop_id = p.shop_id AND NOT b.is_voided AND b.quantity > 0
    JOIN items it ON it.id = b.item_id AND it.shop_id = p.shop_id
    LEFT JOIN LATERAL (
        SELECT r.sell_through_expiry_date
        FROM expiry_policy_revisions r
        WHERE r.shop_id = p.shop_id AND r.item_id = b.item_id
        ORDER BY r.catalog_version DESC LIMIT 1
    ) o ON true
)
SELECT jsonb_build_object(
    'shopId', p.shop_id, 'businessDate', p.business_date, 'catalogVersion', p.catalog_version,
    'results', jsonb_build_object(
        'totalLots', count(*) FILTER (WHERE missing_date OR expired OR quarantined_quantity > 0),
        'missingDateLots', count(*) FILTER (WHERE missing_date),
        'expiredLots', count(*) FILTER (WHERE expired),
        'quarantinedLots', count(*) FILTER (WHERE quarantined_quantity > 0),
        'physicalQuantity', coalesce(sum(physical_quantity), 0),
        'quarantinedQuantity', coalesce(sum(quarantined_quantity), 0),
        'items', coalesce(jsonb_agg(jsonb_build_object(
            'batchId', batch_id, 'itemId', item_id, 'itemName', item_name, 'batchNumber', batch_number,
            'expiryDate', expiry_date, 'physicalQuantity', physical_quantity,
            'quarantinedQuantity', quarantined_quantity, 'missingDate', missing_date, 'expired', expired
        ) ORDER BY batch_id) FILTER (WHERE missing_date OR expired OR quarantined_quantity > 0), '[]'::jsonb)),
    'invalidDateOrderBatchIds', coalesce(jsonb_agg(batch_id ORDER BY batch_id)
        FILTER (WHERE invalid_date_order), '[]'::jsonb)
)
FROM policy p LEFT JOIN lots ON true
GROUP BY p.shop_id, p.business_date, p.catalog_version;
-- No result means the scoped shop/state/default history is unavailable; investigate via
-- supported API/retained migration logs. It is NOT an empty/ready report or Ignore proof.
