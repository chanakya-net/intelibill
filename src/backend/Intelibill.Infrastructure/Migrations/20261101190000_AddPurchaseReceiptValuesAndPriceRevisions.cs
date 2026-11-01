using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptValuesAndPriceRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries");

            migrationBuilder.CreateTable(
                name: "batch_price_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    new_tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    previous_tax_included = table.Column<bool>(type: "boolean", nullable: false),
                    new_tax_included = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_price_revisions", x => x.id);
                    table.CheckConstraint("ck_batch_price_revisions_sales_lte_mrp", "new_sales_price <= new_mrp");
                    table.CheckConstraint("ck_batch_price_revisions_tax_rate_range", "new_tax_rate_percent >= 0 AND new_tax_rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_batch_price_revisions_inventory_batches_inventory_batch_id_",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_price_revisions_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_receipt_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    original_total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    effective_total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    purchase_order_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_receipt_values", x => x.id);
                    table.CheckConstraint("ck_purchase_receipt_values_amounts_by_status", "(status = 'RESOLVED' AND original_total_cost IS NOT NULL AND original_total_cost >= 0 AND effective_total_cost IS NOT NULL AND effective_total_cost >= 0) OR (status = 'LEGACY_UNRESOLVED' AND original_total_cost IS NULL AND effective_total_cost IS NULL)");
                    table.CheckConstraint("ck_purchase_receipt_values_quantity_positive", "received_quantity > 0");
                    table.ForeignKey(
                        name: "fk_purchase_receipt_values_inventory_batches_inventory_batch_i",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_values_purchase_order_receipt_lines_purcha",
                        column: x => x.purchase_order_receipt_line_id,
                        principalTable: "purchase_order_receipt_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_values_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_values_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "purchase_receipt_cost_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_receipt_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    revised_total_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    delta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    supplier_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_receipt_cost_adjustments", x => x.id);
                    table.CheckConstraint("ck_purchase_receipt_cost_adjustments_delta", "delta = revised_total_cost - prior_total_cost AND delta <> 0");
                    table.CheckConstraint("ck_purchase_receipt_cost_adjustments_reason_required", "length(btrim(reason)) > 0");
                    table.CheckConstraint("ck_purchase_receipt_cost_adjustments_totals_non_negative", "prior_total_cost >= 0 AND revised_total_cost >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_receipt_cost_adjustments_inventory_batches_invento",
                        column: x => x.inventory_batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_cost_adjustments_purchase_receipt_values_p",
                        column: x => x.purchase_receipt_value_id,
                        principalTable: "purchase_receipt_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_cost_adjustments_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_cost_adjustments_supplier_ledger_entries_s",
                        column: x => x.supplier_ledger_entry_id,
                        principalTable: "supplier_ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries",
                sql: "((entry_type IN ('GOODS_RECEIVED', 'COST_ADJUSTED') AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL) OR (entry_type = 'REVERSAL'))");

            migrationBuilder.CreateIndex(
                name: "ix_batch_price_revisions_inventory_batch_id_item_id_shop_id",
                table: "batch_price_revisions",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_price_revisions_shop_id_inventory_batch_id_performed_",
                table: "batch_price_revisions",
                columns: new[] { "shop_id", "inventory_batch_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_cost_adjustments_inventory_batch_id",
                table: "purchase_receipt_cost_adjustments",
                column: "inventory_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_cost_adjustments_purchase_receipt_value_id",
                table: "purchase_receipt_cost_adjustments",
                column: "purchase_receipt_value_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_cost_adjustments_shop_id_purchase_receipt_",
                table: "purchase_receipt_cost_adjustments",
                columns: new[] { "shop_id", "purchase_receipt_value_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_cost_adjustments_supplier_ledger_entry_id",
                table: "purchase_receipt_cost_adjustments",
                column: "supplier_ledger_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_values_inventory_batch_id_item_id_shop_id",
                table: "purchase_receipt_values",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_values_purchase_order_receipt_line_id",
                table: "purchase_receipt_values",
                column: "purchase_order_receipt_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_values_shop_id_inventory_batch_id",
                table: "purchase_receipt_values",
                columns: new[] { "shop_id", "inventory_batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_values_shop_id_supplier_id",
                table: "purchase_receipt_values",
                columns: new[] { "shop_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_values_supplier_id",
                table: "purchase_receipt_values",
                column: "supplier_id");

            // Tenant data: the leading shop_id indexes serve the policy predicate. Same predicate as
            // CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE purchase_receipt_values ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_receipt_values FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_receipt_values_shop_isolation ON purchase_receipt_values
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);

                ALTER TABLE purchase_receipt_cost_adjustments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_receipt_cost_adjustments FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_receipt_cost_adjustments_shop_isolation ON purchase_receipt_cost_adjustments
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);

                ALTER TABLE batch_price_revisions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE batch_price_revisions FORCE ROW LEVEL SECURITY;
                CREATE POLICY batch_price_revisions_shop_isolation ON batch_price_revisions
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);

            // Receipt values for active batches received before this migration. A receipt is resolved
            // only when its evidence is unambiguous: an opening IN movement of the full received
            // quantity that is not a legacy "Batch Correction", at most one goods-received payable, and
            // that payable agreeing with the purchase order line when there is one. Its total comes from
            // the PO line, else the posted payable, else zero for a free receipt. Anything else is
            // LEGACY_UNRESOLVED with no amount: a total is never inferred from remaining stock.
            // Idempotent: scripts/db/backfill-purchase-receipt-values.sql holds the same statement, to
            // re-run after the new API version is live for batches an old instance created meanwhile.
            migrationBuilder.Sql("""
                INSERT INTO purchase_receipt_values (
                    id, shop_id, inventory_batch_id, item_id, supplier_id, received_quantity,
                    original_total_cost, effective_total_cost, source, status, source_reference,
                    purchase_order_receipt_line_id, received_at, created_by, created_at)
                SELECT
                    gen_random_uuid(), b.shop_id, b.id, b.item_id,
                    COALESCE(gr.supplier_id, b.supplier_id),
                    b.original_quantity,
                    CASE WHEN ev.resolved THEN ev.total END,
                    CASE WHEN ev.resolved THEN ev.total END,
                    CASE WHEN pol.id IS NOT NULL THEN 'PURCHASE_ORDER' ELSE 'LEGACY_BACKFILL' END,
                    CASE WHEN ev.resolved THEN 'RESOLVED' ELSE 'LEGACY_UNRESOLVED' END,
                    left(opening.reference_number, 80),
                    pol.id,
                    COALESCE(opening.performed_at, b.created_at),
                    b.created_by,
                    now()
                FROM inventory_batches b
                CROSS JOIN LATERAL (
                    SELECT count(*) AS entries, min(e.amount) AS amount, min(e.supplier_id::text)::uuid AS supplier_id
                    FROM supplier_ledger_entries e
                    WHERE e.batch_id = b.id AND e.entry_type = 'GOODS_RECEIVED') gr
                LEFT JOIN purchase_order_receipt_lines pol ON pol.inventory_batch_id = b.id
                LEFT JOIN LATERAL (
                    SELECT t.performed_at, t.reference_number
                    FROM stock_transactions t
                    WHERE t.inventory_batch_id = b.id
                      AND t.transaction_type = 'IN'
                      AND t.quantity = b.original_quantity
                      AND t.reference_number IS DISTINCT FROM 'Batch Correction'
                    ORDER BY t.performed_at
                    LIMIT 1) opening ON true
                CROSS JOIN LATERAL (
                    SELECT CASE
                        WHEN pol.id IS NOT NULL THEN round(pol.total_purchase_cost, 2)
                        WHEN gr.entries = 1 THEN gr.amount
                        WHEN gr.entries = 0 AND b.cost_price = 0 THEN 0
                    END AS total) receipt_total
                CROSS JOIN LATERAL (
                    SELECT
                        COALESCE(opening.performed_at IS NOT NULL
                            AND gr.entries <= 1
                            AND receipt_total.total >= 0
                            AND COALESCE(gr.amount, 0) = receipt_total.total, false) AS resolved,
                        receipt_total.total) ev
                WHERE b.is_voided = false
                  AND b.original_quantity > 0
                  AND NOT EXISTS (SELECT 1 FROM purchase_receipt_values r WHERE r.inventory_batch_id = b.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Receipt values, cost corrections and price revisions are retained audit records: rolling
            // this back discards them, and restoring the old ledger constraint fails while
            // COST_ADJUSTED entries exist. Disable correction writes rather than roll back.
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS batch_price_revisions_shop_isolation ON batch_price_revisions;
                DROP POLICY IF EXISTS purchase_receipt_cost_adjustments_shop_isolation ON purchase_receipt_cost_adjustments;
                DROP POLICY IF EXISTS purchase_receipt_values_shop_isolation ON purchase_receipt_values;
                """);

            migrationBuilder.DropTable(
                name: "batch_price_revisions");

            migrationBuilder.DropTable(
                name: "purchase_receipt_cost_adjustments");

            migrationBuilder.DropTable(
                name: "purchase_receipt_values");

            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries",
                sql: "((entry_type = 'GOODS_RECEIVED' AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL) OR (entry_type = 'REVERSAL'))");
        }
    }
}
