using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineSalesSyncMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_sale_id",
                table: "sales",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "device_id",
                table: "sales",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "sales",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "synced_at",
                table: "sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_device_id_client_sale_id",
                table: "sales",
                columns: new[] { "shop_id", "device_id", "client_sale_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_shop_id_device_id_client_sale_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "client_sale_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "source",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "synced_at",
                table: "sales");
        }
    }
}
