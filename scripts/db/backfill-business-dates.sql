-- Backfills sales.business_date / business_time_zone and sale_returns.business_date /
-- business_time_zone / voided_business_date for rows written by a build that predates them.
--
-- Run once after every pod runs the build that writes business dates. The migration already
-- backfilled existing rows and installed BEFORE INSERT OR UPDATE triggers that fill the columns for
-- an old pod's writes, so this is a safety net: it is idempotent and touches only rows still NULL.
-- Dates are read in Asia/Kolkata, the only business zone before shops carried their own.
--
-- Shop by shop, because row-level security hides other shops' rows from a role that is not
-- BYPASSRLS. Run as the migrator/owner role: psql -f scripts/db/backfill-business-dates.sql
DO $backfill$
DECLARE
    target_shop uuid;
BEGIN
    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
        PERFORM set_config('app.active_shop_id', target_shop::text, true);

        UPDATE sales
        SET business_time_zone = COALESCE(business_time_zone, 'Asia/Kolkata'),
            business_date = COALESCE(business_date, (sold_at AT TIME ZONE 'Asia/Kolkata')::date)
        WHERE shop_id = target_shop
          AND (business_date IS NULL OR business_time_zone IS NULL);

        UPDATE sale_returns
        SET business_time_zone = COALESCE(business_time_zone, 'Asia/Kolkata'),
            business_date = COALESCE(business_date, (processed_at AT TIME ZONE 'Asia/Kolkata')::date),
            voided_business_date = CASE
                WHEN voided_at IS NOT NULL AND voided_business_date IS NULL
                    THEN (voided_at AT TIME ZONE 'Asia/Kolkata')::date
                ELSE voided_business_date
            END
        WHERE shop_id = target_shop
          AND (business_date IS NULL
               OR business_time_zone IS NULL
               OR (voided_at IS NOT NULL AND voided_business_date IS NULL));
    END LOOP;

    PERFORM set_config('app.active_shop_id', '', true);
END
$backfill$;
