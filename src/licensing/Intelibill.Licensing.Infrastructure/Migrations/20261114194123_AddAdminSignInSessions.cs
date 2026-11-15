using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSignInSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                table: "admin_identities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "admin_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mfa_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_sessions", x => x.id);
                    table.CheckConstraint("ck_admin_session_dates", "mfa_verified_at >= created_at AND last_seen_at >= created_at AND (revoked_at IS NULL OR revoked_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_admin_sessions_admin_identities_admin_id",
                        column: x => x.admin_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_sign_in_buckets",
                columns: table => new
                {
                    subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    window_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_sign_in_buckets", x => x.subject);
                    table.CheckConstraint("ck_admin_sign_in_attempts", "attempts > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 15");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_deactivation_date",
                table: "admin_identities",
                sql: "deactivated_at >= verified_at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_sessions_admin_id",
                table: "admin_sessions",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_sign_in_buckets_window_ends_at",
                table: "admin_sign_in_buckets",
                column: "window_ends_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.admin_sessions)
                        OR EXISTS (SELECT FROM public.admin_identities WHERE deactivated_at IS NOT NULL)
                        OR EXISTS (SELECT FROM public.provider_audit_events WHERE action IN (14, 15)) THEN
                        RAISE EXCEPTION 'Admin session evidence exists; this migration cannot be reverted' USING ERRCODE = '55000';
                    END IF;
                END;
                $$;
                """);
            migrationBuilder.DropTable(
                name: "admin_sessions");

            migrationBuilder.DropTable(
                name: "admin_sign_in_buckets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_deactivation_date",
                table: "admin_identities");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                table: "admin_identities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 13");
        }
    }
}
