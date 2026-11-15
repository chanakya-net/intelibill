using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCataloguePublication : Migration
    {
        private static readonly string[] OfferColumns = ["development_fixture", "plan_id", "version_number"];
        private static readonly string[] OperationColumns = ["admin_id", "operation_key"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "trial_policy_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "plan_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "catalogue_preparations",
                columns: table => new
                {
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    trial_policy_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    configuration_revision = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    snapshot = table.Column<string>(type: "text", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_preparations", x => new { x.admin_id, x.operation_key });
                    table.CheckConstraint("ck_catalogue_preparation", "expected_version > 0 AND snapshot_hash ~ '^[A-F0-9]{64}$' AND char_length(reason) BETWEEN 1 AND 500 AND (plan_id IS NULL) = (trial_policy_version_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "catalogue_publications",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_publications", x => x.version_id);
                    table.ForeignKey(
                        name: "fk_catalogue_publications_catalogue_preparations_admin_id_oper",
                        columns: x => new { x.admin_id, x.operation_key },
                        principalTable: "catalogue_preparations",
                        principalColumns: OperationColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_trial_policy_versions_published",
                table: "trial_policy_versions",
                column: "status",
                unique: true,
                filter: "status = 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trial_policy_versions_source",
                table: "trial_policy_versions",
                sql: "source IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ux_plan_versions_published",
                table: "plan_versions",
                column: "plan_id",
                unique: true,
                filter: "status = 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_versions_source",
                table: "plan_versions",
                sql: "source IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_publications_admin_id_operation_key",
                table: "catalogue_publications",
                columns: OperationColumns,
                unique: true);
            migrationBuilder.DropCheckConstraint(
                name: "ck_trial_policy_versions_source",
                table: "trial_policy_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_versions_source",
                table: "plan_versions");

            migrationBuilder.AddColumn<int>(
                name: "source_expected_version",
                table: "trial_policy_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_version_id",
                table: "trial_policy_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_expected_version",
                table: "plan_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_version_id",
                table: "plan_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_trial_policy_versions_source",
                table: "trial_policy_versions",
                sql: "source IN (0, 1, 2) AND (source_version_id IS NULL) = (source_expected_version IS NULL)\nAND (source_expected_version IS NULL OR source_expected_version > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_versions_source",
                table: "plan_versions",
                sql: "source IN (0, 1, 2) AND (source_version_id IS NULL) = (source_expected_version IS NULL)\nAND (source_expected_version IS NULL OR source_expected_version > 0)");
            migrationBuilder.CreateTable(
                name: "published_catalogue_offers",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    development_fixture = table.Column<bool>(type: "boolean", nullable: false),
                    snapshot = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_published_catalogue_offers", x => x.version_id);
                    table.CheckConstraint("ck_published_catalogue_offer", "version_number > 0 AND octet_length(snapshot) BETWEEN 2 AND 2400");
                    table.ForeignKey(
                        name: "fk_published_catalogue_offers_catalogue_publications_version_id",
                        column: x => x.version_id,
                        principalTable: "catalogue_publications",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_published_catalogue_offers_development_fixture_plan_id_vers",
                table: "published_catalogue_offers",
                columns: OfferColumns,
                unique: true);
            migrationBuilder.Sql(PublicationSecurity);
            migrationBuilder.Sql(LifecycleGuard);
            migrationBuilder.Sql(ProjectionSecurity);
            migrationBuilder.Sql(PublicationEvidence);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RequireEmptyPublication);
            migrationBuilder.Sql(RestoreDraftGuard);
            migrationBuilder.DropTable("published_catalogue_offers");
            migrationBuilder.DropTable("catalogue_publications");
            migrationBuilder.DropTable("catalogue_preparations");
            RemovePublicationMetadata(migrationBuilder, "plan_versions", "ux_plan_versions_published");
            RemovePublicationMetadata(migrationBuilder, "trial_policy_versions", "ux_trial_policy_versions_published");
        }

        private static void RemovePublicationMetadata(MigrationBuilder migrationBuilder, string table, string publishedIndex)
        {
            migrationBuilder.DropIndex(publishedIndex, table);
            migrationBuilder.DropCheckConstraint($"ck_{table}_source", table);
            migrationBuilder.DropColumn("source_expected_version", table);
            migrationBuilder.DropColumn("source_version_id", table);
            migrationBuilder.DropColumn("source", table);
        }
    }
}
