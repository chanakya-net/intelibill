using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    adjustment_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_impact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    batch_quantity_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    batch_quantity_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    inventory_quantity_before = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    inventory_quantity_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_adjustments", x => x.id);
                    table.CheckConstraint("ck_inventory_adjustments_cost_impact_positive", "cost_impact > 0");
                    table.CheckConstraint("ck_inventory_adjustments_direction_reason", "((direction = 'Decrease' AND reason IN ('Damaged', 'Expired', 'Stolen', 'MissingLost', 'StockCountCorrection', 'OtherLoss')) OR (direction = 'Increase' AND reason IN ('FoundStock', 'StockCountCorrection', 'ReturnRestockCorrection', 'OtherGain')))");
                    table.CheckConstraint("ck_inventory_adjustments_other_reason_notes", "(reason NOT IN ('OtherLoss', 'OtherGain')) OR (notes IS NOT NULL AND length(btrim(notes)) > 0)");
                    table.CheckConstraint("ck_inventory_adjustments_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_inventory_adjustments_quantity_snapshots", "((direction = 'Decrease' AND batch_quantity_before - quantity = batch_quantity_after AND inventory_quantity_before - quantity = inventory_quantity_after) OR (direction = 'Increase' AND batch_quantity_before + quantity = batch_quantity_after AND inventory_quantity_before + quantity = inventory_quantity_after))");
                    table.CheckConstraint("ck_inventory_adjustments_quantity_snapshots_non_negative", "batch_quantity_before >= 0 AND batch_quantity_after >= 0 AND inventory_quantity_before >= 0 AND inventory_quantity_after >= 0");
                    table.CheckConstraint("ck_inventory_adjustments_unit_cost_non_negative", "unit_cost >= 0");
                    table.CheckConstraint("ck_inventory_adjustments_void_audit", "(is_voided = false AND voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL AND reversal_stock_transaction_id IS NULL) OR (is_voided = true AND voided_at IS NOT NULL AND voided_by IS NOT NULL AND void_reason IS NOT NULL AND reversal_stock_transaction_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_inventory_batches_inventory_batch_id_",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_adjustments_stock_transactions_reversal_stock_tra",
                        column: x => x.reversal_stock_transaction_id,
                        principalTable: "stock_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_inventory_batch_id_item_id_shop_id",
                table: "inventory_adjustments",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_item_id_shop_id",
                table: "inventory_adjustments",
                columns: new[] { "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_reversal_stock_transaction_id",
                table: "inventory_adjustments",
                column: "reversal_stock_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_shop_id_adjustment_number",
                table: "inventory_adjustments",
                columns: new[] { "shop_id", "adjustment_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_shop_id_inventory_batch_id_performed_",
                table: "inventory_adjustments",
                columns: new[] { "shop_id", "inventory_batch_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_shop_id_is_voided",
                table: "inventory_adjustments",
                columns: new[] { "shop_id", "is_voided" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_adjustments_shop_id_item_id_performed_at",
                table: "inventory_adjustments",
                columns: new[] { "shop_id", "item_id", "performed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_adjustments");
        }
    }
}
