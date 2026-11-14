using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAuditInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.AddColumn<int>(
                name: "actor_kind",
                table: "provider_audit_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "after",
                table: "provider_audit_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "before",
                table: "provider_audit_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                table: "provider_audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reason",
                table: "provider_audit_events",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_audit_events_occurred_at_id",
                table: "provider_audit_events",
                columns: ["occurred_at", "id"],
                descending: []);

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 16 OR action BETWEEN 101 AND 103");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 21)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.provider_audit_events IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.provider_audit_events
                        WHERE correlation_id IS NOT NULL OR action NOT BETWEEN 1 AND 16) THEN
                        RAISE EXCEPTION 'Provider audit inspection evidence exists' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_provider_audit_events_occurred_at_id",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events");

            migrationBuilder.DropColumn(
                name: "actor_kind",
                table: "provider_audit_events");

            migrationBuilder.DropColumn(
                name: "after",
                table: "provider_audit_events");

            migrationBuilder.DropColumn(
                name: "before",
                table: "provider_audit_events");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "provider_audit_events");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "provider_audit_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 16");
        }
    }
}
