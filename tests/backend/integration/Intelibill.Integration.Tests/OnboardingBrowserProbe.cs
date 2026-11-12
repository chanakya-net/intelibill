using System.Text.Json;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>Read-only inspection of the isolated onboarding fixture's captured mail and posting facts.</summary>
public static class OnboardingBrowserProbe
{
    public static async Task<object> ReadEnrollmentAsync(ApiWebApplicationFactory factory, string email)
    {
        var (challengeId, token) = await IdentityTestMailbox.ReadAsync(factory, email);
        return new { challengeId, token };
    }

    public static async Task<JsonElement> ReadSnapshotAsync(string connectionString, Guid shop)
    {
        const string sql = """
            SELECT jsonb_build_object(
              'sales', (SELECT count(*) FROM sales WHERE shop_id = @shop),
              'saleDocuments', (SELECT jsonb_agg(jsonb_build_object('number', invoice_number, 'document', invoice_document) ORDER BY id)
                  FROM sales WHERE shop_id = @shop),
              'saleItems', (SELECT count(*) FROM sale_items WHERE shop_id = @shop),
              'sequences', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM invoice_sequences t WHERE shop_id = @shop),
              'leases', (SELECT count(*) FROM invoice_leases WHERE shop_id = @shop),
              'stockTransactions', (SELECT count(*) FROM stock_transactions WHERE shop_id = @shop),
              'batches', (SELECT jsonb_agg(jsonb_build_array(id, quantity, quarantined_quantity) ORDER BY id) FROM inventory_batches WHERE shop_id = @shop),
              'aggregate', (SELECT jsonb_agg(jsonb_build_array(id, quantity) ORDER BY id) FROM inventory WHERE shop_id = @shop),
              'customerLedger', (SELECT count(*) FROM customer_ledger_entries WHERE shop_id = @shop),
              'supplierLedger', (SELECT count(*) FROM supplier_ledger_entries WHERE shop_id = @shop),
              'purchaseReceipts', (SELECT count(*) FROM purchase_receipt_values WHERE shop_id = @shop),
              'payments', (SELECT count(*) FROM payment_receipts WHERE shop_id = @shop),
              'allocations', (SELECT count(*) FROM payment_allocations WHERE shop_id = @shop),
              'till', (SELECT count(*) FROM till_movements WHERE shop_id = @shop),
              'operations', (SELECT count(*) FROM operation_receipts WHERE shop_id = @shop),
              'preferences', (SELECT to_jsonb(t) FROM shop_setup_preferences t WHERE shop_id = @shop),
              'mail', (SELECT count(*) FROM identity_mail),
              'sms', (SELECT count(*) FROM identity_sms)
            )::text
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("shop", shop);
        var json = (string)(await command.ExecuteScalarAsync())!;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
