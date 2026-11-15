using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                table: "admin_totp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "admin_recoveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<int>(type: "integer", nullable: true),
                    verification = table.Column<int>(type: "integer", nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    grant_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    grant_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_recoveries", x => x.id);
                    table.CheckConstraint("ck_admin_recovery_evidence", "(kind = 1 AND redeemed_at = started_at AND num_nonnulls(reviewer_id, reason, verification,\n    evidence_reference, grant_digest, grant_expires_at, retired_at) = 0)\nOR (kind = 2 AND num_nonnulls(reviewer_id, reason, verification, evidence_reference,\n        grant_digest, grant_expires_at) = 6\n    AND reviewer_id <> admin_id AND reason BETWEEN 1 AND 3 AND verification BETWEEN 1 AND 3\n    AND evidence_reference ~ '^[A-Za-z0-9][A-Za-z0-9._:#/-]{2,63}$'\n    AND grant_digest ~ '^[0-9A-F]{64}$' AND started_at < grant_expires_at\n    AND (redeemed_at IS NULL OR retired_at IS NULL)\n    AND (redeemed_at IS NULL OR redeemed_at >= started_at)\n    AND (retired_at IS NULL OR retired_at >= started_at))");
                    table.ForeignKey(
                        name: "fk_admin_recoveries_admin_identities_admin_id",
                        column: x => x.admin_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admin_recoveries_admin_identities_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 25 OR action BETWEEN 101 AND 103");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 31)");

            migrationBuilder.CreateIndex(
                name: "ix_admin_recoveries_admin_id",
                table: "admin_recoveries",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_recoveries_grant_digest",
                table: "admin_recoveries",
                column: "grant_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_recoveries_reviewer_id",
                table: "admin_recoveries",
                column: "reviewer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recovery evidence and retired authenticators are facts: never discard them by rolling back.
            migrationBuilder.Sql("""
                LOCK TABLE public.provider_audit_events, public.admin_recoveries, public.admin_totp IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.admin_recoveries)
                        OR EXISTS (SELECT FROM public.admin_totp WHERE retired_at IS NOT NULL)
                        OR EXISTS (SELECT FROM public.provider_audit_events WHERE action BETWEEN 21 AND 25 OR reason > 25) THEN
                        RAISE EXCEPTION 'Administrator recovery evidence exists' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "admin_recoveries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events");

            migrationBuilder.DropColumn(
                name: "retired_at",
                table: "admin_totp");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 20 OR action BETWEEN 101 AND 103");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 25)");
        }
    }
}
