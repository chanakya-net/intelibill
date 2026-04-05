using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveSupplierReferenceFromItemToBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                table: "inventory_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE inventory_batches AS b
                SET supplier_id = i.preferred_supplier_id
                FROM items AS i
                WHERE b.item_id = i.id
                  AND b.shop_id = i.shop_id
                  AND b.supplier_id IS NULL
                  AND i.preferred_supplier_id IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_items_suppliers_preferred_supplier_id",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_preferred_supplier_id",
                table: "items");

            migrationBuilder.DropColumn(
                name: "preferred_supplier_id",
                table: "items");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_supplier_id",
                table: "inventory_batches",
                columns: new[] { "shop_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_supplier_id",
                table: "inventory_batches",
                column: "supplier_id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_batches_suppliers_supplier_id",
                table: "inventory_batches",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_batches_suppliers_supplier_id",
                table: "inventory_batches");

            migrationBuilder.DropIndex(
                name: "ix_inventory_batches_shop_id_supplier_id",
                table: "inventory_batches");

            migrationBuilder.DropIndex(
                name: "ix_inventory_batches_supplier_id",
                table: "inventory_batches");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "inventory_batches");

            migrationBuilder.AddColumn<Guid>(
                name: "preferred_supplier_id",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE items AS i
                SET preferred_supplier_id = b.supplier_id
                FROM (
                    SELECT DISTINCT ON (item_id, shop_id)
                        item_id,
                        shop_id,
                        supplier_id
                    FROM inventory_batches
                    WHERE supplier_id IS NOT NULL
                    ORDER BY item_id, shop_id, created_at DESC
                ) AS b
                WHERE i.id = b.item_id
                  AND i.shop_id = b.shop_id
                  AND i.preferred_supplier_id IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_items_preferred_supplier_id",
                table: "items",
                column: "preferred_supplier_id");

            migrationBuilder.AddForeignKey(
                name: "fk_items_suppliers_preferred_supplier_id",
                table: "items",
                column: "preferred_supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}

#pragma warning restore CA1861
