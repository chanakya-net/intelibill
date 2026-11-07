using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseTreatment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "treatment",
                table: "expenses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                WITH RECURSIVE settlement_ancestry AS (
                    SELECT id, shop_id FROM expenses WHERE supplier_ledger_entry_id IS NOT NULL
                    UNION
                    SELECT child.id, child.shop_id
                    FROM expenses child JOIN settlement_ancestry parent
                      ON child.original_expense_id = parent.id AND child.shop_id = parent.shop_id
                )
                UPDATE expenses e SET treatment = 2
                FROM settlement_ancestry source
                WHERE e.id = source.id AND e.shop_id = source.shop_id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_treatment",
                table: "expenses",
                sql: "treatment IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_treatment",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "treatment",
                table: "expenses");
        }
    }
}
