using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemUnknownSupplierInvariant : Migration
    {
        private static readonly string[] OwnerUserIdIsSystemColumns = new[] { "owner_user_id", "is_system" };
        private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "state",
                table: "suppliers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "pin",
                table: "suppliers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "suppliers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "suppliers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_owner_user_id_is_system",
                table: "suppliers",
                columns: OwnerUserIdIsSystemColumns,
                unique: true,
                filter: "is_system = true");

            if (ActiveProvider == SqliteProvider)
            {
                return;
            }

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            migrationBuilder.Sql(
                """
                INSERT INTO suppliers (
                    id,
                    owner_user_id,
                    name,
                    contact_person_name,
                    contact_person_phone,
                    address,
                    city,
                    state,
                    pin,
                    is_system,
                    is_active,
                    is_preferred,
                    created_at,
                    updated_at
                )
                SELECT
                    gen_random_uuid(),
                    u.id,
                    'Unknown Supplier',
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    TRUE,
                    TRUE,
                    FALSE,
                    NOW(),
                    NOW()
                FROM users u
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM suppliers s
                    WHERE s.owner_user_id = u.id
                      AND s.is_system = TRUE
                );
                """);

            migrationBuilder.Sql("ALTER TABLE inventory_batches ALTER COLUMN supplier_id DROP NOT NULL;");

            migrationBuilder.Sql(
                """
                UPDATE inventory_batches b
                SET supplier_id = s.id
                FROM shop_memberships sm
                JOIN suppliers s
                  ON s.owner_user_id = sm.user_id
                 AND s.is_system = TRUE
                WHERE sm.shop_id = b.shop_id
                  AND sm.role = 'Owner'
                  AND b.supplier_id IS NULL;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO supplier_ledger_entries (
                    id,
                    shop_id,
                    supplier_id,
                    batch_id,
                    entry_type,
                    amount,
                    entry_date,
                    notes,
                    created_by,
                    created_at,
                    updated_at
                )
                SELECT
                    gen_random_uuid(),
                    b.shop_id,
                    b.supplier_id,
                    b.id,
                    'GOODS_RECEIVED',
                    ROUND((b.quantity * b.cost_price)::numeric, 2),
                    (b.created_at AT TIME ZONE 'UTC')::date,
                    CASE
                        WHEN s.is_system THEN 'Receipt with no supplier assigned'
                        ELSE NULL
                    END,
                    COALESCE(b.created_by, sm.user_id),
                    NOW(),
                    NOW()
                FROM inventory_batches b
                JOIN suppliers s ON s.id = b.supplier_id
                JOIN shop_memberships sm ON sm.shop_id = b.shop_id AND sm.role = 'Owner'
                WHERE b.supplier_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM supplier_ledger_entries sle
                      WHERE sle.batch_id = b.id
                        AND sle.entry_type = 'GOODS_RECEIVED'
                  );
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION batch_has_ledger_entry(batch_uuid uuid)
                RETURNS boolean
                LANGUAGE sql
                STABLE
                AS $$
                    SELECT EXISTS (
                        SELECT 1
                        FROM supplier_ledger_entries sle
                        WHERE sle.batch_id = batch_uuid
                    );
                $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE inventory_batches
                ADD CONSTRAINT chk_batch_always_has_ledger
                CHECK (
                    supplier_id IS NOT NULL
                    OR batch_has_ledger_entry(id)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != SqliteProvider)
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE inventory_batches
                    DROP CONSTRAINT IF EXISTS chk_batch_always_has_ledger;
                    """);

                migrationBuilder.Sql("DROP FUNCTION IF EXISTS batch_has_ledger_entry(uuid);");
            }

            migrationBuilder.DropIndex(
                name: "ix_suppliers_owner_user_id_is_system",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "is_system",
                table: "suppliers");

            migrationBuilder.AlterColumn<string>(
                name: "state",
                table: "suppliers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "pin",
                table: "suppliers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "suppliers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "suppliers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);
        }
    }
}
