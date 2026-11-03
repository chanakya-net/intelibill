using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineSubmissionReconciliationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                table: "reconciliation_issues",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "linked_operation_id",
                table: "reconciliation_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "offline_submission_id",
                table: "reconciliation_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "resolution",
                table: "reconciliation_issues",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_reason",
                table: "reconciliation_issues",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolution_stock_transaction_id",
                table: "reconciliation_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "reconciliation_issues",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // reconciliation_issues.xmin becomes the case row version. It is a PostgreSQL system
            // column, so no DDL is required.

            migrationBuilder.AddColumn<Guid>(
                name: "reserved_by_session_id",
                table: "invoice_leases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reserved_by_user_id",
                table: "invoice_leases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "offline_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    client_sale_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    claimed_sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asserted_issuer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_issuer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recovery_operation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_offline_submissions_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_offline_submissions_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_case_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: false),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    linked_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_case_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_case_events_reconciliation_issues_case_id",
                        column: x => x.case_id,
                        principalTable: "reconciliation_issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_case_events_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_offline_submission_id",
                table: "reconciliation_issues",
                column: "offline_submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_resolution_stock_transaction_id",
                table: "reconciliation_issues",
                column: "resolution_stock_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_shop_id_linked_operation_id",
                table: "reconciliation_issues",
                columns: new[] { "shop_id", "linked_operation_id" },
                unique: true,
                filter: "resolution = 4");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_shop_id_status_created_at",
                table: "reconciliation_issues",
                columns: new[] { "shop_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_offline_submissions_sale_id",
                table: "offline_submissions",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_offline_submissions_shop_id_device_id_client_sale_id_payloa",
                table: "offline_submissions",
                columns: new[] { "shop_id", "device_id", "client_sale_id", "payload_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offline_submissions_shop_id_device_id_submitted_by_user_id",
                table: "offline_submissions",
                columns: new[] { "shop_id", "device_id", "submitted_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_case_events_case_id",
                table: "reconciliation_case_events",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_case_events_shop_id_case_id_occurred_at",
                table: "reconciliation_case_events",
                columns: new[] { "shop_id", "case_id", "occurred_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_reconciliation_issues_offline_submissions_offline_submissio",
                table: "reconciliation_issues",
                column: "offline_submission_id",
                principalTable: "offline_submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_reconciliation_issues_stock_transactions_resolution_stock_t",
                table: "reconciliation_issues",
                column: "resolution_stock_transaction_id",
                principalTable: "stock_transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Cases filed before the status column existed: resolved ones are Resolved (3), the rest Open (1).
            migrationBuilder.Sql(
                "UPDATE reconciliation_issues SET status = CASE WHEN is_resolved THEN 3 ELSE 1 END;");

            // Tenant data: same predicate as CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE offline_submissions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE offline_submissions FORCE ROW LEVEL SECURITY;
                CREATE POLICY offline_submissions_shop_isolation ON offline_submissions
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);

                ALTER TABLE reconciliation_case_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE reconciliation_case_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY reconciliation_case_events_shop_isolation ON reconciliation_case_events
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back discards submission envelopes and case events, which are audit records.
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS offline_submissions_shop_isolation ON offline_submissions;
                DROP POLICY IF EXISTS reconciliation_case_events_shop_isolation ON reconciliation_case_events;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_reconciliation_issues_offline_submissions_offline_submissio",
                table: "reconciliation_issues");

            migrationBuilder.DropForeignKey(
                name: "fk_reconciliation_issues_stock_transactions_resolution_stock_t",
                table: "reconciliation_issues");

            migrationBuilder.DropTable(
                name: "offline_submissions");

            migrationBuilder.DropTable(
                name: "reconciliation_case_events");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_issues_offline_submission_id",
                table: "reconciliation_issues");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_issues_resolution_stock_transaction_id",
                table: "reconciliation_issues");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_issues_shop_id_linked_operation_id",
                table: "reconciliation_issues");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_issues_shop_id_status_created_at",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "linked_operation_id",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "offline_submission_id",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "resolution",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "resolution_reason",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "resolution_stock_transaction_id",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "status",
                table: "reconciliation_issues");

            migrationBuilder.DropColumn(
                name: "reserved_by_session_id",
                table: "invoice_leases");

            migrationBuilder.DropColumn(
                name: "reserved_by_user_id",
                table: "invoice_leases");
        }
    }
}
