using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class InventoryControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

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

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"inventory-{Guid.NewGuid():N}@test.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!",
            firstName = "Test",
            lastName = "User",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> CreateShopAsync(HttpClient client, string token)
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
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<Guid> CreateSupplierAsync(HttpClient client, string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            contactPersonName = "Supplier Contact",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            amount = 0m,
            status = 0,
            isActive = true,
            isPreferred = true,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("supplierId").GetGuid();
    }

    [Fact]
    public async Task AddInboundInventory_CreatesItemBatchTransactionAndInventoryAggregate()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Supplier 1");
        var barcode = $"INV-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            itemName = "Toor Dal",
            barcode,
            itemDescription = "Organic",
            uom = "kg",
            batchNumber = "B-001",
            quantity = 10.5m,
            costPrice = 85m,
            mrp = 120m,
            salesPrice = 110m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId,
            referenceNumber = "PO-1001",
            notes = "Initial inbound",
            performedAt = (DateTimeOffset?)null,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = body.GetProperty("itemId").GetGuid();
        var batchId = body.GetProperty("inventoryBatchId").GetGuid();
        var transactionId = body.GetProperty("stockTransactionId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId);
        Assert.NotNull(item);
        Assert.Equal(barcode, item.Barcode);

        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(10.5m, batch.Quantity);

        var transaction = await db.StockTransactions.FirstOrDefaultAsync(t => t.Id == transactionId);
        Assert.NotNull(transaction);
        Assert.Equal(10.5m, transaction.Quantity);

        var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.ItemId == itemId && i.ShopId == item.ShopId);
        Assert.NotNull(inventory);
        Assert.Equal(10.5m, inventory.Quantity);

        var ledgerEntries = await db.SupplierLedgerEntries
            .Where(e => e.BatchId == batchId)
            .ToListAsync();

        Assert.Single(ledgerEntries);
        Assert.Equal(SupplierLedgerEntryType.GoodsReceived, ledgerEntries[0].EntryType);
        Assert.Equal(892.50m, ledgerEntries[0].Amount);
        Assert.Equal(supplierId, ledgerEntries[0].SupplierId);
    }

    [Fact]
    public async Task AddInboundInventory_SameBatchTwice_IncrementsBatchAndAggregateAndAddsTransaction()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Supplier 2");
        var barcode = $"INV-{Guid.NewGuid():N}";

        async Task<HttpResponseMessage> SendInbound(decimal quantity)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            request.Content = JsonContent.Create(new
            {
                itemName = "Masoor Dal",
                barcode,
                itemDescription = "Organic",
                uom = "kg",
                batchNumber = "B-REPEAT",
                quantity,
                costPrice = 90m,
                mrp = 130m,
                salesPrice = 120m,
                taxRatePercent = 5m,
                taxIncluded = false,
                expiryDate = (DateOnly?)null,
                manufacturingDate = (DateOnly?)null,
                supplierId,
                referenceNumber = "PO-2002",
                notes = "Restock",
                performedAt = (DateTimeOffset?)null,
            });

            return await client.SendAsync(request);
        }

        var first = await SendInbound(4m);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendInbound(6m);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AddInboundInventory_WithoutSupplier_CreatesLedgerEntryWithSystemSupplier()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = $"INV-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            itemName = "Moong Dal",
            barcode,
            itemDescription = "Organic",
            uom = "kg",
            batchNumber = "B-NO-SUP",
            quantity = 5m,
            costPrice = 100m,
            mrp = 140m,
            salesPrice = 130m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = "PO-NA",
            notes = "Inbound without supplier",
            performedAt = (DateTimeOffset?)null,
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = body.GetProperty("inventoryBatchId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ledgerEntry = await db.SupplierLedgerEntries.SingleAsync(e => e.BatchId == batchId);
        Assert.Equal(SupplierLedgerEntryType.GoodsReceived, ledgerEntry.EntryType);
        Assert.Equal("Receipt with no supplier assigned", ledgerEntry.Notes);

        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == batchId);
        Assert.NotNull(batch.SupplierId);
    }

    [Fact]
    public async Task AddInboundInventoryBatch_WithInvalidRow_ReturnsPartialSuccessAndPersistsValidRows()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var sharedBarcode = $"BATCH-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    clientRowId = "row-1",
                    itemName = "Batch Rice",
                    barcode = sharedBarcode,
                    itemDescription = "Valid row",
                    uom = "kg",
                    batchNumber = "BR-001",
                    quantity = 5m,
                    costPrice = 90m,
                    mrp = 120m,
                    salesPrice = 110m,
                    taxRatePercent = 5m,
                    taxIncluded = false,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                    referenceNumber = "PO-3001",
                    notes = "Inbound",
                    performedAt = (DateTimeOffset?)null,
                },
                new
                {
                    clientRowId = "row-2",
                    itemName = "Different Name Same Barcode",
                    barcode = sharedBarcode,
                    itemDescription = "Invalid row",
                    uom = "kg",
                    batchNumber = "BR-002",
                    quantity = 5m,
                    costPrice = 90m,
                    mrp = 120m,
                    salesPrice = 110m,
                    taxRatePercent = 5m,
                    taxIncluded = false,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                    referenceNumber = "PO-3002",
                    notes = "Inbound",
                    performedAt = (DateTimeOffset?)null,
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("requestedCount").GetInt32());
        Assert.Equal(1, body.GetProperty("successCount").GetInt32());
        Assert.Equal(1, body.GetProperty("failedCount").GetInt32());

        var succeeded = body.GetProperty("succeeded");
        Assert.Equal(1, succeeded.GetArrayLength());
        Assert.Equal("row-1", succeeded[0].GetProperty("clientRowId").GetString());
        Assert.Equal(5m, succeeded[0].GetProperty("result").GetProperty("batchQuantity").GetDecimal());
        Assert.Equal(5m, succeeded[0].GetProperty("result").GetProperty("totalQuantity").GetDecimal());

        var failed = body.GetProperty("failed");
        Assert.Equal(1, failed.GetArrayLength());
        Assert.Equal("row-2", failed[0].GetProperty("clientRowId").GetString());
        Assert.Equal("Inventory.ItemNameBarcodeMismatch", failed[0].GetProperty("errors")[0].GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var itemCount = await db.Items.CountAsync(i => i.Name == "Batch Rice");
        Assert.Equal(1, itemCount);

        var invalidItemCount = await db.Items.CountAsync(i => i.Name == "Different Name Same Barcode");
        Assert.Equal(0, invalidItemCount);

        var item = await db.Items.SingleAsync(i => i.Barcode == sharedBarcode);
        var batchCount = await db.InventoryBatches.CountAsync(b => b.ItemId == item.Id);
        Assert.Equal(1, batchCount);

        var txCount = await db.StockTransactions.CountAsync(t => t.ItemId == item.Id);
        Assert.Equal(1, txCount);

        var inventory = await db.Inventory.SingleAsync(i => i.ItemId == item.Id);
        Assert.Equal(5m, inventory.Quantity);
    }

    [Fact]
    public async Task AddInboundInventoryBatch_WhenMoreThanHundredRows_ReturnsClearLimitMessage()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var items = Enumerable.Range(1, 101)
            .Select(index => (object)new
            {
                clientRowId = $"row-{index}",
                itemName = $"Item-{index}",
                barcode = $"BC-{index}",
                itemDescription = "Bulk",
                uom = "kg",
                batchNumber = $"B-{index}",
                quantity = 1m,
                costPrice = 10m,
                mrp = 20m,
                salesPrice = 15m,
                taxRatePercent = 5m,
                taxIncluded = false,
                expiryDate = (DateOnly?)null,
                manufacturingDate = (DateOnly?)null,
                supplierId = (Guid?)null,
                referenceNumber = $"PO-{index}",
                notes = "Bulk inbound",
                performedAt = (DateTimeOffset?)null,
            })
            .ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { items });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Only 100 items are allowed in a batch.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task EditInventoryBatch_WhenQuantityOrCostCorrected_AppendsReversalAndCorrectedLedgerEntries()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var supplierId = await CreateSupplierAsync(client, ownerToken, "Supplier 3");
        var barcode = $"INV-{Guid.NewGuid():N}";

        using var inboundRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inboundRequest.Content = JsonContent.Create(new
        {
            itemName = "Chana Dal",
            barcode,
            itemDescription = "Initial",
            uom = "kg",
            batchNumber = "B-EDIT-1",
            quantity = 10m,
            costPrice = 80m,
            mrp = 120m,
            salesPrice = 110m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId,
            referenceNumber = "PO-EDIT-1",
            notes = "Initial inbound",
            performedAt = (DateTimeOffset?)null,
        });

        var inboundResponse = await client.SendAsync(inboundRequest);
        Assert.Equal(HttpStatusCode.OK, inboundResponse.StatusCode);
        var inboundBody = await inboundResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var editRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/inventory/batches/{batchId}");
        editRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        editRequest.Content = JsonContent.Create(new
        {
            newBatchNumber = "B-EDIT-CORRECTED",
            quantity = 12m,
            costPrice = 85m,
            mrp = 120m,
            salesPrice = 110m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId,
            notes = "Correction",
            entryDate = new DateOnly(2026, 4, 6),
        });

        var editResponse = await client.SendAsync(editRequest);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var oldBatch = await db.InventoryBatches.FindAsync(batchId);
        Assert.True(oldBatch!.IsVoided);

        var newBatch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.BatchNumber == "B-EDIT-CORRECTED");
        Assert.NotNull(newBatch);
        Assert.Equal(12m, newBatch.Quantity);

        var entries = (await db.SupplierLedgerEntries
            .Where(e => e.SupplierId == supplierId)
            .ToListAsync())
            .OrderBy(e => e.CreatedAt)
            .ToList();

        Assert.Equal(3, entries.Count);

        Assert.Equal(SupplierLedgerEntryType.GoodsReceived, entries[0].EntryType);
        Assert.Equal(800m, entries[0].Amount);
        Assert.Equal(batchId, entries[0].BatchId);

        Assert.Equal(SupplierLedgerEntryType.RecordAdjusted, entries[1].EntryType);
        Assert.Equal(-800m, entries[1].Amount);
        Assert.Null(entries[1].BatchId);

        Assert.Equal(SupplierLedgerEntryType.GoodsReceived, entries[2].EntryType);
        Assert.Equal(1020m, entries[2].Amount);
        Assert.Equal(newBatch.Id, entries[2].BatchId);
    }
}
