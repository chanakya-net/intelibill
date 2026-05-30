#nullable disable

using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260505170000_AllowReversalStockTransactionSign")]
public partial class AllowReversalStockTransactionSign : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_transactions_quantity_sign_by_type",
            table: "stock_transactions");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_transactions_quantity_sign_by_type",
            table: "stock_transactions",
            sql: "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL') AND quantity < 0) OR (transaction_type IN ('ADJ', 'REV')))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_stock_transactions_quantity_sign_by_type",
            table: "stock_transactions");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stock_transactions_quantity_sign_by_type",
            table: "stock_transactions",
            sql: "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL') AND quantity < 0) OR (transaction_type = 'ADJ'))");
    }
}
