using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipStatusAndInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shop_memberships_shop_id",
                table: "shop_memberships");

            migrationBuilder.AddColumn<long>(
                name: "authority_version",
                table: "shop_memberships",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "shop_memberships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active");

            // Every membership that exists today was usable, so it stays usable. A globally
            // disabled account stays disabled: this backfill deliberately does not read or write
            // users.is_login_enabled, because re-enabling an identity is an ownership decision,
            // not a schema migration. Legacy owner-created credentials are replaced through
            // verified enrollment and recovery instead.
            migrationBuilder.Sql("UPDATE shop_memberships SET status = 'Active' WHERE status IS NULL OR status = '';");

            migrationBuilder.CreateTable(
                name: "shop_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviter_authority_version = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_invitations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_memberships_shop_id_status",
                table: "shop_memberships",
                columns: new[] { "shop_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_invitations_shop_id_created_at",
                table: "shop_invitations",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_invitations_shop_id_email",
                table: "shop_invitations",
                columns: new[] { "shop_id", "email" },
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_invitations");

            migrationBuilder.DropIndex(
                name: "ix_shop_memberships_shop_id_status",
                table: "shop_memberships");

            migrationBuilder.DropColumn(
                name: "authority_version",
                table: "shop_memberships");

            migrationBuilder.DropColumn(
                name: "status",
                table: "shop_memberships");

            migrationBuilder.CreateIndex(
                name: "ix_shop_memberships_shop_id",
                table: "shop_memberships",
                column: "shop_id");
        }
    }
}
