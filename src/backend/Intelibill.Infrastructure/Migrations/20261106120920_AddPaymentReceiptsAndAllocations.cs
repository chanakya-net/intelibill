using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReceiptsAndAllocations : Migration
    {
        private static readonly string[] AllocationReceiptColumns = [ "shop_id", "receipt_id" ];
        private static readonly string[] AllocationTargetColumns = [ "shop_id", "target_type", "target_id" ];
        private static readonly string[] ReceiptLedgerEntryColumns = [ "party_type", "ledger_entry_id" ];
        private static readonly string[] ReceiptPartyColumns = [ "shop_id", "party_type", "party_id", "business_date" ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries");

            migrationBuilder.CreateTable(
                name: "payment_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_legacy = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_receipts", x => x.id);
                    table.CheckConstraint("ck_payment_receipts_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payment_receipts_expense_supplier_only", "expense_id IS NULL OR party_type = 'SUPPLIER'");
                    table.ForeignKey(
                        name: "fk_payment_receipts_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_receipts_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_receipts_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_allocations_payment_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "payment_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_reversals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_reversals", x => x.id);
                    table.CheckConstraint("ck_payment_reversals_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payment_reversals_reason_required", "length(btrim(reason)) >= 3");
                    table.ForeignKey(
                        name: "fk_payment_reversals_payment_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "payment_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_reversals_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries",
                sql: "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type IN ('PAYMENT_RECEIVED', 'PAYMENT_REVERSAL') AND sale_id IS NULL) OR entry_type IN ('RETURN_CREDIT', 'RETURN_CREDIT_REVERSAL'))");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_receipt_id",
                table: "payment_allocations",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_shop_id_receipt_id",
                table: "payment_allocations",
                columns: AllocationReceiptColumns);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_shop_id_target_type_target_id",
                table: "payment_allocations",
                columns: AllocationTargetColumns);

            migrationBuilder.CreateIndex(
                name: "ix_payment_receipts_bank_account_id",
                table: "payment_receipts",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_receipts_expense_id",
                table: "payment_receipts",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_receipts_party_type_ledger_entry_id",
                table: "payment_receipts",
                columns: ReceiptLedgerEntryColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_receipts_shop_id_party_type_party_id_business_date",
                table: "payment_receipts",
                columns: ReceiptPartyColumns);

            migrationBuilder.CreateIndex(
                name: "ix_payment_reversals_receipt_id",
                table: "payment_reversals",
                column: "receipt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_reversals_shop_id",
                table: "payment_reversals",
                column: "shop_id");

            // Tenant data. Same predicate as CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE payment_receipts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE payment_receipts FORCE ROW LEVEL SECURITY;
                CREATE POLICY payment_receipts_shop_isolation ON payment_receipts
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);

                ALTER TABLE payment_allocations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE payment_allocations FORCE ROW LEVEL SECURITY;
                CREATE POLICY payment_allocations_shop_isolation ON payment_allocations
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);

                ALTER TABLE payment_reversals ENABLE ROW LEVEL SECURITY;
                ALTER TABLE payment_reversals FORCE ROW LEVEL SECURITY;
                CREATE POLICY payment_reversals_shop_isolation ON payment_reversals
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);

            // Legacy payments: one receipt per PAYMENT_RECEIVED / PAYMENT_MADE ledger entry, method
            // UNKNOWN_RECORDED_METHOD, no allocation (neither is inferred from history). Shop by shop,
            // because the deployed migrator is not BYPASSRLS. Same idempotent statement as
            // scripts/db/backfill-payment-receipts.sql, which closes the rolling-deploy gap afterwards.
            migrationBuilder.Sql("""
                DO $backfill$
                DECLARE
                    target_shop uuid;
                BEGIN
                    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
                        PERFORM set_config('app.active_shop_id', target_shop::text, true);

                        INSERT INTO payment_receipts (
                            id, shop_id, party_type, party_id, amount, method, bank_account_id, external_reference,
                            business_date, received_at, notes, ledger_entry_id, expense_id, is_legacy, created_by, created_at)
                        SELECT
                            gen_random_uuid(), e.shop_id, 'CUSTOMER', e.customer_id, e.amount, 'UNKNOWN_RECORDED_METHOD', NULL, NULL,
                            e.entry_date, e.created_at, e.notes, e.id, NULL, true, e.created_by, now()
                        FROM customer_ledger_entries e
                        WHERE e.shop_id = target_shop
                          AND e.entry_type = 'PAYMENT_RECEIVED'
                          AND e.amount > 0
                          AND NOT EXISTS (
                              SELECT 1 FROM payment_receipts r WHERE r.party_type = 'CUSTOMER' AND r.ledger_entry_id = e.id);

                        INSERT INTO payment_receipts (
                            id, shop_id, party_type, party_id, amount, method, bank_account_id, external_reference,
                            business_date, received_at, notes, ledger_entry_id, expense_id, is_legacy, created_by, created_at)
                        SELECT
                            gen_random_uuid(), e.shop_id, 'SUPPLIER', e.supplier_id, e.amount, 'UNKNOWN_RECORDED_METHOD', NULL, NULL,
                            e.entry_date, e.created_at, e.notes, e.id, x.id, true, e.created_by, now()
                        FROM supplier_ledger_entries e
                        LEFT JOIN LATERAL (
                            SELECT ex.id
                            FROM expenses ex
                            WHERE ex.supplier_ledger_entry_id = e.id AND ex.shop_id = e.shop_id
                            ORDER BY ex.created_at
                            LIMIT 1) x ON true
                        WHERE e.shop_id = target_shop
                          AND e.entry_type = 'PAYMENT_MADE'
                          AND e.amount > 0
                          AND NOT EXISTS (
                              SELECT 1 FROM payment_receipts r WHERE r.party_type = 'SUPPLIER' AND r.ledger_entry_id = e.id);
                    END LOOP;

                    PERFORM set_config('app.active_shop_id', '', true);
                END
                $backfill$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Receipts, allocations and reversals are retained payment facts: rolling back discards
            // them, and restoring the old ledger constraint fails while PAYMENT_REVERSAL entries
            // exist. Prefer disabling allocation and reversal over rolling back.
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS payment_reversals_shop_isolation ON payment_reversals;
                DROP POLICY IF EXISTS payment_allocations_shop_isolation ON payment_allocations;
                DROP POLICY IF EXISTS payment_receipts_shop_isolation ON payment_receipts;
                """);

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "payment_reversals");

            migrationBuilder.DropTable(
                name: "payment_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_ledger_entries_sale_by_type",
                table: "customer_ledger_entries",
                sql: "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type = 'PAYMENT_RECEIVED' AND sale_id IS NULL) OR entry_type IN ('RETURN_CREDIT', 'RETURN_CREDIT_REVERSAL'))");
        }
    }
}
