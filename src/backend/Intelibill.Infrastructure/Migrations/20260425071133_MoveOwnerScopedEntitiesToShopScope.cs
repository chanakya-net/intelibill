using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveOwnerScopedEntitiesToShopScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bank_accounts_users_owner_user_id",
                table: "bank_accounts");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_users_owner_user_id",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "fk_suppliers_users_owner_user_id",
                table: "suppliers");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "suppliers",
                newName: "shop_id");

            migrationBuilder.RenameIndex(
                name: "ix_suppliers_owner_user_id_is_system",
                table: "suppliers",
                newName: "ix_suppliers_shop_id_is_system");

            migrationBuilder.RenameIndex(
                name: "ix_suppliers_owner_user_id_is_active",
                table: "suppliers",
                newName: "ix_suppliers_shop_id_is_active");

            migrationBuilder.RenameIndex(
                name: "ix_suppliers_owner_user_id",
                table: "suppliers",
                newName: "ix_suppliers_shop_id");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "customers",
                newName: "shop_id");

            migrationBuilder.RenameIndex(
                name: "ix_customers_owner_user_id_phone_number",
                table: "customers",
                newName: "ix_customers_shop_id_phone_number");

            migrationBuilder.RenameIndex(
                name: "ix_customers_owner_user_id_is_active",
                table: "customers",
                newName: "ix_customers_shop_id_is_active");

            migrationBuilder.RenameIndex(
                name: "ix_customers_owner_user_id",
                table: "customers",
                newName: "ix_customers_shop_id");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "bank_accounts",
                newName: "shop_id");

            migrationBuilder.RenameIndex(
                name: "ix_bank_accounts_owner_user_id",
                table: "bank_accounts",
                newName: "ix_bank_accounts_shop_id");

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // Dev-only: old data has user IDs in these columns, not shop IDs. Clear all dependent data.
                migrationBuilder.Sql("TRUNCATE bank_accounts, customers, suppliers, customer_ledger_entries, supplier_ledger_entries, expenses, expense_categories, sale_items, sales, stock_transactions, inventory_batches, inventory, items CASCADE;");
            }

            migrationBuilder.AddForeignKey(
                name: "fk_bank_accounts_shops_shop_id",
                table: "bank_accounts",
                column: "shop_id",
                principalTable: "shops",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_customers_shops_shop_id",
                table: "customers",
                column: "shop_id",
                principalTable: "shops",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_suppliers_shops_shop_id",
                table: "suppliers",
                column: "shop_id",
                principalTable: "shops",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE bank_accounts ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE customers ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE suppliers ENABLE ROW LEVEL SECURITY;

                    CREATE POLICY bank_accounts_shop_policy
                        ON bank_accounts
                        USING (
                            EXISTS (
                                SELECT 1 FROM shop_memberships sm
                                WHERE sm.shop_id = bank_accounts.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1 FROM shop_memberships sm
                                WHERE sm.shop_id = bank_accounts.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY customers_shop_policy
                        ON customers
                        USING (
                            EXISTS (
                                SELECT 1 FROM shop_memberships sm
                                WHERE sm.shop_id = customers.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1 FROM shop_memberships sm
                                WHERE sm.shop_id = customers.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );

                    CREATE POLICY suppliers_shop_policy
                        ON suppliers
                        USING (
                            EXISTS (
                                SELECT 1 FROM shop_memberships sm
                                WHERE sm.shop_id = suppliers.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1 FROM shop_memberships sm
                                WHERE sm.shop_id = suppliers.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        );
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    DROP POLICY IF EXISTS suppliers_shop_policy ON suppliers;
                    DROP POLICY IF EXISTS customers_shop_policy ON customers;
                    DROP POLICY IF EXISTS bank_accounts_shop_policy ON bank_accounts;

                    ALTER TABLE suppliers DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE customers DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE bank_accounts DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropForeignKey(
                name: "fk_bank_accounts_shops_shop_id",
                table: "bank_accounts");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_shops_shop_id",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "fk_suppliers_shops_shop_id",
                table: "suppliers");

            migrationBuilder.RenameColumn(
                name: "shop_id",
                table: "suppliers",
                newName: "owner_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_suppliers_shop_id_is_system",
                table: "suppliers",
                newName: "ix_suppliers_owner_user_id_is_system");

            migrationBuilder.RenameIndex(
                name: "ix_suppliers_shop_id_is_active",
                table: "suppliers",
                newName: "ix_suppliers_owner_user_id_is_active");

            migrationBuilder.RenameIndex(
                name: "ix_suppliers_shop_id",
                table: "suppliers",
                newName: "ix_suppliers_owner_user_id");

            migrationBuilder.RenameColumn(
                name: "shop_id",
                table: "customers",
                newName: "owner_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_customers_shop_id_phone_number",
                table: "customers",
                newName: "ix_customers_owner_user_id_phone_number");

            migrationBuilder.RenameIndex(
                name: "ix_customers_shop_id_is_active",
                table: "customers",
                newName: "ix_customers_owner_user_id_is_active");

            migrationBuilder.RenameIndex(
                name: "ix_customers_shop_id",
                table: "customers",
                newName: "ix_customers_owner_user_id");

            migrationBuilder.RenameColumn(
                name: "shop_id",
                table: "bank_accounts",
                newName: "owner_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_bank_accounts_shop_id",
                table: "bank_accounts",
                newName: "ix_bank_accounts_owner_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bank_accounts_users_owner_user_id",
                table: "bank_accounts",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_customers_users_owner_user_id",
                table: "customers",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_suppliers_users_owner_user_id",
                table: "suppliers",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
