-- Classifies items by their free-text unit label (quantity-units/1 catalog) and flags items whose
-- recorded quantities carry decimals while the item is classified whole-unit.
-- Same mapping and fractional-history rule as migration AddItemQuantityPrecision, restricted to
-- rows no one has classified yet (unit_code IS NULL), in a single statement so a row is written once.
-- Safe to re-run: an item that already has a unit_code, including one a merchant resolved, is never
-- touched, and the review flag is only raised on rows this statement maps to a whole-unit code whose
-- recorded quantities carry decimals. Run it as the migrator role once every API instance runs the new
-- version, to cover items an old instance created between the migration and the rollout.
-- No quantity column is touched. Rows are tenant data under RLS; the migrator role is exempt.
UPDATE items i
SET unit_code = m.code,
    quantity_scale = m.scale,
    unit_review_required = i.unit_review_required
        OR (m.scale = 0
            AND (EXISTS (SELECT 1 FROM inventory_batches b WHERE b.item_id = i.id AND (b.quantity <> trunc(b.quantity) OR b.original_quantity <> trunc(b.original_quantity)))
              OR EXISTS (SELECT 1 FROM stock_transactions t WHERE t.item_id = i.id AND t.quantity <> trunc(t.quantity))
              OR EXISTS (SELECT 1 FROM sale_items s WHERE s.item_id = i.id AND s.quantity <> trunc(s.quantity))))
FROM (VALUES
        ('PCS', 'PCS', 0),
        ('PC', 'PCS', 0),
        ('PIECE', 'PCS', 0),
        ('PIECES', 'PCS', 0),
        ('NOS', 'PCS', 0),
        ('NO', 'PCS', 0),
        ('NUMBER', 'PCS', 0),
        ('NUMBERS', 'PCS', 0),
        ('UNIT', 'PCS', 0),
        ('UNITS', 'PCS', 0),
        ('EA', 'PCS', 0),
        ('EACH', 'PCS', 0),
        ('PKT', 'PKT', 0),
        ('PACK', 'PKT', 0),
        ('PACKS', 'PKT', 0),
        ('PACKET', 'PKT', 0),
        ('PACKETS', 'PKT', 0),
        ('PAC', 'PKT', 0),
        ('BOX', 'BOX', 0),
        ('BOXES', 'BOX', 0),
        ('DOZ', 'DOZ', 0),
        ('DOZEN', 'DOZ', 0),
        ('DOZENS', 'DOZ', 0),
        ('PAIR', 'PAIR', 0),
        ('PAIRS', 'PAIR', 0),
        ('PRS', 'PAIR', 0),
        ('SET', 'SET', 0),
        ('SETS', 'SET', 0),
        ('BTL', 'BTL', 0),
        ('BOTTLE', 'BTL', 0),
        ('BOTTLES', 'BTL', 0),
        ('BAG', 'BAG', 0),
        ('BAGS', 'BAG', 0),
        ('KG', 'KG', 3),
        ('KGS', 'KG', 3),
        ('KILO', 'KG', 3),
        ('KILOS', 'KG', 3),
        ('KILOGRAM', 'KG', 3),
        ('KILOGRAMS', 'KG', 3),
        ('G', 'G', 0),
        ('GM', 'G', 0),
        ('GMS', 'G', 0),
        ('GRAM', 'G', 0),
        ('GRAMS', 'G', 0),
        ('L', 'L', 3),
        ('LTR', 'L', 3),
        ('LTRS', 'L', 3),
        ('LITRE', 'L', 3),
        ('LITRES', 'L', 3),
        ('LITER', 'L', 3),
        ('LITERS', 'L', 3),
        ('ML', 'ML', 0),
        ('MLT', 'ML', 0),
        ('MILLILITRE', 'ML', 0),
        ('MILLILITRES', 'ML', 0),
        ('MILLILITER', 'ML', 0),
        ('MILLILITERS', 'ML', 0),
        ('M', 'M', 3),
        ('MTR', 'M', 3),
        ('MTRS', 'M', 3),
        ('METRE', 'M', 3),
        ('METRES', 'M', 3),
        ('METER', 'M', 3),
        ('METERS', 'M', 3),
        ('CM', 'CM', 0),
        ('CMS', 'CM', 0),
        ('CENTIMETRE', 'CM', 0),
        ('CENTIMETRES', 'CM', 0),
        ('CENTIMETER', 'CM', 0),
        ('CENTIMETERS', 'CM', 0)
    ) AS m(alias, code, scale)
WHERE i.unit_code IS NULL
  AND upper(btrim(i.uom, E' \t\r\n')) = m.alias;

