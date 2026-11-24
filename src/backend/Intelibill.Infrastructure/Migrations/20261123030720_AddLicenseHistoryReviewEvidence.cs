using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated column arrays

namespace Intelibill.Infrastructure.Migrations
{
    /// <summary>
    /// Append-only conflict observations for LicenseHistoryReview cases and a filtered unique index
    /// guaranteeing at most one typed LicenseHistoryReview case per held envelope across statuses.
    /// Apply through the migration job before writers that record historical review evidence.
    /// </summary>
    public partial class AddLicenseHistoryReviewEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_history_review_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offline_submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    subreason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    compared_field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_account_revision = table.Column<long>(type: "bigint", nullable: true),
                    observed_shop_revision = table.Column<long>(type: "bigint", nullable: true),
                    source_completion_cutoff = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    competing_submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_license_history_review_observations", x => x.id);
                    table.ForeignKey(
                        name: "fk_license_history_review_observations_offline_submissions_com",
                        columns: x => new { x.competing_submission_id, x.shop_id },
                        principalTable: "offline_submissions",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_license_history_review_observations_offline_submissions_off",
                        columns: x => new { x.offline_submission_id, x.shop_id },
                        principalTable: "offline_submissions",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_license_history_review_observations_reconciliation_issues_c",
                        columns: x => new { x.case_id, x.shop_id },
                        principalTable: "reconciliation_issues",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_license_history_review_observations_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_issues_shop_id_offline_submission_id",
                table: "reconciliation_issues",
                columns: new[] { "shop_id", "offline_submission_id" },
                unique: true,
                filter: "issue_type = 15 AND offline_submission_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_license_history_review_observations_case_id_shop_id",
                table: "license_history_review_observations",
                columns: new[] { "case_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_license_history_review_observations_competing_submission_id",
                table: "license_history_review_observations",
                columns: new[] { "competing_submission_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_license_history_review_observations_offline_submission_id_s",
                table: "license_history_review_observations",
                columns: new[] { "offline_submission_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_license_history_review_observations_shop_id_case_id_fingerp",
                table: "license_history_review_observations",
                columns: new[] { "shop_id", "case_id", "fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_license_history_review_observations_shop_id_case_id_observe",
                table: "license_history_review_observations",
                columns: new[] { "shop_id", "case_id", "observed_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_license_history_review_observations_shop_id_offline_submiss",
                table: "license_history_review_observations",
                columns: new[] { "shop_id", "offline_submission_id" });

            migrationBuilder.Sql("""
                ALTER TABLE public.license_history_review_observations ADD CONSTRAINT valid_license_history_observation CHECK (
                    btrim(reason_code) <> ''
                    AND btrim(subreason) <> ''
                    AND (compared_field IS NULL OR btrim(compared_field) <> '')
                    AND fingerprint ~ '^[0-9a-f]{64}$'
                    AND isfinite(observed_at)
                    AND (window_start IS NULL OR isfinite(window_start))
                    AND (window_end IS NULL OR isfinite(window_end))
                    AND (window_start IS NULL OR window_end IS NULL OR window_start <= window_end)
                    AND (observed_account_revision IS NULL OR observed_account_revision > 0)
                    AND (observed_shop_revision IS NULL OR observed_shop_revision > 0)
                    AND (source_completion_cutoff IS NULL OR isfinite(source_completion_cutoff))
                    AND (competing_submission_id IS NULL OR offline_submission_id IS NULL OR competing_submission_id <> offline_submission_id));
                ALTER TABLE public.license_history_review_observations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.license_history_review_observations FORCE ROW LEVEL SECURITY;
                CREATE POLICY license_history_review_observations_shop_isolation ON public.license_history_review_observations
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.license_history_review_observations
                    FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
                CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.license_history_review_observations
                    FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.license_history_review_observations, public.reconciliation_issues IN ACCESS EXCLUSIVE MODE;
                ALTER TABLE public.license_history_review_observations NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.reconciliation_issues NO FORCE ROW LEVEL SECURITY;
                DO $$ BEGIN
                    IF EXISTS (SELECT FROM public.license_history_review_observations)
                        OR EXISTS (SELECT FROM public.reconciliation_issues WHERE issue_type = 15) THEN
                        RAISE EXCEPTION 'Retained licence history review evidence prevents downgrade' USING ERRCODE = '55000';
                    END IF;
                END $$;
                ALTER TABLE public.reconciliation_issues FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "license_history_review_observations");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_issues_shop_id_offline_submission_id",
                table: "reconciliation_issues");
        }
    }
}
