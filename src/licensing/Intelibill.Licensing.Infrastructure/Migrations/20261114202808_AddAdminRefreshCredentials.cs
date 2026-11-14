using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminRefreshCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.CreateTable(
                name: "admin_refresh_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_refresh_credentials", x => x.id);
                    table.CheckConstraint("ck_admin_refresh_credential_dates", "expires_at > issued_at AND (consumed_at IS NULL OR consumed_at >= issued_at)");
                    table.CheckConstraint("ck_admin_refresh_credential_hash", "token_hash ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("ck_admin_refresh_credential_parent", "parent_id IS NULL OR parent_id <> id");
                    table.ForeignKey(
                        name: "fk_admin_refresh_credentials_admin_refresh_credentials_parent_",
                        column: x => x.parent_id,
                        principalTable: "admin_refresh_credentials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admin_refresh_credentials_admin_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "admin_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 16");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_credentials_parent_id",
                table: "admin_refresh_credentials",
                column: "parent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_credentials_session_id",
                table: "admin_refresh_credentials",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_credentials_session_root",
                table: "admin_refresh_credentials",
                column: "session_id",
                unique: true,
                filter: "parent_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_refresh_credentials_token_hash",
                table: "admin_refresh_credentials",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.admin_refresh_credentials)
                        OR EXISTS (SELECT FROM public.provider_audit_events WHERE action = 16) THEN
                        RAISE EXCEPTION 'Admin refresh evidence exists; this migration cannot be reverted' USING ERRCODE = '55000';
                    END IF;
                END;
                $$;
                """);
            migrationBuilder.DropTable(
                name: "admin_refresh_credentials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 15");
        }
    }
}
