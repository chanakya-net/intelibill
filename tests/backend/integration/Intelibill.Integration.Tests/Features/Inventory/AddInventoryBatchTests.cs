using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests.Features.Inventory;

[Collection("Integration Tests")]
public sealed class AddInventoryBatchTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"inventory-batch-{Guid.NewGuid():N}@test.com";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
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
    public async Task AddBatch_WithHsnCode_StoresHsnCodeOnItem()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = $"BATCH-HSN-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    clientRowId = "row-1",
                    itemName = "HSN Rice",
                    barcode,
                    itemDescription = "With HSN",
                    uom = "kg",
                    batchNumber = "HSN-001",
                    quantity = 5m,
                    costPrice = 90m,
                    mrp = 120m,
                    salesPrice = 110m,
                    taxRatePercent = 5m,
                    taxIncluded = false,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                    supplierId = (Guid?)null,
                    referenceNumber = "PO-HSN-1",
                    notes = "Inbound",
                    performedAt = (DateTimeOffset?)null,
                    hsnCode = "HSN-1001",
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var item = await db.Items.SingleAsync(i => i.Barcode == barcode);
        Assert.Equal("HSN-1001", item.HsnCode);
    }

    [Fact]
    public async Task AddBatch_WithNullHsnCode_LeavesItemHsnCodeUnchanged()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = $"BATCH-HSN-{Guid.NewGuid():N}";

        async Task<HttpResponseMessage> SendBatch(string batchNumber, string? hsnCode)
        {
            using var batchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
            batchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            batchRequest.Content = JsonContent.Create(new
            {
                items = new object[]
                {
                    new
                    {
                        clientRowId = "row-1",
                        itemName = "HSN Rice",
                        barcode,
                        itemDescription = "HSN test",
                        uom = "kg",
                        batchNumber,
                        quantity = 5m,
                        costPrice = 90m,
                        mrp = 120m,
                        salesPrice = 110m,
                        taxRatePercent = 5m,
                        taxIncluded = false,
                        expiryDate = (DateOnly?)null,
                        manufacturingDate = (DateOnly?)null,
                        supplierId = (Guid?)null,
                        referenceNumber = "PO-HSN-NULL",
                        notes = "Inbound",
                        performedAt = (DateTimeOffset?)null,
                        hsnCode,
                    },
                },
            });

            return await client.SendAsync(batchRequest);
        }

        var first = await SendBatch("HSN-INIT", "HSN-OLD");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendBatch("HSN-NO-UPDATE", null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var item = await db.Items.SingleAsync(i => i.Barcode == barcode);
        Assert.Equal("HSN-OLD", item.HsnCode);
    }

    [Fact]
    public async Task AddBatch_WithHsnCode_TaxRateStillStoredOnBatch()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = $"BATCH-HSN-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            items = new object[]
            {
                new
                {
                    clientRowId = "row-1",
                    itemName = "HSN Tax Rice",
                    barcode,
                    itemDescription = "HSN + Tax",
                    uom = "kg",
                    batchNumber = "HSN-TAX-1",
                    quantity = 5m,
                    costPrice = 90m,
                    mrp = 120m,
                    salesPrice = 110m,
                    taxRatePercent = 18m,
                    taxIncluded = false,
                    expiryDate = (DateOnly?)null,
                    manufacturingDate = (DateOnly?)null,
                    supplierId = (Guid?)null,
                    referenceNumber = "PO-HSN-TAX",
                    notes = "Inbound",
                    performedAt = (DateTimeOffset?)null,
                    hsnCode = "HSN-TAX",
                },
            },
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = body.GetProperty("succeeded")[0].GetProperty("result").GetProperty("inventoryBatchId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(18m, batch.TaxRatePercent);
    }
}

