using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesFoundation : Migration
    {
        /// <inheritdoc />
#pragma warning disable CA1861
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_price_including_tax = table.Column<bool>(type: "boolean", nullable: false),
                    has_price_mismatch = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_items_inventory_batches_inventory_batch_id",
                        column: x => x.inventory_batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sale_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_inventory_batch_id",
                table: "sale_items",
                column: "inventory_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_item_id",
                table: "sale_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_sale_id",
                table: "sale_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id",
                table: "sale_items",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id_item_id",
                table: "sale_items",
                columns: new[] { "shop_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_customer_id",
                table: "sales",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id",
                table: "sales",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_customer_id",
                table: "sales",
                columns: new[] { "shop_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_invoice_number",
                table: "sales",
                columns: new[] { "shop_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_sold_at",
                table: "sales",
                columns: new[] { "shop_id", "sold_at" });

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE sales ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE sale_items ENABLE ROW LEVEL SECURITY;

                    CREATE POLICY sales_shop_membership_policy
                        ON sales
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = sales.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = sales.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY sale_items_shop_membership_policy
                        ON sale_items
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = sale_items.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = sale_items.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );
                    """);
            }
        }
#pragma warning restore CA1861

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DROP POLICY IF EXISTS sale_items_shop_membership_policy ON sale_items;
                    DROP POLICY IF EXISTS sales_shop_membership_policy ON sales;

                    ALTER TABLE sale_items DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE sales DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropTable(
                name: "sale_items");

            migrationBuilder.DropTable(
                name: "sales");
        }
    }
}
