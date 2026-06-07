using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    order_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expected_delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier_reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_orders_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expected_quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_sequences",
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
                    table.PrimaryKey("pk_purchase_order_sequences", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_sequences_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_shop_id",
                table: "purchase_orders",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_shop_id_purchase_order_number",
                table: "purchase_orders",
                columns: new[] { "shop_id", "purchase_order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_supplier_id",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_item_id",
                table: "purchase_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id",
                table: "purchase_order_lines",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_sequences_shop_id_year",
                table: "purchase_order_sequences",
                columns: new[] { "shop_id", "year" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE purchase_orders ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_orders FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_orders_shop_isolation ON purchase_orders
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);

                ALTER TABLE purchase_order_lines ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_order_lines FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_order_lines_shop_isolation ON purchase_order_lines
                    USING (purchase_order_id IN (
                        SELECT id FROM purchase_orders
                        WHERE shop_id = current_setting('app.active_shop_id', true)::uuid
                    ));

                ALTER TABLE purchase_order_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE purchase_order_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY purchase_order_sequences_shop_isolation ON purchase_order_sequences
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS purchase_orders_shop_isolation ON purchase_orders;
                DROP POLICY IF EXISTS purchase_order_lines_shop_isolation ON purchase_order_lines;
                DROP POLICY IF EXISTS purchase_order_sequences_shop_isolation ON purchase_order_sequences;
                """);

            migrationBuilder.DropTable(name: "purchase_order_lines");
            migrationBuilder.DropTable(name: "purchase_order_sequences");
            migrationBuilder.DropTable(name: "purchase_orders");
        }
    }
}

#pragma warning restore CA1861
