using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineExpiryEvidence : Migration
    {
        private static readonly string[] BatchKey = ["id", "item_id", "shop_id"];
        private static readonly string[] BatchFk = ["batch_id", "item_id", "shop_id"];
        private static readonly string[] SnapshotKey = ["shop_id", "snapshot_id", "batch_id"];
        private static readonly string[] BatchTime = ["shop_id", "batch_id", "captured_at"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offline_expiry_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    effective_policy = table.Column<string>(type: "jsonb", nullable: false),
                    batch_facts = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_expiry_evidence", x => x.id);
                    table.CheckConstraint("ck_offline_expiry_evidence_version", "catalog_version > 0");
                    table.CheckConstraint("ck_offline_expiry_evidence_window", "expires_at = captured_at + interval '7 days'");
                    table.ForeignKey(
                        name: "fk_offline_expiry_evidence_inventory_batches_batch_id_item_id_",
                        columns: x => new { x.batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: BatchKey,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offline_expiry_evidence_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offline_expiry_evidence_batch_id_item_id_shop_id",
                table: "offline_expiry_evidence",
                columns: BatchFk);

            migrationBuilder.CreateIndex(
                name: "ix_offline_expiry_evidence_shop_id_batch_id_captured_at",
                table: "offline_expiry_evidence",
                columns: BatchTime);

            migrationBuilder.CreateIndex(
                name: "ix_offline_expiry_evidence_shop_id_snapshot_id_batch_id",
                table: "offline_expiry_evidence",
                columns: SnapshotKey,
                unique: true);
            migrationBuilder.Sql("""
                ALTER TABLE offline_expiry_evidence ENABLE ROW LEVEL SECURITY;
                ALTER TABLE offline_expiry_evidence FORCE ROW LEVEL SECURITY;
                CREATE POLICY offline_expiry_evidence_shop_isolation ON offline_expiry_evidence
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE TRIGGER offline_expiry_evidence_immutable BEFORE UPDATE OR DELETE ON offline_expiry_evidence
                    FOR EACH ROW EXECUTE FUNCTION expiry_reject_revision_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Offline expiry authority must be retained; use a compatible forward migration.");
    }
}
