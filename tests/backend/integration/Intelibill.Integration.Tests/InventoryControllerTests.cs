using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public class InventoryControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public InventoryControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
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

    [Fact]
    public async Task AddInboundInventory_CreatesItemBatchTransactionAndInventoryAggregate()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
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
            minSalePrice = 100m,
            taxRatePercent = 5m,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
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
    }

    [Fact]
    public async Task AddInboundInventory_SameBatchTwice_IncrementsBatchAndAggregateAndAddsTransaction()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
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
                minSalePrice = 110m,
                taxRatePercent = 5m,
                expiryDate = (DateOnly?)null,
                manufacturingDate = (DateOnly?)null,
                referenceNumber = "PO-2002",
                notes = "Restock",
                performedAt = (DateTimeOffset?)null,
            });

            return await client.SendAsync(request);
        }

        var first = await SendInbound(4m);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendInbound(6m);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = body.GetProperty("itemId").GetGuid();
        var batchId = body.GetProperty("inventoryBatchId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(10m, batch.Quantity);

        var inventory = await db.Inventory.SingleAsync(i => i.ItemId == itemId);
        Assert.Equal(10m, inventory.Quantity);

        var txCount = await db.StockTransactions.CountAsync(t => t.ItemId == itemId && t.InventoryBatchId == batchId);
        Assert.Equal(2, txCount);
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
                    minSalePrice = 100m,
                    taxRatePercent = 5m,
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
                    minSalePrice = 100m,
                    taxRatePercent = 5m,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                    referenceNumber = "PO-3002",
                    notes = "Inbound",
                    performedAt = (DateTimeOffset?)null,
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("requestedCount").GetInt32());
        Assert.Equal(1, body.GetProperty("successCount").GetInt32());
        Assert.Equal(1, body.GetProperty("failedCount").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var itemCount = await db.Items.CountAsync(i => i.Name == "Batch Rice");
        Assert.Equal(1, itemCount);
    }
}
