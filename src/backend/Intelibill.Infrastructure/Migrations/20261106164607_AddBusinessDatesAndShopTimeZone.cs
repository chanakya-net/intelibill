using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDatesAndShopTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "business_time_zone",
                table: "shops",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Kolkata");

            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                table: "sales",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_time_zone",
                table: "sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                table: "sale_returns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_time_zone",
                table: "sale_returns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "voided_business_date",
                table: "sale_returns",
                type: "date",
                nullable: true);

            // Shop by shop, because the deployed migrator is not BYPASSRLS. The same idempotent
            // statements live in scripts/db/backfill-business-dates.sql, which closes the
            // rolling-deploy gap afterwards. Dates are read in Asia/Kolkata, the only zone until now.
            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sales ALTER COLUMN business_date SET NOT NULL;
                ALTER TABLE sales ALTER COLUMN business_time_zone SET NOT NULL;
                ALTER TABLE sale_returns ALTER COLUMN business_date SET NOT NULL;
                ALTER TABLE sale_returns ALTER COLUMN business_time_zone SET NOT NULL;
                """);

            // Rolling-deploy guard: a pod still running the previous build inserts sales and returns
            // (and voids returns) without the new columns. These triggers give those rows the IST date
            // they would have been backfilled with, so no row is ever written without a business date.
            migrationBuilder.Sql("""
                CREATE FUNCTION sales_business_date_default() RETURNS trigger AS $fn$
                BEGIN
                    IF NEW.business_time_zone IS NULL THEN
                        NEW.business_time_zone := 'Asia/Kolkata';
                    END IF;
                    IF NEW.business_date IS NULL THEN
                        NEW.business_date := (NEW.sold_at AT TIME ZONE NEW.business_time_zone)::date;
                    END IF;
                    RETURN NEW;
                END
                $fn$ LANGUAGE plpgsql;

                CREATE TRIGGER sales_business_date_default
                    BEFORE INSERT OR UPDATE ON sales
                    FOR EACH ROW EXECUTE FUNCTION sales_business_date_default();

                CREATE FUNCTION sale_returns_business_date_default() RETURNS trigger AS $fn$
                BEGIN
                    IF NEW.business_time_zone IS NULL THEN
                        NEW.business_time_zone := 'Asia/Kolkata';
                    END IF;
                    IF NEW.business_date IS NULL THEN
                        NEW.business_date := (NEW.processed_at AT TIME ZONE NEW.business_time_zone)::date;
                    END IF;
                    IF NEW.voided_at IS NOT NULL AND NEW.voided_business_date IS NULL THEN
                        NEW.voided_business_date := (NEW.voided_at AT TIME ZONE NEW.business_time_zone)::date;
                    END IF;
                    RETURN NEW;
                END
                $fn$ LANGUAGE plpgsql;

                CREATE TRIGGER sale_returns_business_date_default
                    BEFORE INSERT OR UPDATE ON sale_returns
                    FOR EACH ROW EXECUTE FUNCTION sale_returns_business_date_default();
                """);

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_business_date",
                table: "sales",
                columns: ["shop_id", "business_date"]);

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_business_date",
                table: "sale_returns",
                columns: ["shop_id", "business_date"]);

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_voided_business_date",
                table: "sale_returns",
                columns: ["shop_id", "voided_business_date"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS sale_returns_business_date_default ON sale_returns;
                DROP FUNCTION IF EXISTS sale_returns_business_date_default();
                DROP TRIGGER IF EXISTS sales_business_date_default ON sales;
                DROP FUNCTION IF EXISTS sales_business_date_default();
                """);

            migrationBuilder.DropIndex(
                name: "ix_sales_shop_id_business_date",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sale_returns_shop_id_business_date",
                table: "sale_returns");

            migrationBuilder.DropIndex(
                name: "ix_sale_returns_shop_id_voided_business_date",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "business_time_zone",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "business_date",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "business_time_zone",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "business_date",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "business_time_zone",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "voided_business_date",
                table: "sale_returns");
        }
    }
}
