using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    #pragma warning disable CA1861
    /// <inheritdoc />
    public partial class AddCustomerLedgerAndSalePaymentSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "due_amount",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "paid_amount",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "customer_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_customer_ledger_entries_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_customer_ledger_entries_sale_by_type", "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type = 'PAYMENT_RECEIVED' AND sale_id IS NULL))");
                    table.ForeignKey(
                        name: "fk_customer_ledger_entries_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_ledger_entries_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_ledger_entries_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_customer_id",
                table: "customer_ledger_entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_sale_id",
                table: "customer_ledger_entries",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_shop_id_customer_id_entry_date",
                table: "customer_ledger_entries",
                columns: new[] { "shop_id", "customer_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_shop_id_sale_id_entry_type",
                table: "customer_ledger_entries",
                columns: new[] { "shop_id", "sale_id", "entry_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_ledger_entries");

            migrationBuilder.DropColumn(
                name: "due_amount",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "paid_amount",
                table: "sales");
        }
    }
    #pragma warning restore CA1861
}
