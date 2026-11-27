using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0406LicensingTests
{
    private const int StockCountOperation = 5;

    [Fact]
    public async Task Keyed_count_gain_rechecks_access_after_receipt_completion()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (shop, _, end) = await OpenIntakeAsync(client);
        var seeded = await PostAsync(client, Inbound, Row("A", _clock.Now.AddDays(-1)));
        var batch = seeded.GetProperty("inventoryBatchId").GetGuid();
        var route = $"/api/inventory/batches/{batch}/count";
        var committedRequest = CountRequest(await BatchVersionAsync(batch), 3, "count-committed");
        var committed = await PostAsync(client, route, committedRequest);
        var version = await BatchVersionAsync(batch);
        var before = await CountFactsAsync(shop, batch);
        Assert.Equal(new CountFacts(1, 2, 1, 3, 3, 1), before);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "operation_receipts", shop,
            $"NEW.operation_type = {StockCountOperation} AND NEW.operation_key = 'count-late'", "AFTER UPDATE");
        var posting = client.PostAsJsonAsync(route, CountRequest(version, 5, "count-late"), Ct);
        await CrossDeadlineAsync(barrier, end);

        using var response = await posting;
        var after = await CountFactsAsync(shop, batch);
        _output.WriteLine("Keyed count held at receipt completion until {0:O}: HTTP {1}; before={2}; after={3}",
            end, (int)response.StatusCode, before, after);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, after);
        Assert.Equal(version, await BatchVersionAsync(batch));

        using var recovered = await client.PostAsJsonAsync(route, committedRequest, Ct);
        Assert.True(JsonElement.DeepEquals(committed, await ReadSuccessAsync(recovered)));
        await AssertProblemAsync(await client.PostAsJsonAsync(route, CountRequest(version, 5, "count-fresh"), Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, await CountFactsAsync(shop, batch));
        _output.WriteLine("Committed count {0} replayed after expiry; a fresh key was refused.", committed.GetProperty("stockCountId"));
    }

    private static object CountRequest(uint expectedVersion, decimal countedQuantity, string operationKey) =>
        new { expectedVersion, countedQuantity, reason = "Fixture recount", operationKey };

    private async Task<uint> BatchVersionAsync(Guid batch)
    {
        await using var db = Open(Migrator);
        return await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Version).SingleAsync(Ct);
    }

    private async Task<CountFacts> CountFactsAsync(Guid shop, Guid batch)
    {
        await using var db = Open(Migrator);
        return new(
            await db.StockCounts.CountAsync(x => x.ShopId == shop, Ct),
            await db.StockTransactions.CountAsync(x => x.ShopId == shop, Ct),
            await db.InventoryAdjustments.CountAsync(x => x.ShopId == shop, Ct),
            await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Quantity).SingleAsync(Ct),
            await db.Inventory.Where(x => x.ShopId == shop).SumAsync(x => x.Quantity, Ct),
            await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct));
    }

    private sealed record CountFacts(int Counts, int Movements, int Adjustments, decimal BatchQuantity, decimal ShopQuantity, int Operations);
}
