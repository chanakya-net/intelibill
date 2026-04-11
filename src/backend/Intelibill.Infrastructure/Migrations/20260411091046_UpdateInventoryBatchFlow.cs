using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateInventoryBatchFlow : Migration
    {
        private static readonly string[] BatchIndexColumns = new[] { "shop_id", "item_id", "batch_number" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_batches_shop_id_item_id_batch_number",
                table: "inventory_batches");

            migrationBuilder.AddColumn<bool>(
                name: "is_voided",
                table: "inventory_batches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "original_quantity",
                table: "inventory_batches",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE inventory_batches SET original_quantity = quantity");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_item_id_batch_number",
                table: "inventory_batches",
                columns: BatchIndexColumns,
                unique: true,
                filter: "is_voided = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_batches_shop_id_item_id_batch_number",
                table: "inventory_batches");

            migrationBuilder.DropColumn(
                name: "is_voided",
                table: "inventory_batches");

            migrationBuilder.DropColumn(
                name: "original_quantity",
                table: "inventory_batches");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_item_id_batch_number",
                table: "inventory_batches",
                columns: BatchIndexColumns,
                unique: true);
        }
    }
}
