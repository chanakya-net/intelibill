using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_supplier_ledger_entries_amount_non_zero", "amount <> 0");
                    table.CheckConstraint("ck_supplier_ledger_entries_batch_by_type", "((entry_type = 'GOODS_RECEIVED' AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL))");
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_inventory_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_batch_id",
                table: "supplier_ledger_entries",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_shop_id_batch_id_entry_type",
                table: "supplier_ledger_entries",
                columns: new[] { "shop_id", "batch_id", "entry_type" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_shop_id_supplier_id_entry_date",
                table: "supplier_ledger_entries",
                columns: new[] { "shop_id", "supplier_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_supplier_id",
                table: "supplier_ledger_entries",
                column: "supplier_id");

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE supplier_ledger_entries ENABLE ROW LEVEL SECURITY;

                    CREATE POLICY supplier_ledger_entries_shop_membership_policy
                        ON supplier_ledger_entries
                        USING (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = supplier_ledger_entries.shop_id
                                  AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                            )
                        )
                        WITH CHECK (
                            EXISTS (
                                SELECT 1
                                FROM shop_memberships sm
                                WHERE sm.shop_id = supplier_ledger_entries.shop_id
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
                    DROP POLICY IF EXISTS supplier_ledger_entries_shop_membership_policy ON supplier_ledger_entries;
                    ALTER TABLE supplier_ledger_entries DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.DropTable(
                name: "supplier_ledger_entries");
        }
    }
}

#pragma warning restore CA1861
