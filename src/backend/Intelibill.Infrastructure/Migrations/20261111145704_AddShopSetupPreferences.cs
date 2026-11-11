using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopSetupPreferences : Migration
    {
        private static readonly string[] MembershipColumns = ["shop_id", "user_id"];
        private static readonly string[] ActorColumns = ["shop_id", "sample_receipt_acknowledged_by_user_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_setup_preferences",
                columns: table => new
                {
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    show_on_startup = table.Column<bool>(type: "boolean", nullable: false),
                    sample_receipt_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sample_receipt_acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_setup_preferences", x => x.shop_id);
                    table.CheckConstraint("ck_shop_setup_preferences_acknowledgement", "(sample_receipt_acknowledged_at IS NULL) = (sample_receipt_acknowledged_by_user_id IS NULL)");
                    table.CheckConstraint("ck_shop_setup_preferences_version", "version >= 0");
                    table.ForeignKey(
                        name: "fk_shop_setup_preferences_shop_memberships_shop_id_sample_rece",
                        columns: x => new { x.shop_id, x.sample_receipt_acknowledged_by_user_id },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_setup_preferences_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_setup_preferences_shop_id_sample_receipt_acknowledged_",
                table: "shop_setup_preferences",
                columns: ActorColumns);

            migrationBuilder.Sql("""
                ALTER TABLE shop_setup_preferences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shop_setup_preferences FORCE ROW LEVEL SECURITY;
                CREATE POLICY shop_setup_preferences_shop_isolation ON shop_setup_preferences
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_setup_preferences");
        }
    }
}
