using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleDiscountPersistenceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "configured_sale_rule_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "configured_sale_rule_percentage",
                table: "sales",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "configured_sale_rule_threshold_amount",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "configured_sale_rule_type",
                table: "sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sale_discount_override_type",
                table: "sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "sale_discount_override_value",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal_before_discount",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_before_discount",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_discount_amount",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "configured_batch_rule_id",
                table: "sale_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "configured_batch_rule_percentage",
                table: "sale_items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "final_sales_price",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "item_discount_amount",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "item_discount_override_type",
                table: "sale_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "item_discount_override_value",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "original_sales_price",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pre_tax_amount_before_discount",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "sale_discount_amount",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_amount",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_amount",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE sales
                SET
                    subtotal_before_discount = ROUND(total_amount - total_tax_amount, 2),
                    total_before_discount = total_amount,
                    total_discount_amount = 0,
                    sale_discount_override_type = 0,
                    sale_discount_override_value = 0
                ;

                UPDATE sale_items
                SET
                    original_sales_price = sales_price,
                    final_sales_price = sales_price,
                    pre_tax_amount_before_discount = CASE
                        WHEN is_price_including_tax AND tax_rate_percent > 0
                            THEN ROUND((quantity * sales_price) - ((quantity * sales_price * tax_rate_percent) / (100 + tax_rate_percent)), 2)
                        ELSE ROUND(quantity * sales_price, 2)
                    END,
                    item_discount_amount = 0,
                    sale_discount_amount = 0,
                    taxable_amount = CASE
                        WHEN is_price_including_tax AND tax_rate_percent > 0
                            THEN ROUND((quantity * sales_price) - ((quantity * sales_price * tax_rate_percent) / (100 + tax_rate_percent)), 2)
                        ELSE ROUND(quantity * sales_price, 2)
                    END,
                    tax_amount = CASE
                        WHEN tax_rate_percent <= 0
                            THEN 0
                        WHEN is_price_including_tax
                            THEN ROUND((quantity * sales_price * tax_rate_percent) / (100 + tax_rate_percent), 2)
                        ELSE ROUND((quantity * sales_price * tax_rate_percent) / 100, 2)
                    END,
                    total_amount = CASE
                        WHEN is_price_including_tax
                            THEN ROUND(quantity * sales_price, 2)
                        ELSE ROUND((quantity * sales_price) + ((quantity * sales_price * tax_rate_percent) / 100), 2)
                    END,
                    item_discount_override_type = 0,
                    item_discount_override_value = 0
                ;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_configured_sale_rule_id",
                table: "sales",
                columns: new[] { "shop_id", "configured_sale_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id_configured_batch_rule_id",
                table: "sale_items",
                columns: new[] { "shop_id", "configured_batch_rule_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_shop_id_configured_sale_rule_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sale_items_shop_id_configured_batch_rule_id",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "configured_sale_rule_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "configured_sale_rule_percentage",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "configured_sale_rule_threshold_amount",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "configured_sale_rule_type",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "sale_discount_override_type",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "sale_discount_override_value",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "subtotal_before_discount",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "total_before_discount",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "total_discount_amount",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "configured_batch_rule_id",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "configured_batch_rule_percentage",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "final_sales_price",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "item_discount_amount",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "item_discount_override_type",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "item_discount_override_value",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "original_sales_price",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "pre_tax_amount_before_discount",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "sale_discount_amount",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "taxable_amount",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "total_amount",
                table: "sale_items");
        }
    }
}
