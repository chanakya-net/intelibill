using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated migration index arrays are used once per migration.

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedPhoneChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_phone_verified",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "phone_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identity_sms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    encrypted_content = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_sms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "phone_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    destination = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    security_version = table.Column<long>(type: "bigint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resend_allowed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phone_challenges", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identity_sms_challenge_id",
                table: "identity_sms",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_sms_completed_at_next_attempt_at",
                table: "identity_sms",
                columns: new[] { "completed_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_sms_destination_created_at",
                table: "identity_sms",
                columns: new[] { "destination", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_phone_challenges_destination_created_at",
                table: "phone_challenges",
                columns: new[] { "destination", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_phone_challenges_user_id_purpose",
                table: "phone_challenges",
                columns: new[] { "user_id", "purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_sms");

            migrationBuilder.DropTable(
                name: "phone_challenges");

            migrationBuilder.DropColumn(
                name: "is_phone_verified",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone_verified_at",
                table: "users");
        }
    }
}
