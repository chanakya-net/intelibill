using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <summary>
    /// Additive: the session table and the link from a refresh credential to the device that
    /// earned it. No row-level security policy, matching users and refresh_tokens — a session is
    /// account-scoped and has to be readable while a request is still being authenticated.
    /// </summary>
    /// <remarks>
    /// auth_session_id is nullable so existing refresh rows survive the deploy. They are refused on
    /// rotation rather than adopted by an invented session, which is what makes the cutover a
    /// fresh sign-in rather than a silent session hand-over.
    /// </remarks>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031210000_AddAuthSessions")]
    public partial class AddAuthSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_version = table.Column<long>(type: "bigint", nullable: false),
                    recovery_epoch = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    active_shop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope_version = table.Column<long>(type: "bigint", nullable: false),
                    device_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_from_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_user_id_revoked_at",
                table: "auth_sessions",
                columns: ["user_id", "revoked_at"]);

            migrationBuilder.AddColumn<Guid>(
                name: "auth_session_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_auth_session_id",
                table: "refresh_tokens",
                column: "auth_session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_auth_sessions_auth_session_id",
                table: "refresh_tokens",
                column: "auth_session_id",
                principalTable: "auth_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_auth_sessions_auth_session_id",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_auth_session_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "auth_session_id",
                table: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "auth_sessions");
        }
    }
}
