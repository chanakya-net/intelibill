using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierLedgerEntryIdToExpense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "supplier_ledger_entry_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_supplier_ledger_entry_id",
                table: "expenses",
                column: "supplier_ledger_entry_id");

            migrationBuilder.AddForeignKey(
                name: "fk_expenses_supplier_ledger_entries_supplier_ledger_entry_id",
                table: "expenses",
                column: "supplier_ledger_entry_id",
                principalTable: "supplier_ledger_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expenses_supplier_ledger_entries_supplier_ledger_entry_id",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ix_expenses_supplier_ledger_entry_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "supplier_ledger_entry_id",
                table: "expenses");
        }
    }
}
