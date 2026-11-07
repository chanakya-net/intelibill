#pragma warning disable CA1861 // Generated migration array arguments.
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierReturnsAndReceiptReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions");

            migrationBuilder.AlterColumn<string>(
                name: "entry_type",
                table: "supplier_ledger_entries",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<bool>(
                name: "has_supplier_returns",
                table: "purchase_receipt_values",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_suppliers_id_shop_id",
                table: "suppliers",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_supplier_ledger_entries_id_shop_id",
                table: "supplier_ledger_entries",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_stock_transactions_id_shop_id",
                table: "stock_transactions",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_purchase_receipt_values_id_shop_id",
                table: "purchase_receipt_values",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.CreateTable(
                name: "purchase_receipt_reversals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    business_time_zone_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    effective_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_receipt_reversals", x => x.id);
                    table.UniqueConstraint("ak_purchase_receipt_reversals_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_purchase_receipt_reversals_amounts", "quantity > 0 AND effective_amount >= 0 AND ((effective_amount = 0 AND supplier_ledger_entry_id IS NULL) OR (effective_amount > 0 AND supplier_ledger_entry_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_purchase_receipt_reversals_purchase_receipt_values_receipt_",
                        columns: x => new { x.receipt_value_id, x.shop_id },
                        principalTable: "purchase_receipt_values",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_reversals_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_reversals_stock_transactions_stock_transac",
                        columns: x => new { x.stock_transaction_id, x.shop_id },
                        principalTable: "stock_transactions",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_receipt_reversals_supplier_ledger_entries_supplier",
                        columns: x => new { x.supplier_ledger_entry_id, x.shop_id },
                        principalTable: "supplier_ledger_entries",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    business_time_zone_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_returns", x => x.id);
                    table.UniqueConstraint("ak_supplier_returns_id_shop_id", x => new { x.id, x.shop_id });
                    table.ForeignKey(
                        name: "fk_supplier_returns_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_returns_suppliers_supplier_id_shop_id",
                        columns: x => new { x.supplier_id, x.shop_id },
                        principalTable: "suppliers",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_return_credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    business_time_zone_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    supplier_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_return_credits", x => x.id);
                    table.UniqueConstraint("ak_supplier_return_credits_id_shop_id", x => new { x.id, x.shop_id });
                    table.ForeignKey(
                        name: "fk_supplier_return_credits_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_credits_supplier_returns_supplier_return_id",
                        columns: x => new { x.supplier_return_id, x.shop_id },
                        principalTable: "supplier_returns",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_return_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_receipt_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    proposed_credit_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    effective_total_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uqc = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_return_lines", x => x.id);
                    table.UniqueConstraint("ak_supplier_return_lines_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_supplier_return_lines_amounts", "quantity > 0 AND quantity <= received_quantity AND proposed_credit_amount >= 0 AND effective_total_cost >= 0");
                    table.ForeignKey(
                        name: "fk_supplier_return_lines_inventory_batches_inventory_batch_id_",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_lines_purchase_receipt_values_receipt_value",
                        columns: x => new { x.receipt_value_id, x.shop_id },
                        principalTable: "purchase_receipt_values",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_lines_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_lines_stock_transactions_stock_transaction_",
                        columns: x => new { x.stock_transaction_id, x.shop_id },
                        principalTable: "stock_transactions",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_lines_supplier_returns_supplier_return_id_s",
                        columns: x => new { x.supplier_return_id, x.shop_id },
                        principalTable: "supplier_returns",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_return_credit_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_return_credit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_value_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    supplier_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_return_credit_lines", x => x.id);
                    table.UniqueConstraint("ak_supplier_return_credit_lines_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_supplier_return_credit_lines_amounts", "accepted_amount >= 0 AND ((accepted_amount = 0 AND supplier_ledger_entry_id IS NULL) OR (accepted_amount > 0 AND supplier_ledger_entry_id IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_supplier_return_credit_lines_purchase_receipt_values_receip",
                        columns: x => new { x.receipt_value_id, x.shop_id },
                        principalTable: "purchase_receipt_values",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_credit_lines_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_credit_lines_supplier_ledger_entries_suppli",
                        columns: x => new { x.supplier_ledger_entry_id, x.shop_id },
                        principalTable: "supplier_ledger_entries",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_credit_lines_supplier_return_credits_suppli",
                        columns: x => new { x.supplier_return_credit_id, x.shop_id },
                        principalTable: "supplier_return_credits",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_return_credit_lines_supplier_return_lines_return_l",
                        columns: x => new { x.return_line_id, x.shop_id },
                        principalTable: "supplier_return_lines",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries",
                sql: "((entry_type IN ('GOODS_RECEIVED', 'COST_ADJUSTED', 'SUPPLIER_RETURN_CREDIT') AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL) OR (entry_type = 'REVERSAL'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions",
                sql: "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL', 'SUPPLIER_RETURN') AND quantity < 0) OR (transaction_type IN ('ADJ', 'REV')))");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_reversals_receipt_value_id",
                table: "purchase_receipt_reversals",
                column: "receipt_value_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_reversals_receipt_value_id_shop_id",
                table: "purchase_receipt_reversals",
                columns: new[] { "receipt_value_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_reversals_shop_id",
                table: "purchase_receipt_reversals",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_reversals_stock_transaction_id_shop_id",
                table: "purchase_receipt_reversals",
                columns: new[] { "stock_transaction_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_receipt_reversals_supplier_ledger_entry_id_shop_id",
                table: "purchase_receipt_reversals",
                columns: new[] { "supplier_ledger_entry_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credit_lines_receipt_value_id_shop_id",
                table: "supplier_return_credit_lines",
                columns: new[] { "receipt_value_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credit_lines_return_line_id",
                table: "supplier_return_credit_lines",
                column: "return_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credit_lines_return_line_id_shop_id",
                table: "supplier_return_credit_lines",
                columns: new[] { "return_line_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credit_lines_shop_id",
                table: "supplier_return_credit_lines",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credit_lines_supplier_ledger_entry_id_shop_",
                table: "supplier_return_credit_lines",
                columns: new[] { "supplier_ledger_entry_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credit_lines_supplier_return_credit_id_shop",
                table: "supplier_return_credit_lines",
                columns: new[] { "supplier_return_credit_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credits_shop_id",
                table: "supplier_return_credits",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credits_supplier_return_id",
                table: "supplier_return_credits",
                column: "supplier_return_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_credits_supplier_return_id_shop_id",
                table: "supplier_return_credits",
                columns: new[] { "supplier_return_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_lines_inventory_batch_id_item_id_shop_id",
                table: "supplier_return_lines",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_lines_receipt_value_id_shop_id",
                table: "supplier_return_lines",
                columns: new[] { "receipt_value_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_lines_shop_id",
                table: "supplier_return_lines",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_lines_stock_transaction_id_shop_id",
                table: "supplier_return_lines",
                columns: new[] { "stock_transaction_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_lines_supplier_return_id_receipt_value_id",
                table: "supplier_return_lines",
                columns: new[] { "supplier_return_id", "receipt_value_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_lines_supplier_return_id_shop_id",
                table: "supplier_return_lines",
                columns: new[] { "supplier_return_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_returns_shop_id_return_number",
                table: "supplier_returns",
                columns: new[] { "shop_id", "return_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_returns_shop_id_supplier_id_shipped_at",
                table: "supplier_returns",
                columns: new[] { "shop_id", "supplier_id", "shipped_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_returns_supplier_id_shop_id",
                table: "supplier_returns",
                columns: new[] { "supplier_id", "shop_id" });
            foreach (var table in new[] { "supplier_returns", "supplier_return_lines", "supplier_return_credits", "supplier_return_credit_lines", "purchase_receipt_reversals" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {table} FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON {table} USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid) WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_receipt_reversals");

            migrationBuilder.DropTable(
                name: "supplier_return_credit_lines");

            migrationBuilder.DropTable(
                name: "supplier_return_credits");

            migrationBuilder.DropTable(
                name: "supplier_return_lines");

            migrationBuilder.DropTable(
                name: "supplier_returns");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_suppliers_id_shop_id",
                table: "suppliers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_supplier_ledger_entries_id_shop_id",
                table: "supplier_ledger_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_stock_transactions_id_shop_id",
                table: "stock_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_purchase_receipt_values_id_shop_id",
                table: "purchase_receipt_values");

            migrationBuilder.DropColumn(
                name: "has_supplier_returns",
                table: "purchase_receipt_values");

            migrationBuilder.AlterColumn<string>(
                name: "entry_type",
                table: "supplier_ledger_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AddCheckConstraint(
                name: "ck_supplier_ledger_entries_batch_by_type",
                table: "supplier_ledger_entries",
                sql: "((entry_type IN ('GOODS_RECEIVED', 'COST_ADJUSTED') AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL) OR (entry_type = 'REVERSAL'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transactions_quantity_sign_by_type",
                table: "stock_transactions",
                sql: "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL') AND quantity < 0) OR (transaction_type IN ('ADJ', 'REV')))");
        }
    }
}
