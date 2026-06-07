using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_order_receipt_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_receipt_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipts_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipts_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_receipt_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_purchase_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax_included = table.Column<bool>(type: "boolean", nullable: false),
                    purchase_tax_included = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_receipt_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipt_lines_inventory_batches_inventory_ba",
                        column: x => x.inventory_batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipt_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipt_lines_purchase_order_lines_purchase_",
                        column: x => x.purchase_order_line_id,
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipt_lines_purchase_order_receipts_purcha",
                        column: x => x.purchase_order_receipt_id,
                        principalTable: "purchase_order_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_order_receipt_lines_stock_transactions_stock_trans",
                        column: x => x.stock_transaction_id,
                        principalTable: "stock_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_lines_inventory_batch_id",
                table: "purchase_order_receipt_lines",
                column: "inventory_batch_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_lines_item_id",
                table: "purchase_order_receipt_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_lines_purchase_order_line_id",
                table: "purchase_order_receipt_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_lines_purchase_order_receipt_id",
                table: "purchase_order_receipt_lines",
                column: "purchase_order_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_lines_stock_transaction_id",
                table: "purchase_order_receipt_lines",
                column: "stock_transaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_sequences_shop_id_year",
                table: "purchase_order_receipt_sequences",
                columns: new[] { "shop_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipts_purchase_order_id",
                table: "purchase_order_receipts",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipts_shop_id",
                table: "purchase_order_receipts",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipts_shop_id_receipt_number",
                table: "purchase_order_receipts",
                columns: new[] { "shop_id", "receipt_number" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE purchase_order_receipts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_order_receipts FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_order_receipts_shop_isolation ON purchase_order_receipts
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);

                ALTER TABLE purchase_order_receipt_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_order_receipt_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_order_receipt_lines_shop_isolation ON purchase_order_receipt_lines
                    USING (purchase_order_receipt_id IN (
                        SELECT id FROM purchase_order_receipts
                        WHERE shop_id = current_setting('app.active_shop_id', true)::uuid
                    ));

                ALTER TABLE purchase_order_receipt_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_order_receipt_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_order_receipt_sequences_shop_isolation ON purchase_order_receipt_sequences
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS purchase_order_receipts_shop_isolation ON purchase_order_receipts;
                DROP POLICY IF EXISTS purchase_order_receipt_lines_shop_isolation ON purchase_order_receipt_lines;
                DROP POLICY IF EXISTS purchase_order_receipt_sequences_shop_isolation ON purchase_order_receipt_sequences;
                """);

            migrationBuilder.DropTable(
                name: "purchase_order_receipt_lines");

            migrationBuilder.DropTable(
                name: "purchase_order_receipt_sequences");

            migrationBuilder.DropTable(
                name: "purchase_order_receipts");
        }
    }
}

#pragma warning restore CA1861
