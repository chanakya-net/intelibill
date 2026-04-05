using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMinSalePriceFromInventoryBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_batches_min_sale_lte_sales",
                table: "inventory_batches");

            migrationBuilder.DropColumn(
                name: "min_sale_price",
                table: "inventory_batches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "min_sale_price",
                table: "inventory_batches",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_batches_min_sale_lte_sales",
                table: "inventory_batches",
                sql: "min_sale_price <= sales_price");
        }
    }
}
