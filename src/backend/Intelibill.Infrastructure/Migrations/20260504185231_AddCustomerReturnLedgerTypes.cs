using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturnLedgerTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries");

            migrationBuilder.AlterColumn<string>(
                name: "entry_type",
                table: "customer_ledger_entries",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries",
                sql: "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type = 'PAYMENT_RECEIVED' AND sale_id IS NULL) OR entry_type IN ('RETURN_CREDIT', 'RETURN_CREDIT_REVERSAL'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries");

            migrationBuilder.AlterColumn<string>(
                name: "entry_type",
                table: "customer_ledger_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries",
                sql: "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type = 'PAYMENT_RECEIVED' AND sale_id IS NULL))");
        }
    }
}
