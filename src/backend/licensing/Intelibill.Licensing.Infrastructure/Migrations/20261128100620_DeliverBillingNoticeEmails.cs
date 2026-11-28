using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeliverBillingNoticeEmails : Migration
    {
        private static readonly string[] SourceKey = ["account_id", "id"];
        private static readonly string[] JobKey = ["account_id", "job_id"];
        private static readonly string[] ProofKey = ["account_id", "proof_contact_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_notification_jobs_account_id_id",
                table: "notification_jobs",
                columns: SourceKey);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_billing_notice_contact_account_id_id",
                table: "billing_notice_contacts",
                columns: SourceKey);

            migrationBuilder.CreateTable(
                name: "notification_email_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proof_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    affiliation_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    protected_proof = table.Column<string>(type: "text", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_email_deliveries", x => x.id);
                    table.CheckConstraint("ck_email_source", "(job_id IS NOT NULL AND proof_contact_id IS NULL AND protected_proof IS NULL)\nOR (job_id IS NULL AND proof_contact_id IS NOT NULL AND protected_proof IS NOT NULL AND affiliation_request_id IS NULL)");
                    table.CheckConstraint("ck_email_state", "state IN ('Pending','Leased','Sending','RetryPending','SmtpAccepted','Uncertain','Suppressed','Failed')\nAND attempts BETWEEN 0 AND 5\nAND (state IN ('Leased','Sending')) = (lease_token IS NOT NULL)\nAND (lease_token IS NULL) = (lease_expires_at IS NULL)\nAND (state IN ('Pending','RetryPending')) = (next_attempt_at IS NOT NULL)\nAND (state IN ('SmtpAccepted','Uncertain','Suppressed','Failed')) = (outcome_at IS NOT NULL)\nAND (attempts = 0) = (last_attempt_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_notification_email_deliveries_billing_notice_contact_accoun",
                        columns: x => new { x.account_id, x.proof_contact_id },
                        principalTable: "billing_notice_contacts",
                        principalColumns: SourceKey,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_email_deliveries_notification_jobs_account_id_",
                        columns: x => new { x.account_id, x.job_id },
                        principalTable: "notification_jobs",
                        principalColumns: SourceKey,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_email_deliveries_account_id_job_id",
                table: "notification_email_deliveries",
                columns: JobKey);

            migrationBuilder.CreateIndex(
                name: "ix_notification_email_deliveries_account_id_proof_contact_id",
                table: "notification_email_deliveries",
                columns: ProofKey);

            migrationBuilder.CreateIndex(
                name: "ix_notification_email_deliveries_job_id",
                table: "notification_email_deliveries",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_email_deliveries_lease_expires_at",
                table: "notification_email_deliveries",
                column: "lease_expires_at",
                filter: "state IN ('Leased','Sending')");

            migrationBuilder.CreateIndex(
                name: "ix_notification_email_deliveries_next_attempt_at",
                table: "notification_email_deliveries",
                column: "next_attempt_at",
                filter: "state IN ('Pending','RetryPending')");

            migrationBuilder.CreateIndex(
                name: "ix_notification_email_deliveries_proof_contact_id",
                table: "notification_email_deliveries",
                column: "proof_contact_id",
                unique: true);
            SecureDeliveries(migrationBuilder);
            AddClaim(migrationBuilder);
            AddAdvance(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveEmptyDeliveryLedger(migrationBuilder);
            migrationBuilder.DropTable(
                name: "notification_email_deliveries");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_notification_jobs_account_id_id",
                table: "notification_jobs");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_billing_notice_contact_account_id_id",
                table: "billing_notice_contacts");
        }
    }
}
