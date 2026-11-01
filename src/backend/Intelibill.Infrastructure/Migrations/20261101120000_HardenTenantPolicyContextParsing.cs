#nullable disable

using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

/// <summary>
/// Rewrites every policy that already existed so an absent shop context is a
/// safe answer rather than an error, and states the write side explicitly.
/// </summary>
/// <remarks>
/// <para>
/// <c>PostgresSessionContextInterceptor</c> writes an empty string into
/// <c>app.active_shop_id</c> whenever the request has no active shop — an
/// account between enrolling and creating its first shop, and every request
/// while token validation runs. The original predicate cast that value
/// straight to <c>uuid</c>, which raises SQLSTATE 22P02 instead of matching no
/// rows, so the restricted runtime role met a cast error where it should have
/// seen nothing. <c>NULLIF(…, '')</c> turns the absent context into NULL, and a
/// NULL comparison admits no row and permits no write.
/// </para>
/// <para>
/// The write clause was previously left implicit: PostgreSQL falls back to the
/// <c>USING</c> expression when <c>WITH CHECK</c> is omitted, so inserts were
/// constrained, but only by inheritance. Stating both clauses makes the write
/// boundary reviewable in <c>pg_policy</c> instead of inferred from a default.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261101120000_HardenTenantPolicyContextParsing")]
public partial class HardenTenantPolicyContextParsing : Migration
{
    private const string ShopPredicate =
        "shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid";

    private const string LegacyShopPredicate =
        "shop_id = current_setting('app.active_shop_id', true)::uuid";

    /// <summary>Tables that carry their own <c>shop_id</c> and already had a policy.</summary>
    private static readonly string[] DirectTables =
    [
        "credit_note_redemptions",
        "credit_notes",
        "discount_rules",
        "purchase_order_receipt_sequences",
        "purchase_order_receipts",
        "purchase_order_sequences",
        "purchase_orders",
        "reconciliation_issues",
        "service_code_sequences",
        "services",
    ];

    /// <summary>Line tables, which derive their shop from the parent they hang off.</summary>
    private static readonly (string Table, string ParentColumn, string ParentTable)[] ParentDerivedTables =
    [
        ("purchase_order_lines", "purchase_order_id", "purchase_orders"),
        ("purchase_order_receipt_lines", "purchase_order_receipt_id", "purchase_order_receipts"),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in DirectTables)
        {
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS " + table + "_shop_isolation ON " + table + ";\n" +
                "ALTER TABLE " + table + " ENABLE ROW LEVEL SECURITY;\n" +
                "ALTER TABLE " + table + " FORCE ROW LEVEL SECURITY;\n" +
                "CREATE POLICY " + table + "_shop_isolation ON " + table + "\n" +
                "    USING (" + ShopPredicate + ")\n" +
                "    WITH CHECK (" + ShopPredicate + ");\n");
        }

        foreach (var (table, parentColumn, parentTable) in ParentDerivedTables)
        {
            var predicate = ParentPredicate(parentColumn, parentTable, ShopPredicate);

            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS " + table + "_shop_isolation ON " + table + ";\n" +
                "ALTER TABLE " + table + " ENABLE ROW LEVEL SECURITY;\n" +
                "ALTER TABLE " + table + " FORCE ROW LEVEL SECURITY;\n" +
                "CREATE POLICY " + table + "_shop_isolation ON " + table + "\n" +
                "    USING (" + predicate + ")\n" +
                "    WITH CHECK (" + predicate + ");\n");
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in DirectTables)
        {
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS " + table + "_shop_isolation ON " + table + ";\n" +
                "CREATE POLICY " + table + "_shop_isolation ON " + table + "\n" +
                "    USING (" + LegacyShopPredicate + ");\n");
        }

        foreach (var (table, parentColumn, parentTable) in ParentDerivedTables)
        {
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS " + table + "_shop_isolation ON " + table + ";\n" +
                "CREATE POLICY " + table + "_shop_isolation ON " + table + "\n" +
                "    USING (" + ParentPredicate(parentColumn, parentTable, LegacyShopPredicate) + ");\n");
        }
    }

    private static string ParentPredicate(string parentColumn, string parentTable, string shopPredicate) =>
        parentColumn + " IN (SELECT id FROM " + parentTable + " WHERE " + shopPredicate + ")";
}
