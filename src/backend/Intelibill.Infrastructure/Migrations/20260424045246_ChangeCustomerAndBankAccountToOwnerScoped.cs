using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCustomerAndBankAccountToOwnerScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bank_accounts_shops_shop_id",
                table: "bank_accounts");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_shops_shop_id",
                table: "customers");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bank_accounts_users_owner_user_id",
                table: "bank_accounts");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_users_owner_user_id",
                table: "customers");

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
        }
    }
}
