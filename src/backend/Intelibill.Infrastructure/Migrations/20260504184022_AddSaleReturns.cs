using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sale_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_refund_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_reduction_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payout_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    customer_balance_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    customer_balance_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_returns", x => x.id);
                    table.CheckConstraint("ck_sale_returns_refund_amounts_non_negative", "total_refund_amount >= 0 AND due_reduction_amount >= 0 AND payout_amount >= 0 AND total_taxable_amount >= 0 AND total_tax_amount >= 0");
                    table.CheckConstraint("ck_sale_returns_refund_split", "due_reduction_amount + payout_amount = total_refund_amount");
                    table.CheckConstraint("ck_sale_returns_void_audit", "(is_voided = false AND voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR (is_voided = true AND voided_at IS NOT NULL AND voided_by IS NOT NULL AND void_reason IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_sale_returns_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_returns_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_return_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    original_cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    original_sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    original_tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    original_is_price_including_tax = table.Column<bool>(type: "boolean", nullable: false),
                    max_refund_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    approved_refund_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_return_items", x => x.id);
                    table.CheckConstraint("ck_sale_return_items_amounts_non_negative", "original_cost_price >= 0 AND original_sales_price >= 0 AND max_refund_amount >= 0 AND approved_refund_amount >= 0 AND taxable_amount >= 0 AND tax_amount >= 0");
                    table.CheckConstraint("ck_sale_return_items_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_sale_return_items_refund_cap", "approved_refund_amount <= max_refund_amount");
                    table.CheckConstraint("ck_sale_return_items_tax_rate_range", "original_tax_rate_percent >= 0 AND original_tax_rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_sale_return_items_sale_items_sale_item_id",
                        column: x => x.sale_item_id,
                        principalTable: "sale_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_return_items_sale_returns_sale_return_id",
                        column: x => x.sale_return_id,
                        principalTable: "sale_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sale_return_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_return_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_sale_id",
                table: "sale_return_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_sale_item_id",
                table: "sale_return_items",
                column: "sale_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_sale_return_id",
                table: "sale_return_items",
                column: "sale_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_shop_id_condition",
                table: "sale_return_items",
                columns: new[] { "shop_id", "condition" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_shop_id_sale_id",
                table: "sale_return_items",
                columns: new[] { "shop_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_shop_id_sale_item_id",
                table: "sale_return_items",
                columns: new[] { "shop_id", "sale_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_sale_id",
                table: "sale_returns",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_is_voided",
                table: "sale_returns",
                columns: new[] { "shop_id", "is_voided" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_processed_at",
                table: "sale_returns",
                columns: new[] { "shop_id", "processed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_return_number",
                table: "sale_returns",
                columns: new[] { "shop_id", "return_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_sale_id",
                table: "sale_returns",
                columns: new[] { "shop_id", "sale_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_return_items");

            migrationBuilder.DropTable(
                name: "sale_returns");
        }
    }
}
