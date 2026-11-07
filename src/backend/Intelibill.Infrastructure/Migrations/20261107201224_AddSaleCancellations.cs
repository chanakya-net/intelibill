#pragma warning disable CA1861 // EF-generated migration array arguments.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries");

            migrationBuilder.AlterColumn<string>(
                name: "transaction_type",
                table: "stock_transactions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "sales",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_transactions_id_item_id_inventory_batch_id_shop_id",
                table: "stock_transactions",
                columns: new[] { "id", "item_id", "inventory_batch_id", "shop_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sales_id_shop_id",
                table: "sales",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_sale_items_id_sale_id_shop_id",
                table: "sale_items",
                columns: new[] { "id", "sale_id", "shop_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_customer_ledger_entries_id_shop_id",
                table: "customer_ledger_entries",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.CreateTable(
                name: "sale_cancellations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    business_time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    original_invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    original_business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    revenue_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_reduction_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cash_refund_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    customer_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seller = table.Column<string>(type: "jsonb", nullable: false),
                    buyer = table.Column<string>(type: "jsonb", nullable: false),
                    goods_recovered_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    cash_refund_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_cancellations", x => x.id);
                    table.UniqueConstraint("ak_sale_cancellations_id_sale_id_shop_id", x => new { x.id, x.sale_id, x.shop_id });
                    table.CheckConstraint("ck_sale_cancellations_amounts", "total_amount >= 0 AND revenue_amount >= 0 AND tax_amount = 0 AND due_reduction_amount >= 0 AND cash_refund_amount >= 0 AND total_amount = due_reduction_amount + cash_refund_amount AND revenue_amount = total_amount");
                    table.CheckConstraint("ck_sale_cancellations_reason", "length(btrim(reason)) BETWEEN 3 AND 255 AND length(btrim(operation_key)) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_sale_cancellations_settlement", "((due_reduction_amount > 0 AND customer_ledger_entry_id IS NOT NULL) OR (due_reduction_amount = 0 AND customer_ledger_entry_id IS NULL)) AND cash_refund_confirmed = (cash_refund_amount > 0) AND goods_recovered_confirmed");
                    table.ForeignKey(
                        name: "fk_sale_cancellations_customer_ledger_entries_customer_ledger_",
                        columns: x => new { x.customer_ledger_entry_id, x.shop_id },
                        principalTable: "customer_ledger_entries",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellations_sales_sale_id_shop_id",
                        columns: x => new { x.sale_id, x.shop_id },
                        principalTable: "sales",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellations_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_cancellation_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_cancellation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    uqc = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    original_cost_price = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    revenue_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    recorded_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_cancellation_lines", x => x.id);
                    table.CheckConstraint("ck_sale_cancellation_lines_amounts", "quantity > 0 AND original_cost_price >= 0 AND revenue_amount >= 0 AND recorded_cost_amount >= 0");
                    table.ForeignKey(
                        name: "fk_sale_cancellation_lines_inventory_batches_inventory_batch_i",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellation_lines_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellation_lines_sale_cancellations_sale_cancellatio",
                        columns: x => new { x.sale_cancellation_id, x.sale_id, x.shop_id },
                        principalTable: "sale_cancellations",
                        principalColumns: new[] { "id", "sale_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellation_lines_sale_items_sale_item_id_sale_id_sho",
                        columns: x => new { x.sale_item_id, x.sale_id, x.shop_id },
                        principalTable: "sale_items",
                        principalColumns: new[] { "id", "sale_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellation_lines_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_cancellation_lines_stock_transactions_stock_transactio",
                        columns: x => new { x.stock_transaction_id, x.item_id, x.inventory_batch_id, x.shop_id },
                        principalTable: "stock_transactions",
                        principalColumns: new[] { "id", "item_id", "inventory_batch_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions",
                sql: "((transaction_type IN ('IN', 'RET', 'SALE_CANCELLATION') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL', 'SUPPLIER_RETURN') AND quantity < 0) OR (transaction_type IN ('ADJ', 'REV')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_status",
                table: "sales",
                sql: "status IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries",
                sql: "((entry_type IN ('SALE_DUE', 'SALE_CANCELLATION_CREDIT') AND sale_id IS NOT NULL) OR (entry_type IN ('PAYMENT_RECEIVED', 'PAYMENT_REVERSAL') AND sale_id IS NULL) OR entry_type IN ('RETURN_CREDIT', 'RETURN_CREDIT_REVERSAL'))");

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_inventory_batch_id_item_id_shop_id",
                table: "sale_cancellation_lines",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_item_id_shop_id",
                table: "sale_cancellation_lines",
                columns: new[] { "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_sale_cancellation_id_sale_id_shop_id",
                table: "sale_cancellation_lines",
                columns: new[] { "sale_cancellation_id", "sale_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_sale_cancellation_id_sale_item_id",
                table: "sale_cancellation_lines",
                columns: new[] { "sale_cancellation_id", "sale_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_sale_item_id_sale_id_shop_id",
                table: "sale_cancellation_lines",
                columns: new[] { "sale_item_id", "sale_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_shop_id",
                table: "sale_cancellation_lines",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_stock_transaction_id",
                table: "sale_cancellation_lines",
                column: "stock_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_stock_transaction_id_item_id_invent",
                table: "sale_cancellation_lines",
                columns: new[] { "stock_transaction_id", "item_id", "inventory_batch_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellations_customer_ledger_entry_id_shop_id",
                table: "sale_cancellations",
                columns: new[] { "customer_ledger_entry_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellations_sale_id",
                table: "sale_cancellations",
                column: "sale_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellations_sale_id_shop_id",
                table: "sale_cancellations",
                columns: new[] { "sale_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellations_shop_id_business_date",
                table: "sale_cancellations",
                columns: new[] { "shop_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellations_shop_id_reference_number",
                table: "sale_cancellations",
                columns: new[] { "shop_id", "reference_number" },
                unique: true);
            foreach (var table in new[] { "sale_cancellations", "sale_cancellation_lines" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {table} FORCE ROW LEVEL SECURITY; CREATE POLICY {table}_shop_isolation ON {table} USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid) WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);");
            }

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sale_cancellation_lines");

            migrationBuilder.DropTable(
                name: "sale_cancellations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_transactions_id_item_id_inventory_batch_id_shop_id",
                table: "stock_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sales_id_shop_id",
                table: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_status",
                table: "sales");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_sale_items_id_sale_id_shop_id",
                table: "sale_items");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_customer_ledger_entries_id_shop_id",
                table: "customer_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries");

            migrationBuilder.DropColumn(
                name: "status",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "sales");

            migrationBuilder.AlterColumn<string>(
                name: "transaction_type",
                table: "stock_transactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions",
                sql: "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL', 'SUPPLIER_RETURN') AND quantity < 0) OR (transaction_type IN ('ADJ', 'REV')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries",
                sql: "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type IN ('PAYMENT_RECEIVED', 'PAYMENT_REVERSAL') AND sale_id IS NULL) OR entry_type IN ('RETURN_CREDIT', 'RETURN_CREDIT_REVERSAL'))");
        }
    }
}
