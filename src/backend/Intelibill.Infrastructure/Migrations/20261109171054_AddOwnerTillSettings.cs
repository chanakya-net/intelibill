using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerTillSettings : Migration
    {
        private static readonly string[] MembershipColumns = ["shop_id", "user_id"];
        private static readonly string[] ActorColumns = ["shop_id", "actor_user_id"];
        private static readonly string[] VersionColumns = ["shop_id", "version"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_till_setting_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_till_setting_events", x => x.id);
                    table.CheckConstraint("ck_shop_till_setting_events_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_shop_till_setting_events_version", "version > 0 AND previous_enabled <> enabled");
                    table.ForeignKey(
                        name: "fk_shop_till_setting_events_shop_memberships_shop_id_actor_use",
                        columns: x => new { x.shop_id, x.actor_user_id },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_till_setting_events_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shop_till_settings",
                columns: table => new
                {
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_till_settings", x => x.shop_id);
                    table.CheckConstraint("ck_shop_till_settings_version", "version >= 0");
                    table.ForeignKey(
                        name: "fk_shop_till_settings_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_till_setting_events_shop_id_actor_user_id",
                table: "shop_till_setting_events",
                columns: ActorColumns);

            migrationBuilder.CreateIndex(
                name: "ix_shop_till_setting_events_shop_id_version",
                table: "shop_till_setting_events",
                columns: VersionColumns,
                unique: true);

            // Preserve shops that already configured tills or posted till evidence. New and
            // otherwise unconfigured shops remain explicitly off (absence is version zero).
            migrationBuilder.Sql("""
                INSERT INTO shop_till_settings (shop_id, enabled, version)
                SELECT shop_id, true, 0 FROM (
                    SELECT shop_id FROM tills
                    UNION SELECT shop_id FROM till_sessions
                    UNION SELECT shop_id FROM till_late_postings
                ) existing;
                ALTER TABLE shop_till_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shop_till_settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY shop_till_settings_shop_isolation ON shop_till_settings
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE shop_till_setting_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shop_till_setting_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY shop_till_setting_events_shop_isolation ON shop_till_setting_events
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE TRIGGER shop_till_setting_events_immutable BEFORE UPDATE OR DELETE ON shop_till_setting_events
                    FOR EACH ROW EXECUTE FUNCTION till_reject_fact_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain owner till settings and audit facts; disable new postings instead of rolling schema back.");
    }
}
