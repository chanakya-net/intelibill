using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0407LicensingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreExpiry_order_cannot_receive_at_deadline(bool keyed)
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var deadline = _clock.Now.AddHours(1);
        await ApplyAsync(await BindAsync(shop), deadline);
        await EnforceAsync(shop);
        var (order, line) = await PlaceOrderAsync(client);

        _clock.Now = deadline.AddMilliseconds(-1);
        var path = $"/api/purchase-orders/{order}/receipts";
        await ReadSuccessAsync(await client.PostAsJsonAsync(path, Receipt(line, "FIRST", 4, Guid.NewGuid(), _clock.Now), Ct));
        var before = await ReadFactsAsync(shop, order);

        _clock.Now = deadline;
        using var response = await client.PostAsJsonAsync(path,
            Receipt(line, "LATE", 6, keyed ? Guid.NewGuid() : null, deadline.AddMinutes(-10)), Ct);
        var after = await ReadFactsAsync(shop, order);
        _output.WriteLine($"Keyed={keyed}, server deadline={deadline:O}, HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        _output.WriteLine($"PostgreSQL before: {before}\nPostgreSQL after: {after}");
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Restricted_shop_cannot_create_edit_or_place_purchase_orders()
    {
        using var host = CreateHost();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var deadline = _clock.Now.AddHours(1);
        await ApplyAsync(await BindAsync(shop), deadline);
        await EnforceAsync(shop);
        var input = await DraftInputAsync(client);
        var draft = await CreateDraftAsync(client, input);
        var id = draft.GetProperty("purchaseOrderId").GetGuid();
        var before = await ReadFactsAsync(shop, id);

        _clock.Now = deadline;
        var path = $"/api/purchase-orders/{id}";
        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/purchase-orders", input, Ct),
            await client.PutAsJsonAsync(path, input with { Notes = "Forbidden edit" }, Ct),
            await client.PostAsync($"{path}/place", null, Ct),
        };
        foreach (var response in responses)
            _output.WriteLine($"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        foreach (var response in responses)
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, await ReadFactsAsync(shop, id));
    }

    private static async Task<DraftInput> DraftInputAsync(HttpClient client)
    {
        var item = await ReadSuccessAsync(await client.PostAsJsonAsync("/api/items", new
        {
            name = "Fixture item", barcode = $"PO-{Guid.NewGuid():N}", uom = "pcs", isActive = true,
        }, Ct));
        var supplier = await ReadSuccessAsync(await client.PostAsJsonAsync("/api/suppliers", new
        {
            name = "Fixture supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
            pin = "560001", isActive = true,
        }, Ct));
        return new(supplier.GetProperty("supplierId").GetGuid(), "Original draft",
            [new(item.GetProperty("id").GetGuid(), "Fixture item", 10, 10)]);
    }

    private static async Task<JsonElement> CreateDraftAsync(HttpClient client, DraftInput input)
    {
        using var response = await client.PostAsJsonAsync("/api/purchase-orders", input, Ct);
        return await ReadSuccessAsync(response);
    }

    private static object Receipt(Guid line, string batch, int quantity, Guid? operationId, DateTimeOffset receivedAt) =>
        new { operationId, receivedAt, lines = new[] { ReceiptLine(line, batch, quantity) } };

    private static object ReceiptLine(Guid line, string batch, int quantity) => new
    {
        purchaseOrderLineId = line, batchNumber = batch, barcode = $"PO-{line:N}",
        quantity, totalPurchaseCost = 10m * quantity, mrp = 12m, salesPrice = 11m,
        taxRatePercent = 5m, taxIncluded = false, purchaseTaxIncluded = false,
    };

    private async Task<string> ReadFactsAsync(Guid shop, Guid order)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'orders', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM purchase_orders t WHERE shop_id = $1),
              'lines', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM purchase_order_lines t WHERE purchase_order_id = $2),
              'receipts', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM purchase_order_receipts t WHERE purchase_order_id = $2),
              'receiptLines', (SELECT jsonb_agg(to_jsonb(l) ORDER BY l.id) FROM purchase_order_receipt_lines l
                JOIN purchase_order_receipts r ON r.id = l.purchase_order_receipt_id WHERE r.purchase_order_id = $2),
              'inventory', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM inventory t WHERE shop_id = $1),
              'batches', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM inventory_batches t WHERE shop_id = $1),
              'movements', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM stock_transactions t WHERE shop_id = $1),
              'payables', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM supplier_ledger_entries t WHERE shop_id = $1),
              'values', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM purchase_receipt_values t WHERE shop_id = $1),
              'operations', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.id) FROM operation_receipts t WHERE shop_id = $1),
              'poSequences', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.year) FROM purchase_order_sequences t WHERE shop_id = $1),
              'receiptSequences', (SELECT jsonb_agg(to_jsonb(t) ORDER BY t.year) FROM purchase_order_receipt_sequences t WHERE shop_id = $1)
            )::text
            """, connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = shop });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = order });
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    private sealed record DraftInput(Guid SupplierId, string Notes, DraftLine[] Lines);
    private sealed record DraftLine(Guid ItemId, string Description, int ExpectedQuantity, decimal UnitCost);
}
