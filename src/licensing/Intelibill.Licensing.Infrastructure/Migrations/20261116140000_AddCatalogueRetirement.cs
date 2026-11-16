using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueRetirement : Migration
    {
        private static readonly string[] OperationColumns = ["admin_id", "operation_key"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalogue_retirements",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_retirements", x => x.version_id);
                    table.CheckConstraint("ck_catalogue_retirement", "expected_version > 0 AND char_length(btrim(reason)) BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_catalogue_retirements_catalogue_publications_version_id",
                        column: x => x.version_id,
                        principalTable: "catalogue_publications",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "withdrawn_catalogue_offers",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withdrawn_catalogue_offers", x => x.version_id);
                    table.ForeignKey(
                        name: "fk_withdrawn_catalogue_offers_catalogue_retirements_version_id",
                        column: x => x.version_id,
                        principalTable: "catalogue_retirements",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_withdrawn_catalogue_offers_published_catalogue_offers_versi",
                        column: x => x.version_id,
                        principalTable: "published_catalogue_offers",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_retirements_admin_id_operation_key",
                table: "catalogue_retirements",
                columns: OperationColumns,
                unique: true);
            migrationBuilder.Sql(RetirementSecurity);
            migrationBuilder.Sql(RetirementEvidence);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveRetirementGuards);
            migrationBuilder.DropTable(
                name: "withdrawn_catalogue_offers");

            migrationBuilder.DropTable(
                name: "catalogue_retirements");
        }
    }
}
