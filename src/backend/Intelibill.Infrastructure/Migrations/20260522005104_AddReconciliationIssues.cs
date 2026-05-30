using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliation_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_sale_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    device_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    issue_type = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    printed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    available_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    consumed_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_issues", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_issues_inventory_batches_inventory_batch_id",
                        column: x => x.inventory_batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reconciliation_issues_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reconciliation_issues_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_reconciliation_issues_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_inventory_batch_id",
                table: "reconciliation_issues",
                column: "inventory_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_item_id",
                table: "reconciliation_issues",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_sale_id",
                table: "reconciliation_issues",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_shop_id_client_sale_id_device_id",
                table: "reconciliation_issues",
                columns: new[] { "shop_id", "client_sale_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_shop_id_is_resolved",
                table: "reconciliation_issues",
                columns: new[] { "shop_id", "is_resolved" });

            migrationBuilder.Sql("""
                ALTER TABLE reconciliation_issues ENABLE ROW LEVEL SECURITY;
                ALTER TABLE reconciliation_issues FORCE ROW LEVEL SECURITY;
                CREATE POLICY reconciliation_issues_shop_isolation ON reconciliation_issues
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS reconciliation_issues_shop_isolation ON reconciliation_issues;");

            migrationBuilder.DropTable(
                name: "reconciliation_issues");
        }
    }
}
#pragma warning restore CA1861
