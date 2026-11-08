using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleTillEvidence : Migration
    {
        private static readonly string[] SessionShopColumns = ["till_session_id", "shop_id"];
        private static readonly string[] PrincipalColumns = ["id", "shop_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tender_lines",
                table: "sales",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "till_override_reason",
                table: "sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "till_session_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            // Existing sales RLS policies cover these nullable columns. Never assign historical sessions.
            migrationBuilder.AddCheckConstraint("ck_sales_till_evidence", "sales",
                "(till_session_id IS NULL AND tender_lines IS NULL AND till_override_reason IS NULL) OR (till_session_id IS NOT NULL AND tender_lines IS NOT NULL AND jsonb_typeof(tender_lines) = 'array' AND jsonb_array_length(tender_lines) <= 1)");

            migrationBuilder.CreateIndex(
                name: "ix_sales_till_session_id_shop_id",
                table: "sales",
                columns: SessionShopColumns);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_till_sessions_till_session_id_shop_id",
                table: "sales",
                columns: SessionShopColumns,
                principalTable: "till_sessions",
                principalColumns: PrincipalColumns,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("ck_sales_till_evidence", "sales");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_till_sessions_till_session_id_shop_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sales_till_session_id_shop_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "tender_lines",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "till_override_reason",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "till_session_id",
                table: "sales");
        }
    }
}
