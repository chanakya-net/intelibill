using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemQuantityPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "quantity_scale",
                table: "items",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "unit_code",
                table: "items",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "unit_review_required",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_items_quantity_scale",
                table: "items",
                sql: "quantity_scale BETWEEN 0 AND 3");

            // Known unit labels get their catalog code and scale (quantity-units/1, pinned by
            // tests/contracts/quantity-units-v1.json); a label the catalog does not know stays a custom
            // unit (unit_code NULL) and is flagged for the merchant to classify. A whole-unit item that
            // already holds fractional quantities is flagged too. No quantity column is changed.
            // The first and third statements are idempotent and live in
            // scripts/db/backfill-item-quantity-precision.sql, to re-run after the new API version is
            // live for items an old instance created meanwhile. Runs shop by shop with
            // app.active_shop_id set, because FORCE row level security binds the migrator as table
            // owner: without a shop context the statements would see no rows and change nothing.
            migrationBuilder.Sql("""
                DO $backfill$
                DECLARE
                    target_shop uuid;
                BEGIN
                    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
                        PERFORM set_config('app.active_shop_id', target_shop::text, true);

                        UPDATE items i
                        SET unit_code = m.code, quantity_scale = m.scale
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

                        UPDATE items
                        SET unit_review_required = true
                        WHERE unit_code IS NULL
                          AND unit_review_required = false;

                        UPDATE items i
                        SET unit_review_required = true
                        WHERE i.quantity_scale = 0
                          AND i.unit_review_required = false
                          AND (EXISTS (SELECT 1 FROM inventory_batches b WHERE b.item_id = i.id AND (b.quantity <> trunc(b.quantity) OR b.original_quantity <> trunc(b.original_quantity)))
                            OR EXISTS (SELECT 1 FROM stock_transactions t WHERE t.item_id = i.id AND t.quantity <> trunc(t.quantity))
                            OR EXISTS (SELECT 1 FROM sale_items s WHERE s.item_id = i.id AND s.quantity <> trunc(s.quantity)));
                    END LOOP;

                    PERFORM set_config('app.active_shop_id', '', true);
                END
                $backfill$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_items_quantity_scale",
                table: "items");

            migrationBuilder.DropColumn(
                name: "quantity_scale",
                table: "items");

            migrationBuilder.DropColumn(
                name: "unit_code",
                table: "items");

            migrationBuilder.DropColumn(
                name: "unit_review_required",
                table: "items");
        }
    }
}
