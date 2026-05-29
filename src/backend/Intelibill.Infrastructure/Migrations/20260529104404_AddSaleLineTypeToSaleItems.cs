using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleLineTypeToSaleItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "item_id",
                table: "sale_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "inventory_batch_id",
                table: "sale_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "line_code",
                table: "sale_items",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "UNKNOWN");

            migrationBuilder.AddColumn<string>(
                name: "line_name",
                table: "sale_items",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "UNKNOWN");

            migrationBuilder.AddColumn<string>(
                name: "line_type",
                table: "sale_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "GOODS");

            migrationBuilder.AddColumn<Guid>(
                name: "service_id",
                table: "sale_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_service_id",
                table: "sale_items",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id_service_id",
                table: "sale_items",
                columns: new[] { "shop_id", "service_id" });

            migrationBuilder.Sql("""
                UPDATE sale_items si
                SET line_type = 'GOODS',
                    line_name = COALESCE(i.name, 'UNKNOWN'),
                    line_code = COALESCE(i.barcode, 'UNKNOWN')
                FROM items i
                WHERE si.item_id IS NOT NULL
                  AND si.item_id = i.id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_items_line_snapshots_required",
                table: "sale_items",
                sql: "line_name IS NOT NULL AND length(btrim(line_name)) > 0 AND line_code IS NOT NULL AND length(btrim(line_code)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_items_line_type_refs",
                table: "sale_items",
                sql: "((line_type = 'GOODS' AND item_id IS NOT NULL AND inventory_batch_id IS NOT NULL AND service_id IS NULL) OR (line_type = 'SERVICE' AND service_id IS NOT NULL AND item_id IS NULL AND inventory_batch_id IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "fk_sale_items_services_service_id",
                table: "sale_items",
                column: "service_id",
                principalTable: "services",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sale_items_services_service_id",
                table: "sale_items");

            migrationBuilder.DropIndex(
                name: "ix_sale_items_service_id",
                table: "sale_items");

            migrationBuilder.DropIndex(
                name: "ix_sale_items_shop_id_service_id",
                table: "sale_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_items_line_snapshots_required",
                table: "sale_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_items_line_type_refs",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "line_code",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "line_name",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "line_type",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "service_id",
                table: "sale_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "item_id",
                table: "sale_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "inventory_batch_id",
                table: "sale_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}

#pragma warning restore CA1861
