using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenses : Migration
    {
        /// <inheritdoc />
#pragma warning disable CA1861
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_categories_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    paid_to = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    original_expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_expenses_expense_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expenses_expenses_original_expense_id",
                        column: x => x.original_expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expenses_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_shop_id_name",
                table: "expense_categories",
                columns: new[] { "shop_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_id",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_original_expense_id",
                table: "expenses",
                column: "original_expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_shop_id_expense_date",
                table: "expenses",
                columns: new[] { "shop_id", "expense_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_shop_id_is_voided",
                table: "expenses",
                columns: new[] { "shop_id", "is_voided" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_shop_id_paid_to",
                table: "expenses",
                columns: new[] { "shop_id", "paid_to" });

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE expenses ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE expense_categories ENABLE ROW LEVEL SECURITY;

                    CREATE POLICY expenses_shop_membership_policy
                        ON expenses
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = expenses.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = expenses.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY expense_categories_shop_membership_policy
                        ON expense_categories
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = expense_categories.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = expense_categories.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );
                    """);
            }
        }
#pragma warning restore CA1861

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DROP POLICY IF EXISTS expense_categories_shop_membership_policy ON expense_categories;
                    DROP POLICY IF EXISTS expenses_shop_membership_policy ON expenses;

                    ALTER TABLE expense_categories DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE expenses DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "expense_categories");
        }
    }
}
