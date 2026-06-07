using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderCloseFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "close_reason",
                table: "purchase_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "purchase_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closed_by",
                table: "purchase_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_closed_by",
                table: "purchase_orders",
                column: "closed_by");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_orders_users_closed_by",
                table: "purchase_orders",
                column: "closed_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_orders_users_closed_by",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "ix_purchase_orders_closed_by",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "close_reason",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "closed_by",
                table: "purchase_orders");
        }
    }
}
