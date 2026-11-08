using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryPolicies : Migration
    {
        private static readonly string[] ItemShopColumns = ["item_id", "shop_id"];
        private static readonly string[] IdShopColumns = ["id", "shop_id"];
        private static readonly string[] ShopVersionColumns = ["shop_id", "catalog_version"];
        private static readonly string[] ShopItemTimeColumns = ["shop_id", "item_id", "effective_at"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "quarantined_quantity",
                table: "inventory_batches",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "shop_expiry_states",
                columns: table => new
                {
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    default_policy_version = table.Column<long>(type: "bigint", nullable: false),
                    initialized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pre_feature_baseline = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_expiry_states", x => x.shop_id);
                    table.CheckConstraint("ck_shop_expiry_states_version", "catalog_version >= 1 AND default_policy_version >= 1 AND default_policy_version <= catalog_version");
                    table.ForeignKey(
                        name: "fk_shop_expiry_states_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expiry_policy_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: true),
                    expiry_required = table.Column<bool>(type: "boolean", nullable: true),
                    sell_through_expiry_date = table.Column<bool>(type: "boolean", nullable: true),
                    warning_days = table.Column<int>(type: "integer", nullable: true),
                    override_allowed = table.Column<bool>(type: "boolean", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pre_feature_baseline = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expiry_policy_revisions", x => x.id);
                    table.CheckConstraint("ck_expiry_policy_revisions_baseline", "(NOT pre_feature_baseline AND actor_user_id IS NOT NULL) OR (pre_feature_baseline AND catalog_version = 1 AND item_id IS NULL AND mode = 0 AND expiry_required = false AND sell_through_expiry_date = true AND warning_days = 7 AND override_allowed = false AND actor_user_id IS NULL)");
                    table.CheckConstraint("ck_expiry_policy_revisions_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_expiry_policy_revisions_settings", "catalog_version >= 1 AND ((item_id IS NOT NULL AND mode IS NULL AND expiry_required IS NULL AND sell_through_expiry_date IS NULL AND warning_days IS NULL AND override_allowed IS NULL) OR (mode IS NOT NULL AND mode BETWEEN 0 AND 2 AND expiry_required IS NOT NULL AND sell_through_expiry_date IS NOT NULL AND warning_days IS NOT NULL AND warning_days BETWEEN 0 AND 365 AND override_allowed IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_expiry_policy_revisions_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: IdShopColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expiry_policy_revisions_shop_expiry_states_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shop_expiry_states",
                        principalColumn: "shop_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_batches_quarantine_range",
                table: "inventory_batches",
                sql: "quarantined_quantity >= 0 AND quarantined_quantity <= quantity");

            migrationBuilder.CreateIndex(
                name: "ix_expiry_policy_revisions_item_id_shop_id",
                table: "expiry_policy_revisions",
                columns: ItemShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_expiry_policy_revisions_shop_id_catalog_version",
                table: "expiry_policy_revisions",
                columns: ShopVersionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expiry_policy_revisions_shop_id_item_id_effective_at",
                table: "expiry_policy_revisions",
                columns: ShopItemTimeColumns);
            // Only compatible policy state is seeded. Existing quantities and date facts are untouched.
            migrationBuilder.Sql("""
                INSERT INTO shop_expiry_states (shop_id, catalog_version, default_policy_version, initialized_at, pre_feature_baseline)
                SELECT id, 1, 1, created_at, true FROM shops;
                INSERT INTO expiry_policy_revisions (id, shop_id, item_id, catalog_version, mode, expiry_required,
                    sell_through_expiry_date, warning_days, override_allowed, actor_user_id, reason, effective_at, pre_feature_baseline)
                SELECT gen_random_uuid(), shop_id, NULL, 1, 0, false, true, 7, false, NULL,
                    'Known pre-feature Ignore baseline', transaction_timestamp(), true FROM shop_expiry_states;
                ALTER TABLE shop_expiry_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shop_expiry_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY shop_expiry_states_shop_isolation ON shop_expiry_states
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE expiry_policy_revisions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE expiry_policy_revisions FORCE ROW LEVEL SECURITY;
                CREATE POLICY expiry_policy_revisions_shop_isolation ON expiry_policy_revisions
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE FUNCTION expiry_reject_revision_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Expiry policy revisions are immutable' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER expiry_policy_revisions_immutable BEFORE UPDATE OR DELETE ON expiry_policy_revisions
                    FOR EACH ROW EXECUTE FUNCTION expiry_reject_revision_mutation();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain expiry history and quarantine facts; disable policy through an audited change instead of removing schema.");
    }
}
