using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discount_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    threshold_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    below_cost_confirmed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    below_cost_confirmation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    replaces_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaced_by_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_rules", x => x.id);
                    table.CheckConstraint("ck_discount_rules_disabled_audit", "(is_active = true AND disabled_at IS NULL) OR (is_active = false AND disabled_at IS NOT NULL)");
                    table.CheckConstraint("ck_discount_rules_ends_after_starts", "starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at");
                    table.CheckConstraint("ck_discount_rules_percentage_range", "percentage > 0 AND percentage <= 100");
                    table.CheckConstraint("ck_discount_rules_threshold_amount_positive", "threshold_amount IS NULL OR threshold_amount > 0");
                    table.CheckConstraint("ck_discount_rules_threshold_required", "(rule_type != 'SaleThresholdPercentage') OR (threshold_amount IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_discount_rules_inventory_batches_inventory_batch_id",
                        column: x => x.inventory_batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discount_rules_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discount_rules_inventory_batch_id",
                table: "discount_rules",
                column: "inventory_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_discount_rules_shop_id_inventory_batch_id",
                table: "discount_rules",
                columns: new[] { "shop_id", "inventory_batch_id" },
                filter: "inventory_batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_discount_rules_shop_id_is_active",
                table: "discount_rules",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_discount_rules_shop_id_rule_type",
                table: "discount_rules",
                columns: new[] { "shop_id", "rule_type" });

            migrationBuilder.Sql("""
                ALTER TABLE discount_rules ENABLE ROW LEVEL SECURITY;
                ALTER TABLE discount_rules FORCE ROW LEVEL SECURITY;
                CREATE POLICY discount_rules_shop_isolation ON discount_rules
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS discount_rules_shop_isolation ON discount_rules;");

            migrationBuilder.DropTable(
                name: "discount_rules");
        }
    }
}
