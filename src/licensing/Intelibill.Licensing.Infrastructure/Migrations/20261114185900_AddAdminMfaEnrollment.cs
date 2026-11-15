using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminMfaEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("ck_bootstrap_audit_action", "provider_audit_events");
            migrationBuilder.AddCheckConstraint("ck_bootstrap_audit_action", "provider_audit_events", "action BETWEEN 1 AND 13");

            migrationBuilder.CreateTable(
                name: "admin_totp",
                columns: table => new
                {
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    encrypted_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    last_accepted_step = table.Column<long>(type: "bigint", nullable: true),
                    attempt_window_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_totp", x => x.admin_id);
                    table.CheckConstraint("ck_admin_totp_attempts", "(attempts = 0 AND attempt_window_ends_at IS NULL)\nOR (attempts BETWEEN 1 AND 5 AND attempt_window_ends_at IS NOT NULL)");
                    table.CheckConstraint("ck_admin_totp_secret", "octet_length(encrypted_secret) = 48 AND last_accepted_step >= 0");
                    table.ForeignKey(
                        name: "fk_admin_totp_admin_identities_admin_id",
                        column: x => x.admin_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_recovery_codes",
                columns: table => new
                {
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_recovery_codes", x => new { x.admin_id, x.digest });
                    table.CheckConstraint("ck_admin_recovery_digest", "digest ~ '^[0-9A-F]{64}$'");
                    table.ForeignKey(
                        name: "fk_admin_recovery_codes_admin_totp_admin_id",
                        column: x => x.admin_id,
                        principalTable: "admin_totp",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.admin_totp)
                        OR EXISTS (SELECT FROM public.provider_audit_events WHERE action IN (12, 13)) THEN
                        RAISE EXCEPTION 'Admin MFA evidence exists; this migration cannot be reverted' USING ERRCODE = '55000';
                    END IF;
                END;
                $$;
                """);
            migrationBuilder.DropCheckConstraint("ck_bootstrap_audit_action", "provider_audit_events");
            migrationBuilder.AddCheckConstraint("ck_bootstrap_audit_action", "provider_audit_events", "action BETWEEN 1 AND 11");

            migrationBuilder.DropTable(
                name: "admin_recovery_codes");

            migrationBuilder.DropTable(
                name: "admin_totp");
        }
    }
}
