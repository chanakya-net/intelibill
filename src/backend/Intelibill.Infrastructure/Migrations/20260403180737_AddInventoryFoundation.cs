using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    uom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    barcode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    preferred_supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.UniqueConstraint("ak_items_id_shop_id", x => new { x.id, x.shop_id });
                    table.ForeignKey(
                        name: "fk_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_items_suppliers_preferred_supplier_id",
                        column: x => x.preferred_supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reorder_level = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    max_level = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    last_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory", x => x.id);
                    table.CheckConstraint("ck_inventory_max_non_negative", "max_level >= 0");
                    table.CheckConstraint("ck_inventory_quantity_non_negative", "quantity >= 0");
                    table.CheckConstraint("ck_inventory_reorder_lte_max", "reorder_level <= max_level");
                    table.CheckConstraint("ck_inventory_reorder_non_negative", "reorder_level >= 0");
                    table.ForeignKey(
                        name: "fk_inventory_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    min_sale_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_batches", x => x.id);
                    table.UniqueConstraint("ak_inventory_batches_id_item_id_shop_id", x => new { x.id, x.item_id, x.shop_id });
                    table.CheckConstraint("ck_inventory_batches_min_sale_lte_sales", "min_sale_price <= sales_price");
                    table.CheckConstraint("ck_inventory_batches_quantity_non_negative", "quantity >= 0");
                    table.CheckConstraint("ck_inventory_batches_sales_lte_mrp", "sales_price <= mrp");
                    table.CheckConstraint("ck_inventory_batches_tax_rate_range", "tax_rate_percent >= 0 AND tax_rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_inventory_batches_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_batches_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transactions", x => x.id);
                    table.CheckConstraint("ck_stock_transactions_quantity_non_zero", "quantity <> 0");
                    table.CheckConstraint("ck_stock_transactions_quantity_sign_by_type", "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL') AND quantity < 0) OR (transaction_type = 'ADJ'))");
                    table.ForeignKey(
                        name: "fk_stock_transactions_inventory_batches_inventory_batch_id_ite",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transactions_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transactions_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_item_id_shop_id",
                table: "inventory",
                columns: new[] { "item_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_shop_id_item_id",
                table: "inventory",
                columns: new[] { "shop_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_id_item_id_shop_id",
                table: "inventory_batches",
                columns: new[] { "id", "item_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_item_id_shop_id",
                table: "inventory_batches",
                columns: new[] { "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_expiry_date",
                table: "inventory_batches",
                columns: new[] { "shop_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_item_id_batch_number",
                table: "inventory_batches",
                columns: new[] { "shop_id", "item_id", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_id_shop_id",
                table: "items",
                columns: new[] { "id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_preferred_supplier_id",
                table: "items",
                column: "preferred_supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_shop_id_barcode",
                table: "items",
                columns: new[] { "shop_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_shop_id_is_active",
                table: "items",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_items_shop_id_name",
                table: "items",
                columns: new[] { "shop_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_inventory_batch_id_item_id_shop_id",
                table: "stock_transactions",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_item_id_shop_id",
                table: "stock_transactions",
                columns: new[] { "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_shop_id_inventory_batch_id_performed_at",
                table: "stock_transactions",
                columns: new[] { "shop_id", "inventory_batch_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_shop_id_item_id_performed_at",
                table: "stock_transactions",
                columns: new[] { "shop_id", "item_id", "performed_at" });

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE items ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE inventory ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE inventory_batches ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE stock_transactions ENABLE ROW LEVEL SECURITY;

                    CREATE POLICY items_shop_membership_policy
                        ON items
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = items.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = items.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY inventory_shop_membership_policy
                        ON inventory
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = inventory.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = inventory.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY inventory_batches_shop_membership_policy
                        ON inventory_batches
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = inventory_batches.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = inventory_batches.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY stock_transactions_shop_membership_policy
                        ON stock_transactions
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = stock_transactions.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = stock_transactions.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DROP POLICY IF EXISTS stock_transactions_shop_membership_policy ON stock_transactions;
                    DROP POLICY IF EXISTS inventory_batches_shop_membership_policy ON inventory_batches;
                    DROP POLICY IF EXISTS inventory_shop_membership_policy ON inventory;
                    DROP POLICY IF EXISTS items_shop_membership_policy ON items;

                    ALTER TABLE stock_transactions DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE inventory_batches DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE inventory DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE items DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropTable(
                name: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transactions");

            migrationBuilder.DropTable(
                name: "inventory_batches");

            migrationBuilder.DropTable(
                name: "items");
        }
    }
}

#pragma warning restore CA1861
