using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnAccountingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "accounting_policy_version",
                table: "sale_returns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accounting_review_reason",
                table: "sale_returns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accounting_status",
                table: "sale_returns",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_non_tax_amount",
                table: "sale_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_tax_amount",
                table: "sale_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_taxable_amount",
                table: "sale_returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_non_tax_amount",
                table: "sale_return_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_tax_amount",
                table: "sale_return_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "adjustment_taxable_amount",
                table: "sale_return_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sale_return_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_return_settlements", x => x.id);
                    table.CheckConstraint("ck_sale_return_settlements_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_sale_return_settlements_references_by_kind", "(kind = 'STORE_CREDIT_ISSUED' AND credit_note_id IS NOT NULL AND customer_ledger_entry_id IS NULL) OR (kind = 'RECEIVABLE_REDUCTION' AND credit_note_id IS NULL) OR (kind = 'EXTERNAL_REFUND' AND credit_note_id IS NULL AND customer_ledger_entry_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_sale_return_settlements_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_return_settlements_customer_ledger_entries_customer_le",
                        column: x => x.customer_ledger_entry_id,
                        principalTable: "customer_ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_return_settlements_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_return_settlements_sale_returns_sale_return_id",
                        column: x => x.sale_return_id,
                        principalTable: "sale_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sale_return_settlements_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sale_returns_shop_id_voided_at",
                table: "sale_returns",
                columns: new[] { "shop_id", "voided_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_returns_accounting_balanced",
                table: "sale_returns",
                sql: "accounting_status IS DISTINCT FROM 'BALANCED' OR (accounting_policy_version IS NOT NULL AND adjustment_taxable_amount IS NOT NULL AND adjustment_tax_amount IS NOT NULL AND adjustment_non_tax_amount IS NOT NULL AND adjustment_taxable_amount >= 0 AND adjustment_tax_amount >= 0 AND adjustment_non_tax_amount >= 0 AND adjustment_taxable_amount + adjustment_tax_amount + adjustment_non_tax_amount = total_refund_amount)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_returns_accounting_review_reason",
                table: "sale_returns",
                sql: "accounting_status IS DISTINCT FROM 'NEEDS_REVIEW' OR accounting_review_reason IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_return_items_adjustment_split",
                table: "sale_return_items",
                sql: "(adjustment_taxable_amount IS NULL AND adjustment_tax_amount IS NULL AND adjustment_non_tax_amount IS NULL) OR (adjustment_taxable_amount >= 0 AND adjustment_tax_amount >= 0 AND adjustment_non_tax_amount >= 0 AND adjustment_taxable_amount + adjustment_tax_amount + adjustment_non_tax_amount = approved_refund_amount)");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_settlements_credit_note_id",
                table: "sale_return_settlements",
                column: "credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_settlements_customer_id",
                table: "sale_return_settlements",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_settlements_customer_ledger_entry_id",
                table: "sale_return_settlements",
                column: "customer_ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_settlements_sale_return_id_sequence",
                table: "sale_return_settlements",
                columns: new[] { "sale_return_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_settlements_shop_id_sale_return_id",
                table: "sale_return_settlements",
                columns: new[] { "shop_id", "sale_return_id" });

            // Tenant data: the leading shop_id index serves the policy predicate. Same predicate as
            // CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE sale_return_settlements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE sale_return_settlements FORCE ROW LEVEL SECURITY;
                CREATE POLICY sale_return_settlements_shop_isolation ON sale_return_settlements
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);

            // Accounting snapshots for returns recorded before this migration. Each line is split by
            // proportional-line-tax/1 (recorded as backfill:proportional-line-tax/1); a return is
            // BALANCED only when every line is classifiable, its lines add up to its total and its
            // payout has evidence (a refund destination, or exactly one credit note of the payout
            // amount issued for it). Anything else is NEEDS_REVIEW with the reason: a settlement is
            // never inferred. Idempotent: scripts/db/backfill-sale-return-accounting.sql holds the
            // same statement, to re-run after the new API version is live for returns an old
            // instance recorded meanwhile.
            migrationBuilder.Sql("""
                WITH pending AS (
                    SELECT r.id, r.shop_id, r.return_number, r.total_refund_amount, r.due_reduction_amount,
                        r.payout_amount, r.payout_destination, s.id AS sale_id, s.customer_id, s.invoice_number
                    FROM sale_returns r
                    JOIN sales s ON s.id = r.sale_id
                    WHERE r.accounting_status IS NULL
                ),
                split AS (
                    SELECT i.id, i.sale_return_id, i.approved_refund_amount AS approved, share.supported,
                        share.within_share - share.tax AS taxable,
                        share.tax,
                        i.approved_refund_amount - share.within_share AS non_tax
                    FROM sale_return_items i
                    JOIN pending p ON p.id = i.sale_return_id
                    CROSS JOIN LATERAL (
                        SELECT
                            i.approved_refund_amount = 0
                                OR (i.max_refund_amount > 0 AND i.tax_amount >= 0 AND i.tax_amount <= i.max_refund_amount) AS supported,
                            LEAST(i.approved_refund_amount, i.max_refund_amount) AS within_share) basis
                    CROSS JOIN LATERAL (
                        SELECT basis.supported, basis.within_share,
                            CASE WHEN basis.supported AND i.approved_refund_amount > 0
                                THEN round(basis.within_share * i.tax_amount / i.max_refund_amount, 2)
                                ELSE 0
                            END AS tax) share
                ),
                totals AS (
                    SELECT x.sale_return_id, count(*) AS line_count, bool_and(x.supported) AS supported,
                        sum(x.approved) AS approved, sum(x.taxable) AS taxable, sum(x.tax) AS tax, sum(x.non_tax) AS non_tax
                    FROM split x
                    GROUP BY x.sale_return_id
                ),
                notes AS (
                    SELECT n.sale_return_id, count(*) AS matches, min(n.id::text)::uuid AS credit_note_id
                    FROM credit_notes n
                    JOIN pending p ON p.id = n.sale_return_id
                    WHERE n.original_amount = p.payout_amount
                    GROUP BY n.sale_return_id
                ),
                receivables AS (
                    SELECT p.id AS sale_return_id, count(*) AS matches, min(e.id::text)::uuid AS ledger_entry_id
                    FROM pending p
                    JOIN customer_ledger_entries e
                        ON e.sale_id = p.sale_id
                        AND e.entry_type = 'RETURN_CREDIT'
                        AND e.amount = p.due_reduction_amount
                        AND e.notes = 'Return credit from ' || p.return_number || ' for sale ' || p.invoice_number
                    WHERE p.due_reduction_amount > 0
                    GROUP BY p.id
                ),
                verdict AS (
                    SELECT p.*, t.taxable, t.tax, t.non_tax, n.credit_note_id,
                        CASE WHEN r.matches = 1 THEN r.ledger_entry_id END AS ledger_entry_id,
                        CASE
                            WHEN t.sale_return_id IS NULL THEN
                                'Backfill: the return has no lines to classify.'
                            WHEN NOT t.supported THEN
                                'Backfill: a returned line has no paid value or its tax is outside its gross, so its tax cannot be split.'
                            WHEN t.approved <> p.total_refund_amount THEN
                                'Backfill: the line refunds do not add up to the return''s total refund.'
                            WHEN p.payout_amount > 0 AND p.payout_destination IS NULL THEN
                                'Backfill: the payout has no recorded destination, so where the money went is not evidenced.'
                            WHEN p.payout_amount > 0 AND p.payout_destination = 1 AND COALESCE(n.matches, 0) <> 1 THEN
                                'Backfill: the payout went to store credit, but no single credit note of that amount was issued for the return.'
                            WHEN p.payout_amount > 0 AND p.payout_destination NOT IN (1, 2) THEN
                                'Backfill: the payout destination is not recognised.'
                        END AS review_reason
                    FROM pending p
                    LEFT JOIN totals t ON t.sale_return_id = p.id
                    LEFT JOIN notes n ON n.sale_return_id = p.id
                    LEFT JOIN receivables r ON r.sale_return_id = p.id
                ),
                line_postings AS (
                    UPDATE sale_return_items i
                    SET adjustment_taxable_amount = x.taxable,
                        adjustment_tax_amount = x.tax,
                        adjustment_non_tax_amount = x.non_tax
                    FROM split x
                    JOIN verdict v ON v.id = x.sale_return_id
                    WHERE i.id = x.id AND v.review_reason IS NULL
                    RETURNING i.id
                ),
                settlement_postings AS (
                    INSERT INTO sale_return_settlements (
                        id, shop_id, sale_return_id, sequence, kind, amount,
                        customer_id, credit_note_id, customer_ledger_entry_id, created_at)
                    SELECT gen_random_uuid(), v.shop_id, v.id, 1, 'RECEIVABLE_REDUCTION', v.due_reduction_amount,
                        v.customer_id, NULL, v.ledger_entry_id, now()
                    FROM verdict v
                    WHERE v.review_reason IS NULL AND v.due_reduction_amount > 0
                    UNION ALL
                    SELECT gen_random_uuid(), v.shop_id, v.id,
                        CASE WHEN v.due_reduction_amount > 0 THEN 2 ELSE 1 END,
                        CASE WHEN v.payout_destination = 1 THEN 'STORE_CREDIT_ISSUED' ELSE 'EXTERNAL_REFUND' END,
                        v.payout_amount,
                        v.customer_id,
                        CASE WHEN v.payout_destination = 1 THEN v.credit_note_id END,
                        NULL,
                        now()
                    FROM verdict v
                    WHERE v.review_reason IS NULL AND v.payout_amount > 0
                    RETURNING sale_return_id
                )
                UPDATE sale_returns r
                SET accounting_status = CASE WHEN v.review_reason IS NULL THEN 'BALANCED' ELSE 'NEEDS_REVIEW' END,
                    accounting_policy_version = 'backfill:proportional-line-tax/1',
                    accounting_review_reason = v.review_reason,
                    adjustment_taxable_amount = CASE WHEN v.review_reason IS NULL THEN v.taxable END,
                    adjustment_tax_amount = CASE WHEN v.review_reason IS NULL THEN v.tax END,
                    adjustment_non_tax_amount = CASE WHEN v.review_reason IS NULL THEN v.non_tax END
                FROM verdict v
                WHERE r.id = v.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back discards every return's accounting snapshot and settlement legs; returns
            // then export as needing review. Prefer refusing Tally return export over rolling back.
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS sale_return_settlements_shop_isolation ON sale_return_settlements;
                """);

            migrationBuilder.DropTable(
                name: "sale_return_settlements");

            migrationBuilder.DropIndex(
                name: "ix_sale_returns_shop_id_voided_at",
                table: "sale_returns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_returns_accounting_balanced",
                table: "sale_returns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_returns_accounting_review_reason",
                table: "sale_returns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_return_items_adjustment_split",
                table: "sale_return_items");

            migrationBuilder.DropColumn(
                name: "accounting_policy_version",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "accounting_review_reason",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "accounting_status",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "adjustment_non_tax_amount",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "adjustment_tax_amount",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "adjustment_taxable_amount",
                table: "sale_returns");

            migrationBuilder.DropColumn(
                name: "adjustment_non_tax_amount",
                table: "sale_return_items");

            migrationBuilder.DropColumn(
                name: "adjustment_tax_amount",
                table: "sale_return_items");

            migrationBuilder.DropColumn(
                name: "adjustment_taxable_amount",
                table: "sale_return_items");
        }
    }
}
