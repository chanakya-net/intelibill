#nullable disable

using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

/// <summary>
/// Extends the tenant boundary to the merchant data that was never covered:
/// stock, batches, stock movements, sales and returns, customers and suppliers
/// with their ledgers, expenses, invoice numbering, barcodes and bank
/// accounts.
/// </summary>
/// <remarks>
/// <para>
/// The consolidated initial migration created every one of these tables
/// without row-level security. Twelve tables added later did carry policies,
/// which left the merchant's most sensitive data — the money and the stock —
/// as the only part of the schema with no database boundary at all.
/// </para>
/// <para>
/// The predicate is the same one <c>HardenTenantPolicyContextParsing</c>
/// established: an absent context parses to NULL, so it admits no row and
/// permits no write instead of raising a cast error. Every table here already
/// carries an index leading with <c>shop_id</c>, which the predicate needs
/// because it runs on every row the statement touches.
/// </para>
/// <para>
/// The two line tables got their explicit <c>WITH CHECK</c> in that earlier
/// migration, so nothing is left to add for them here.
/// </para>
/// <para>
/// Three operations write or read a tenant table for a shop that is not the
/// request's active one — shop creation's system supplier, and shop bank
/// details read and written for a route-supplied shop. They acquire a
/// transaction-local scope through <c>ITenantWriteScope</c>, which must be in
/// place before this migration runs or they break.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261101130000_CompleteTenantRowLevelSecurity")]
public partial class CompleteTenantRowLevelSecurity : Migration
{
    private const string ShopPredicate =
        "shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid";

    private static readonly string[] Tables =
    [
        "bank_accounts",
        "customer_ledger_entries",
        "customers",
        "expense_categories",
        "expenses",
        "inventory",
        "inventory_adjustments",
        "inventory_batches",
        "invoice_leases",
        "invoice_sequences",
        "item_barcode_sequences",
        "items",
        "sale_items",
        "sale_return_items",
        "sale_returns",
        "sales",
        "stock_transactions",
        "supplier_ledger_entries",
        "suppliers",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS " + table + "_shop_isolation ON " + table + ";\n" +
                "ALTER TABLE " + table + " ENABLE ROW LEVEL SECURITY;\n" +
                "ALTER TABLE " + table + " FORCE ROW LEVEL SECURITY;\n" +
                "CREATE POLICY " + table + "_shop_isolation ON " + table + "\n" +
                "    USING (" + ShopPredicate + ")\n" +
                "    WITH CHECK (" + ShopPredicate + ");\n");
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS " + table + "_shop_isolation ON " + table + ";\n" +
                "ALTER TABLE " + table + " NO FORCE ROW LEVEL SECURITY;\n" +
                "ALTER TABLE " + table + " DISABLE ROW LEVEL SECURITY;\n");
        }
    }
}
