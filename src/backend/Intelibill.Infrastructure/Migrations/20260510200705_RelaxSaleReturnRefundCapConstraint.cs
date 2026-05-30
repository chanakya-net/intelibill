using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelaxSaleReturnRefundCapConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_return_items_refund_cap",
                table: "sale_return_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_return_items_refund_cap",
                table: "sale_return_items",
                sql: "approved_refund_amount <= max_refund_amount OR notes IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_return_items_refund_cap",
                table: "sale_return_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_return_items_refund_cap",
                table: "sale_return_items",
                sql: "approved_refund_amount <= max_refund_amount");
        }
    }
}
