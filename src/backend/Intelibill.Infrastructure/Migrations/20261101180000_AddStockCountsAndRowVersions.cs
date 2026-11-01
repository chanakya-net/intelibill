using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountsAndRowVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_adjustments_cost_impact_positive",
                table: "inventory_adjustments");

            // inventory_batches.xmin becomes the batch row version. It is a PostgreSQL system column,
            // so no DDL is required.

            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_batch_version = table.Column<long>(type: "bigint", nullable: false),
                    observed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    delta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_counts", x => x.id);
                    table.CheckConstraint("ck_stock_counts_delta", "delta = counted_quantity - observed_quantity");
                    table.CheckConstraint("ck_stock_counts_movement_links", "(delta = 0 AND inventory_adjustment_id IS NULL AND stock_transaction_id IS NULL) OR (delta <> 0 AND inventory_adjustment_id IS NOT NULL AND stock_transaction_id IS NOT NULL)");
                    table.CheckConstraint("ck_stock_counts_quantities_non_negative", "observed_quantity >= 0 AND counted_quantity >= 0");
                    table.CheckConstraint("ck_stock_counts_reason_required", "length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "fk_stock_counts_inventory_adjustments_inventory_adjustment_id",
                        column: x => x.inventory_adjustment_id,
                        principalTable: "inventory_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_counts_inventory_batches_inventory_batch_id_item_id_s",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_counts_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_counts_stock_transactions_stock_transaction_id",
                        column: x => x.stock_transaction_id,
                        principalTable: "stock_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The domain has always posted a receipt void's payable reversal as a REVERSAL entry, which
            // the old constraint refused, so voiding any priced receipt failed.
            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries",
                sql: "((entry_type = 'GOODS_RECEIVED' AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL) OR (entry_type = 'REVERSAL'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_adjustments_cost_impact_non_negative",
                table: "inventory_adjustments",
                sql: "cost_impact >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_inventory_adjustment_id",
                table: "stock_counts",
                column: "inventory_adjustment_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_inventory_batch_id_item_id_shop_id",
                table: "stock_counts",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_shop_id_inventory_batch_id_performed_at",
                table: "stock_counts",
                columns: new[] { "shop_id", "inventory_batch_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_shop_id_item_id_performed_at",
                table: "stock_counts",
                columns: new[] { "shop_id", "item_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_stock_transaction_id",
                table: "stock_counts",
                column: "stock_transaction_id");

            // Tenant data: the leading shop_id indexes serve the policy predicate. Same predicate as
            // CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE stock_counts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE stock_counts FORCE ROW LEVEL SECURITY;
                CREATE POLICY stock_counts_shop_isolation ON stock_counts
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Counts are retained audit records; rolling this back discards them. Restoring the old
            // constraints fails while voided receipts' REVERSAL entries or zero-cost adjustments exist.
            migrationBuilder.Sql("DROP POLICY IF EXISTS stock_counts_shop_isolation ON stock_counts;");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_adjustments_cost_impact_non_negative",
                table: "inventory_adjustments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries",
                sql: "((entry_type = 'GOODS_RECEIVED' AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_adjustments_cost_impact_positive",
                table: "inventory_adjustments",
                sql: "cost_impact > 0");
        }
    }
}
