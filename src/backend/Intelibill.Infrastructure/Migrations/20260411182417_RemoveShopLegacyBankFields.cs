using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShopLegacyBankFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_holder_name",
                table: "shops",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_number",
                table: "shops",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bank_account_type",
                table: "shops",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "shops",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ifsc_code",
                table: "shops",
                type: "text",
                nullable: true);
        }
    }
}
