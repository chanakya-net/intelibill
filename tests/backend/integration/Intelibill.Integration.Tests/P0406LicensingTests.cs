using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0406LicensingTests
{
    private const string Inbound = "/api/inventory/inbound";
    private const string Bulk = Inbound + "/batch";

    [Fact]
    public async Task Single_intake_uses_current_access_after_a_write_wait()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, _, end) = await OpenIntakeAsync(client);
        var seed = Row("A", _clock.Now.AddDays(-1));
        _clock.Now = end.AddTicks(-10);
        await PostAsync(client, Inbound, seed);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "inventory_batches", shop);
        var before = await FactsAsync(shop);
        var posting = client.PostAsJsonAsync(Inbound, Row("B", seed.PerformedAt), Ct);
        await CrossDeadlineAsync(barrier, end);
        using var response = await posting;
        var after = await FactsAsync(shop);
        _output.WriteLine("Single blocked save at {0:O}: HTTP {1}; before={2}; after={3}", end, (int)response.StatusCode, before, after);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("reserve-keyed")]
    [InlineData("complete-keyed")]
    public async Task Partial_intake_crosses_deadline_without_posting_row_B(string stage)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, binding, end) = await OpenIntakeAsync(client);
        var supplierId = (await PostAsync(client, "/api/suppliers", new
        {
            name = "Stock supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
            pin = "560001", isActive = true, isPreferred = false,
        })).GetProperty("supplierId").GetGuid();
        var rows = new[] { Row("A", _clock.Now.AddDays(-1), supplierId), Row("B", _clock.Now.AddDays(-1), supplierId) };
        var request = new { operationKey = "two-rows", items = rows };
        await using var barrier = await PauseSecondRowAsync(shop, stage);
        var posting = client.PostAsJsonAsync(Bulk, request, Ct);
        var waiter = await barrier.WaitForArrivalAsync();
        try
        {
            Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 1, 2), await FactsAsync(shop));
            _clock.Now = end;
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        using var response = await posting;
        var after = await FactsAsync(shop);
        _output.WriteLine("PostgreSQL waiter {0} blocked by {1} during {2}; HTTP {3}; facts={4}",
            waiter, barrier.BlockerPid, stage, (int)response.StatusCode, after);
        var original = await AssertPartialAsync(response, "Licence.Expired");
        Assert.Equal(new StockFacts(1, 1, 2, 1, 1, 1, 2), after);
        await AssertRecoveryAsync(client, binding, rows, original);
    }

    private Task<TillPostgresBarrier> PauseSecondRowAsync(Guid shop, string stage)
    {
        const string rowReceipt = "NEW.operation_type = 4 AND NEW.operation_key = '8:two-rows:B'";
        return stage switch
        {
            "reserve-keyed" => TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop, rowReceipt, "BEFORE INSERT"),
            _ => TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop, rowReceipt, "AFTER UPDATE"),
        };
    }

    private async Task AssertRecoveryAsync(HttpClient client, Binding binding, IntakeRow[] rows, JsonElement original)
    {
        var request = new { operationKey = "two-rows", items = rows };
        using var expired = await client.PostAsJsonAsync(Bulk, request, Ct);
        Assert.True(JsonElement.DeepEquals(original, await AssertPartialAsync(expired, "Licence.Expired")));
        await AssertProblemAsync(await client.PostAsJsonAsync(Bulk, request with { operationKey = "fresh" }, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading");
        using var suspended = await client.PostAsJsonAsync(Bulk, request, Ct);
        Assert.True(JsonElement.DeepEquals(original, await AssertPartialAsync(suspended, "Licence.TradingRestricted")));
        await AssertProblemAsync(await client.PostAsJsonAsync(Bulk, request with { items = rows.Reverse().ToArray() }, Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        await ApplyAsync(binding, _clock.Now.AddDays(1), 3, releasedRevision: 2);
        using var resumed = await client.PostAsJsonAsync(Bulk, request, Ct);
        var complete = await ReadSuccessAsync(resumed);
        Assert.Equal(2, complete.GetProperty("successCount").GetInt32());
        Assert.True(JsonElement.DeepEquals(original, complete.GetProperty("succeeded")[0]));
        Assert.Equal(new StockFacts(2, 2, 4, 2, 2, 2, 3), await FactsAsync(binding.Shop));
        await ApplyAsync(binding, _clock.Now.AddDays(1), 4, restriction: "Security");
        await AssertProblemAsync(await client.PostAsJsonAsync(Bulk, request, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await client.PostAsJsonAsync(Bulk, request with { items = rows.Reverse().ToArray() }, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
    }

    private async Task CrossDeadlineAsync(TillPostgresBarrier barrier, DateTimeOffset end)
    {
        var waiter = await barrier.WaitForArrivalAsync();
        try
        {
            _output.WriteLine("PostgreSQL waiter {0}; clock advances to {1:O}", waiter, end);
            _clock.Now = end;
        }
        finally
        {
            await barrier.ReleaseAsync();
        }
    }

    private static async Task<JsonElement> AssertPartialAsync(HttpResponseMessage response, string code)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.MultiStatus, $"Expected 207 with {code}, received {(int)response.StatusCode}: {body}");
        var result = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(1, result.GetProperty("successCount").GetInt32());
        Assert.Equal(1, result.GetProperty("failedCount").GetInt32());
        var committed = result.GetProperty("succeeded")[0];
        Assert.Equal("A", committed.GetProperty("clientRowId").GetString());
        Assert.NotEqual(Guid.Empty, committed.GetProperty("result").GetProperty("inventoryBatchId").GetGuid());
        Assert.NotEqual(Guid.Empty, committed.GetProperty("result").GetProperty("stockTransactionId").GetGuid());
        var rejected = result.GetProperty("failed")[0];
        Assert.Equal("B", rejected.GetProperty("clientRowId").GetString());
        Assert.Equal(code, rejected.GetProperty("errors")[0].GetProperty("code").GetString());
        return committed;
    }

    private async Task<(Guid Shop, Binding Binding, DateTimeOffset End)> OpenIntakeAsync(HttpClient client)
    {
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        return (shop, binding, end);
    }

    private async Task<StockFacts> FactsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return new(
            await db.Items.CountAsync(x => x.ShopId == shop, Ct),
            await db.InventoryBatches.CountAsync(x => x.ShopId == shop, Ct),
            await db.Inventory.Where(x => x.ShopId == shop).SumAsync(x => x.Quantity, Ct),
            await db.StockTransactions.CountAsync(x => x.ShopId == shop, Ct),
            await db.PurchaseReceiptValues.CountAsync(x => x.ShopId == shop, Ct),
            await db.SupplierLedgerEntries.CountAsync(x => x.ShopId == shop, Ct),
            await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct));
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string route, object body)
    {
        using var response = await client.PostAsJsonAsync(route, body, Ct);
        return await ReadSuccessAsync(response);
    }

    private static IntakeRow Row(string id, DateTimeOffset performedAt, Guid? supplierId = null) =>
        new(id, $"Goods {id}", id, id, "pcs", 2, supplierId is null ? 0 : 20, 20, 16, performedAt, supplierId);

    private sealed record IntakeRow(string ClientRowId, string ItemName, string Barcode, string BatchNumber,
        string Uom, decimal Quantity, decimal TotalPurchaseCost, decimal Mrp, decimal SalesPrice,
        DateTimeOffset PerformedAt, Guid? SupplierId);
    private sealed record StockFacts(int Items, int Batches, decimal Quantity, int Movements, int Receipts, int Payables, int Operations);
}
