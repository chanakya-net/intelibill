using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSessionRevocationAudit : Migration
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

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 20 OR action BETWEEN 101 AND 103");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 25)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.provider_audit_events IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.provider_audit_events WHERE action BETWEEN 17 AND 20 OR reason > 21) THEN
                        RAISE EXCEPTION 'Administrator session audit evidence exists' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 16 OR action BETWEEN 101 AND 103");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 21)");
        }
    }
}
