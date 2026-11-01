using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests.Features.Inventory;

/// <summary>
/// Proves against PostgreSQL that each bulk-intake row commits completely or not at all, and that
/// resending a keyed request never posts a committed row a second time.
/// </summary>
/// <remarks>
/// Every assertion about stored rows reads through a separate, fresh context so it sees what was
/// committed, never what a request still had tracked.
/// </remarks>
[Collection("Integration Tests")]
public sealed class BulkIntakeAtomicityTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    // Disarmed unless a test names the batches whose first save should fail.
    private static readonly FailingBatchInterceptor Fault = new();

    private readonly ApiWebApplicationFactory _factory = new(
        fixture,
        services => services.AddSingleton<IInterceptor>(Fault));

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ValidThenInvalidSupplier_LeavesNoFailedItem()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var validBarcode = Barcode();
        var failedBarcode = Barcode();

        var response = await PostBatchAsync(client, ownerToken, null,
            Row("row-1", validBarcode, "B-1", 4m, 400m),
            Row("row-2", failedBarcode, "B-2", 3m, 300m, supplierId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("row-1", body.GetProperty("succeeded")[0].GetProperty("clientRowId").GetString());
        Assert.Equal("Supplier.SupplierNotFound", body.GetProperty("failed")[0].GetProperty("errors")[0].GetProperty("code").GetString());

        await AssertCompleteReceiptAsync(shopId, validBarcode, 4m, 400m);
        await AssertNoTraceAsync(shopId, failedBarcode);
    }

    [Fact]
    public async Task InvalidSupplierThenValid_LeavesNoFailedItem()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var failedBarcode = Barcode();
        var validBarcode = Barcode();

        var response = await PostBatchAsync(client, ownerToken, null,
            Row("row-1", failedBarcode, "B-1", 3m, 300m, supplierId: Guid.NewGuid()),
            Row("row-2", validBarcode, "B-2", 4m, 400m));

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        await AssertCompleteReceiptAsync(shopId, validBarcode, 4m, 400m);
        await AssertNoTraceAsync(shopId, failedBarcode);
    }

    [Fact]
    public async Task ZeroCostReceipt_HasStockWithoutPayable()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var barcode = Barcode();

        var response = await PostBatchAsync(client, ownerToken, null, Row("row-1", barcode, "FREE-1", 5m, 0m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.Items.SingleAsync(i => i.ShopId == shopId && i.Barcode == barcode);
        var batch = await db.InventoryBatches.SingleAsync(b => b.ItemId == item.Id);
        Assert.Equal(5m, batch.Quantity);
        Assert.Equal(0m, batch.CostPrice);
        Assert.Equal(5m, (await db.Inventory.SingleAsync(i => i.ItemId == item.Id)).Quantity);
        var movement = await db.StockTransactions.SingleAsync(t => t.InventoryBatchId == batch.Id);
        Assert.Equal(StockTransactionType.In, movement.TransactionType);
        Assert.Equal(5m, movement.Quantity);
        Assert.False(await db.SupplierLedgerEntries.AnyAsync(e => e.BatchId == batch.Id));
    }

    [Fact]
    public async Task ZeroCostBatch_CanBeVoidedWithoutPayableReversal()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var barcode = Barcode();

        var response = await PostBatchAsync(client, ownerToken, null, Row("row-1", barcode, "FREE-1", 5m, 0m));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = body.GetProperty("succeeded")[0].GetProperty("result").GetProperty("inventoryBatchId").GetGuid();

        using var voidRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{batchId}/void");
        voidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var voidResponse = await client.SendAsync(voidRequest);

        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.SupplierLedgerEntries.AnyAsync(e => e.ShopId == shopId && e.BatchId == batchId));
        Assert.Equal(0m, (await db.Inventory.SingleAsync(i => i.ShopId == shopId)).Quantity);
    }

    [Fact]
    public async Task DuplicateBatchWithinRequest_OnlyOneReceipt()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var barcode = Barcode();

        var response = await PostBatchAsync(client, ownerToken, OperationKey(),
            Row("row-1", barcode, "DUP-1", 4m, 400m),
            Row("row-2", $"  {barcode} ", " DUP-1 ", 6m, 600m));

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("row-1", body.GetProperty("succeeded")[0].GetProperty("clientRowId").GetString());
        var failed = body.GetProperty("failed")[0];
        Assert.Equal("row-2", failed.GetProperty("clientRowId").GetString());
        Assert.Equal("Inventory.BatchNumberAlreadyExists", failed.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.False(failed.GetProperty("retryable").GetBoolean());

        await AssertCompleteReceiptAsync(shopId, barcode, 4m, 400m);
    }

    [Fact]
    public async Task ResponseLoss_ResumesOnlyUnfinishedRows()
    {
        Fault.Arm("LOST-2", "LOST-3");
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var key = OperationKey();
        var barcodes = new[] { Barcode(), Barcode(), Barcode() };
        object[] rows =
        [
            Row("row-1", barcodes[0], "LOST-1", 1m, 100m),
            Row("row-2", barcodes[1], "LOST-2", 2m, 200m),
            Row("row-3", barcodes[2], "LOST-3", 3m, 300m),
        ];

        // The first attempt commits row 1 and loses rows 2 and 3 to a storage fault: the
        // situation a dropped connection leaves behind.
        var first = await PostBatchAsync(client, ownerToken, key, rows);
        Assert.Equal(HttpStatusCode.MultiStatus, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstRow1 = firstBody.GetProperty("succeeded")[0];
        Assert.Equal("row-1", firstRow1.GetProperty("clientRowId").GetString());
        Assert.Equal("row-2,row-3", string.Join(',', firstBody.GetProperty("failed").EnumerateArray()
            .Select(r => r.GetProperty("clientRowId").GetString())));

        var resend = await PostBatchAsync(client, ownerToken, key, rows);

        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var resendBody = await resend.Content.ReadFromJsonAsync<JsonElement>();
        var succeeded = resendBody.GetProperty("succeeded").EnumerateArray().ToArray();
        Assert.Equal("row-1,row-2,row-3", string.Join(',', succeeded.Select(r => r.GetProperty("clientRowId").GetString())));
        Assert.Equal(
            firstRow1.GetProperty("result").GetProperty("inventoryBatchId").GetGuid(),
            succeeded[0].GetProperty("result").GetProperty("inventoryBatchId").GetGuid());
        Assert.Equal(
            firstRow1.GetProperty("result").GetProperty("stockTransactionId").GetGuid(),
            succeeded[0].GetProperty("result").GetProperty("stockTransactionId").GetGuid());

        await AssertCompleteReceiptAsync(shopId, barcodes[0], 1m, 100m);
        await AssertCompleteReceiptAsync(shopId, barcodes[1], 2m, 200m);
        await AssertCompleteReceiptAsync(shopId, barcodes[2], 3m, 300m);

        // A third copy of the same request changes nothing at all.
        var third = await PostBatchAsync(client, ownerToken, key, rows);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        await AssertCompleteReceiptAsync(shopId, barcodes[0], 1m, 100m);
        await AssertCompleteReceiptAsync(shopId, barcodes[2], 3m, 300m);
    }

    [Fact]
    public async Task ChangedRowOnReplay_Conflicts()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var key = OperationKey();
        var barcode = Barcode();
        var otherBarcode = Barcode();

        var first = await PostBatchAsync(client, ownerToken, key, Row("row-1", barcode, "CHG-1", 4m, 400m));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var changed = await PostBatchAsync(client, ownerToken, key, Row("row-1", barcode, "CHG-1", 5m, 400m));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var problem = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Operation.PayloadMismatch", problem.GetProperty("title").GetString());

        // An added row is a changed manifest too: it never runs under the old key.
        var extended = await PostBatchAsync(client, ownerToken, key,
            Row("row-1", barcode, "CHG-1", 4m, 400m),
            Row("row-2", otherBarcode, "CHG-2", 1m, 100m));
        Assert.Equal(HttpStatusCode.Conflict, extended.StatusCode);

        await AssertCompleteReceiptAsync(shopId, barcode, 4m, 400m);
        await AssertNoTraceAsync(shopId, otherBarcode);
    }

    [Fact]
    public async Task DuplicateClientRowId_RejectsBeforeProcessing()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var firstBarcode = Barcode();
        var secondBarcode = Barcode();

        var response = await PostBatchAsync(client, ownerToken, OperationKey(),
            Row("row-1", firstBarcode, "DUPID-1", 1m, 100m),
            Row("row-1", secondBarcode, "DUPID-2", 1m, 100m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Inventory.DuplicateClientRowId", problem.GetProperty("title").GetString());
        await AssertNoTraceAsync(shopId, firstBarcode);
        await AssertNoTraceAsync(shopId, secondBarcode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.OperationReceipts.AnyAsync(r => r.ShopId == shopId));
    }

    [Fact]
    public async Task TransientFailure_RemainsRetryable()
    {
        Fault.Arm("FAULT-1");
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var faultedBarcode = Barcode();
        var laterBarcode = Barcode();
        var key = OperationKey();
        object[] rows =
        [
            Row("row-1", faultedBarcode, "FAULT-1", 2m, 200m),
            Row("row-2", laterBarcode, "OK-2", 3m, 300m),
        ];

        var response = await PostBatchAsync(client, ownerToken, key, rows);

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var failed = body.GetProperty("failed")[0];
        Assert.Equal("row-1", failed.GetProperty("clientRowId").GetString());
        Assert.True(failed.GetProperty("retryable").GetBoolean());
        Assert.Equal("Inventory.RowRetryable", failed.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("row-2", body.GetProperty("succeeded")[0].GetProperty("clientRowId").GetString());

        await AssertNoTraceAsync(shopId, faultedBarcode);
        await AssertCompleteReceiptAsync(shopId, laterBarcode, 3m, 300m);

        var retry = await PostBatchAsync(client, ownerToken, key, rows);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await AssertCompleteReceiptAsync(shopId, faultedBarcode, 2m, 200m);
        await AssertCompleteReceiptAsync(shopId, laterBarcode, 3m, 300m);
    }

    [Fact]
    public async Task ConcurrentResend_PostsEachRowOnce()
    {
        using var client = CreateClient(_factory);
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var key = OperationKey();
        var barcodes = new[] { Barcode(), Barcode() };
        object[] rows =
        [
            Row("row-1", barcodes[0], "RACE-1", 1m, 100m),
            Row("row-2", barcodes[1], "RACE-2", 2m, 200m),
        ];

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            using var parallelClient = CreateClient(_factory);
            return await PostBatchAsync(parallelClient, ownerToken, key, rows);
        })));

        var batchIds = new HashSet<Guid>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            batchIds.Add(body.GetProperty("succeeded")[0].GetProperty("result").GetProperty("inventoryBatchId").GetGuid());
        }

        Assert.Single(batchIds);
        await AssertCompleteReceiptAsync(shopId, barcodes[0], 1m, 100m);
        await AssertCompleteReceiptAsync(shopId, barcodes[1], 2m, 200m);
    }

    private async Task AssertCompleteReceiptAsync(Guid shopId, string barcode, decimal quantity, decimal cost)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var item = await db.Items.SingleAsync(i => i.ShopId == shopId && i.Barcode == barcode);
        var batch = await db.InventoryBatches.SingleAsync(b => b.ShopId == shopId && b.ItemId == item.Id);
        Assert.Equal(quantity, batch.Quantity);
        Assert.Equal(quantity, (await db.Inventory.SingleAsync(i => i.ShopId == shopId && i.ItemId == item.Id)).Quantity);

        var movement = await db.StockTransactions.SingleAsync(t => t.InventoryBatchId == batch.Id);
        Assert.Equal(StockTransactionType.In, movement.TransactionType);
        Assert.Equal(quantity, movement.Quantity);

        var payable = await db.SupplierLedgerEntries.SingleAsync(e => e.BatchId == batch.Id);
        Assert.Equal(SupplierLedgerEntryType.GoodsReceived, payable.EntryType);
        Assert.Equal(cost, payable.Amount);
    }

    private async Task AssertNoTraceAsync(Guid shopId, string barcode)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var normalized = barcode.Trim();
        Assert.False(await db.Items.AnyAsync(i => i.ShopId == shopId && i.Barcode == normalized));
        Assert.False(await db.InventoryBatches.AnyAsync(b => b.ShopId == shopId
            && db.Items.Any(i => i.Id == b.ItemId && i.Barcode == normalized)));
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string Barcode() => $"BULK-{Guid.NewGuid():N}";

    private static string OperationKey() => $"intake-{Guid.NewGuid():N}";

    private static object Row(
        string clientRowId,
        string barcode,
        string batchNumber,
        decimal quantity,
        decimal totalPurchaseCost,
        Guid? supplierId = null) =>
        new
        {
            clientRowId,
            itemName = $"Item {barcode.Trim()}",
            barcode,
            itemDescription = (string?)null,
            uom = "pcs",
            batchNumber,
            quantity,
            totalPurchaseCost,
            mrp = 150m,
            salesPrice = 140m,
            taxRatePercent = 5m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId,
            referenceNumber = "GRN-1",
            notes = (string?)null,
            performedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
            hsnCode = (string?)null,
        };

    private static async Task<HttpResponseMessage> PostBatchAsync(
        HttpClient client, string token, string? operationKey, params object[] rows)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { operationKey, items = rows });
        return await client.SendAsync(request);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.EnrollAsync(new
        {
            email = $"bulk-intake-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<(Guid ShopId, string OwnerToken)> CreateShopAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    /// <summary>
    /// Fails the first save that stages each armed batch number, as a dropped connection or a
    /// failed commit would; every later save of that batch goes through. Batch numbers carry a
    /// per-test prefix, so tests running in other classes never match.
    /// </summary>
    private sealed class FailingBatchInterceptor : SaveChangesInterceptor
    {
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();

        public void Arm(params string[] batchNumbers)
        {
            lock (_gate)
                _pending.UnionWith(batchNumbers);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var staged = eventData.Context?.ChangeTracker.Entries<InventoryBatch>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity.BatchNumber)
                .ToArray() ?? [];

            lock (_gate)
            {
                foreach (var batchNumber in staged)
                {
                    if (_pending.Remove(batchNumber))
                        throw new InvalidOperationException("Simulated storage failure.");
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
