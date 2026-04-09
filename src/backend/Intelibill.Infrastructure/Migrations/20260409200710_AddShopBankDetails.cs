using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopBankDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_holder_name",
                table: "shops",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_number",
                table: "shops",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_type",
                table: "shops",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "shops",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ifsc_code",
                table: "shops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "account_holder_name",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "bank_account_number",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "bank_account_type",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "ifsc_code",
                table: "shops");
        }
    }
}
