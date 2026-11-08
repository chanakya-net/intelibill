using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTillReconciliationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_till_sources_type",
                table: "till_source_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_till_movements_type",
                table: "till_movements");

            migrationBuilder.AddColumn<string>(
                name: "evidence_reason",
                table: "till_late_postings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_till_sources_type",
                table: "till_source_records",
                sql: "source_type BETWEEN 1 AND 12");

            migrationBuilder.AddCheckConstraint(
                name: "ck_till_movements_type",
                table: "till_movements",
                sql: "method BETWEEN 2 AND 6 AND source_type BETWEEN 1 AND 12 AND sequence > 0 AND line_number = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_till_late_postings_evidence_reason",
                table: "till_late_postings",
                sql: "evidence_reason IS NULL OR length(btrim(evidence_reason)) BETWEEN 1 AND 500");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new System.NotSupportedException("Retain till schema and immutable settlement facts; disable new postings instead of rolling schema back.");
    }
}
