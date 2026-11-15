using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminCapabilities : Migration
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

            migrationBuilder.AddColumn<int>(
                name: "capabilities",
                table: "admin_identities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE public.admin_identities AS admin
                SET capabilities = 16
                FROM public.initial_admin_bootstrap AS bootstrap
                WHERE bootstrap.id = 1 AND bootstrap.consumed_at IS NOT NULL AND bootstrap.owner_id = admin.id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 25 OR action BETWEEN 101 AND 107");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 36)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_capabilities",
                table: "admin_identities",
                sql: "capabilities BETWEEN 0 AND 31");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.initial_admin_bootstrap, public.admin_identities, public.provider_audit_events
                IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.provider_audit_events WHERE action BETWEEN 104 AND 107 OR reason > 31)
                        OR EXISTS (
                            SELECT FROM public.admin_identities AS admin
                            CROSS JOIN public.initial_admin_bootstrap AS bootstrap
                            WHERE admin.capabilities <>
                                CASE WHEN bootstrap.consumed_at IS NOT NULL AND bootstrap.owner_id = admin.id
                                    THEN 16 ELSE 0 END)
                        THEN RAISE EXCEPTION 'Administrator capability evidence exists' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_capabilities",
                table: "admin_identities");

            migrationBuilder.DropColumn(
                name: "capabilities",
                table: "admin_identities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 25 OR action BETWEEN 101 AND 103");

            migrationBuilder.AddCheckConstraint(
                name: "ck_provider_audit_metadata",
                table: "provider_audit_events",
                sql: "(num_nonnulls(actor_kind, reason, correlation_id) = 0 AND action BETWEEN 1 AND 16\n    AND \"before\" IS NULL AND \"after\" IS NULL)\nOR (num_nonnulls(actor_kind, reason, correlation_id) = 3\n    AND actor_kind BETWEEN 1 AND 5 AND reason BETWEEN 1 AND 31)");
        }
    }
}
