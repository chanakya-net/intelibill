using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration array arguments.

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptClientLineIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_line_id",
                table: "purchase_order_receipt_lines",
                type: "uuid",
                nullable: true);

            // Preserve existing immutable receipt-line identity; nullable supports old-server writes during rollout.
            migrationBuilder.Sql("""
                DO $backfill$
                DECLARE
                    target_shop uuid;
                    previous_shop text := current_setting('app.active_shop_id', true);
                BEGIN
                    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
                        PERFORM set_config('app.active_shop_id', target_shop::text, true);
                        UPDATE purchase_order_receipt_lines line SET client_line_id = line.id
                        FROM purchase_order_receipts receipt
                        WHERE line.purchase_order_receipt_id = receipt.id
                          AND receipt.shop_id = target_shop AND line.client_line_id IS NULL;
                    END LOOP;
                    PERFORM set_config('app.active_shop_id', coalesce(previous_shop, ''), true);
                END
                $backfill$;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_receipt_lines_purchase_order_receipt_id_clie",
                table: "purchase_order_receipt_lines",
                columns: new[] { "purchase_order_receipt_id", "client_line_id" },
                unique: true,
                filter: "client_line_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_purchase_order_receipt_lines_purchase_order_receipt_id_clie",
                table: "purchase_order_receipt_lines");

            migrationBuilder.DropColumn(
                name: "client_line_id",
                table: "purchase_order_receipt_lines");
        }
    }
}
