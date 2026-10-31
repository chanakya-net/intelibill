using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // Generated migration index arrays are used once per migration.

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOwnershipChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_operator_recovery",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "security_version",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "security_version",
                table: "refresh_tokens",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "email_ownership_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    security_version = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_ownership_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identity_mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
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
                    table.PrimaryKey("pk_identity_mail", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_ownership_challenges_destination_created_at",
                table: "email_ownership_challenges",
                columns: new[] { "destination", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_ownership_challenges_user_id_purpose",
                table: "email_ownership_challenges",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_mail_completed_at_next_attempt_at",
                table: "identity_mail",
                columns: new[] { "completed_at", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_mail_destination_created_at",
                table: "identity_mail",
                columns: new[] { "destination", "created_at" });
            // Historical IsEmailVerified values were inferred from arbitrary provider email.
            // Conservatively invalidate that evidence and require operator recovery for every
            // pre-cutover account: deleted historical memberships cannot prove absence of access.
            migrationBuilder.Sql("UPDATE users SET is_email_verified = false, requires_operator_recovery = true, security_version = security_version + 1;");
            migrationBuilder.Sql("UPDATE refresh_tokens SET is_revoked = true, revoked_at = CURRENT_TIMESTAMP;");
            migrationBuilder.Sql("DELETE FROM password_reset_tokens;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_users_normalized_email ON users (lower(btrim(email))) WHERE email IS NOT NULL;");
            migrationBuilder.Sql("""
                CREATE FUNCTION public.mark_identity_business_access() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog, public AS $body$
                BEGIN
                    UPDATE public.users SET requires_operator_recovery = true WHERE id = NEW.user_id;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER mark_identity_business_access AFTER INSERT ON public.shop_memberships
                FOR EACH ROW EXECUTE FUNCTION public.mark_identity_business_access();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS mark_identity_business_access ON public.shop_memberships;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.mark_identity_business_access();");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_users_normalized_email;");
            migrationBuilder.DropTable(
                name: "email_ownership_challenges");

            migrationBuilder.DropTable(
                name: "identity_mail");

            migrationBuilder.DropColumn(
                name: "requires_operator_recovery",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_version",
                table: "refresh_tokens");
        }
    }
}
