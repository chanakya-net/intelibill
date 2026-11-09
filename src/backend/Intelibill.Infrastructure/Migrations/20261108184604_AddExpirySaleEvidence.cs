using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpirySaleEvidence : Migration
    {
        private static readonly string[] BatchKeyColumns = ["id", "item_id", "shop_id"];
        private static readonly string[] BatchForeignKeyColumns = ["inventory_batch_id", "item_id", "shop_id"];
        private static readonly string[] ShopCatalogColumns = ["shop_id", "catalog_version"];
        private static readonly string[] ShopBatchTimeColumns = ["shop_id", "inventory_batch_id", "effective_at"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "expiry_decisions",
                table: "sales",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "batch_expiry_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    new_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    previous_manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    new_manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    previous_batch_version = table.Column<long>(type: "bigint", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_expiry_revisions", x => x.id);
                    table.CheckConstraint("ck_batch_expiry_revisions_dates", "new_expiry_date IS NULL OR new_manufacturing_date IS NULL OR new_manufacturing_date <= new_expiry_date");
                    table.CheckConstraint("ck_batch_expiry_revisions_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_batch_expiry_revisions_version", "catalog_version > 1 AND previous_batch_version > 0");
                    table.ForeignKey(
                        name: "fk_batch_expiry_revisions_inventory_batches_inventory_batch_id",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: BatchKeyColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_expiry_revisions_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batch_expiry_revisions_inventory_batch_id_item_id_shop_id",
                table: "batch_expiry_revisions",
                columns: BatchForeignKeyColumns);

            migrationBuilder.CreateIndex(
                name: "ix_batch_expiry_revisions_shop_id_catalog_version",
                table: "batch_expiry_revisions",
                columns: ShopCatalogColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batch_expiry_revisions_shop_id_inventory_batch_id_effective",
                table: "batch_expiry_revisions",
                columns: ShopBatchTimeColumns);
            migrationBuilder.Sql("""
                ALTER TABLE batch_expiry_revisions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE batch_expiry_revisions FORCE ROW LEVEL SECURITY;
                CREATE POLICY batch_expiry_revisions_shop_isolation ON batch_expiry_revisions
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE TRIGGER batch_expiry_revisions_immutable BEFORE UPDATE OR DELETE ON batch_expiry_revisions
                    FOR EACH ROW EXECUTE FUNCTION expiry_reject_revision_mutation();
                CREATE FUNCTION expiry_reject_sale_evidence_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.expiry_decisions IS NOT NULL THEN
                            RAISE EXCEPTION 'Sale expiry evidence is immutable' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF OLD.expiry_decisions IS NOT NULL AND NEW.expiry_decisions IS DISTINCT FROM OLD.expiry_decisions THEN
                        RAISE EXCEPTION 'Sale expiry evidence is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER sales_expiry_evidence_immutable BEFORE UPDATE OR DELETE ON sales
                    FOR EACH ROW EXECUTE FUNCTION expiry_reject_sale_evidence_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Expiry sale and date evidence must be retained; use a compatible forward migration.");
    }
}
