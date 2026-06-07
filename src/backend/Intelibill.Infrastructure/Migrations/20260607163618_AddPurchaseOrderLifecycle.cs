using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "purchase_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "purchase_orders");
        }
    }
}
