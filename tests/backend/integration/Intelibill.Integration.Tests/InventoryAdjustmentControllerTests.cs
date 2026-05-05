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
public sealed class InventoryAdjustmentControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    [Fact]
    public async Task CreateAdjustment_AsOwnerDecrease_PersistsAdjustmentStockTransactionAndQuantityChanges()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);
        var inbound = await CreateInboundAsync(client, ownerToken, 10m, 80m);
        var performedAt = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{inbound.BatchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            direction = InventoryAdjustmentDirection.Decrease,
            reason = InventoryAdjustmentReason.Damaged,
            quantity = 3m,
            performedAt,
            notes = "Damaged in storage",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var adjustmentId = body.GetProperty("adjustmentId").GetGuid();
        var adjustmentNumber = body.GetProperty("adjustmentNumber").GetString()!;
        var stockTransactionId = body.GetProperty("stockTransactionId").GetGuid();
        Assert.StartsWith("ADJ-20260501-", adjustmentNumber, StringComparison.Ordinal);
        Assert.Equal(10m, body.GetProperty("batchQuantityBefore").GetDecimal());
        Assert.Equal(7m, body.GetProperty("batchQuantityAfter").GetDecimal());
        Assert.Equal(10m, body.GetProperty("inventoryQuantityBefore").GetDecimal());
        Assert.Equal(7m, body.GetProperty("inventoryQuantityAfter").GetDecimal());
        Assert.Equal(240m, body.GetProperty("costImpact").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == inbound.BatchId);
        Assert.Equal(7m, batch.Quantity);

        var inventory = await db.Inventory.SingleAsync(i => i.ItemId == inbound.ItemId);
        Assert.Equal(7m, inventory.Quantity);

        var transaction = await db.StockTransactions.SingleAsync(t => t.Id == stockTransactionId);
        Assert.Equal(StockTransactionType.Dmg, transaction.TransactionType);
        Assert.Equal(-3m, transaction.Quantity);
        Assert.Equal(adjustmentNumber, transaction.ReferenceNumber);

        var adjustment = await db.InventoryAdjustments.SingleAsync(a => a.Id == adjustmentId);
        Assert.Equal(InventoryAdjustmentDirection.Decrease, adjustment.Direction);
        Assert.Equal(InventoryAdjustmentReason.Damaged, adjustment.Reason);
        Assert.Equal(3m, adjustment.Quantity);
        Assert.Equal(80m, adjustment.UnitCost);
        Assert.Equal(240m, adjustment.CostImpact);
        Assert.Equal(10m, adjustment.BatchQuantityBefore);
        Assert.Equal(7m, adjustment.BatchQuantityAfter);
        Assert.Equal(10m, adjustment.InventoryQuantityBefore);
        Assert.Equal(7m, adjustment.InventoryQuantityAfter);
    }

    [Fact]
    public async Task CreateAdjustment_AsManagerIncrease_ReturnsOk()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (shopId, ownerScopedToken) = await CreateShopAsync(client, ownerToken);
        var inbound = await CreateInboundAsync(client, ownerScopedToken, 5m, 50m);
        var managerToken = await AddUserAndLoginAsync(client, ownerScopedToken, shopId, "Manager");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{inbound.BatchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        request.Content = JsonContent.Create(new
        {
            direction = InventoryAdjustmentDirection.Increase,
            reason = InventoryAdjustmentReason.FoundStock,
            quantity = 2m,
            performedAt = (DateTimeOffset?)null,
            notes = "Found stock",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == inbound.BatchId);
        Assert.Equal(7m, batch.Quantity);
    }

    [Fact]
    public async Task CreateAdjustment_AsStaff_ReturnsForbidden()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (shopId, ownerScopedToken) = await CreateShopAsync(client, ownerToken);
        var inbound = await CreateInboundAsync(client, ownerScopedToken, 5m, 50m);
        var staffToken = await AddUserAndLoginAsync(client, ownerScopedToken, shopId, "Staff");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{inbound.BatchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            direction = InventoryAdjustmentDirection.Increase,
            reason = InventoryAdjustmentReason.FoundStock,
            quantity = 1m,
            performedAt = (DateTimeOffset?)null,
            notes = "Found stock",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAdjustment_ForOtherShopBatch_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA);
        var inboundA = await CreateInboundAsync(client, ownerTokenA, 5m, 50m);

        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{inboundA.BatchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        request.Content = JsonContent.Create(new
        {
            direction = InventoryAdjustmentDirection.Increase,
            reason = InventoryAdjustmentReason.FoundStock,
            quantity = 1m,
            performedAt = (DateTimeOffset?)null,
            notes = "Wrong shop",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateAdjustment_WithFuturePerformedAt_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);
        var inbound = await CreateInboundAsync(client, ownerToken, 5m, 50m);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{inbound.BatchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            direction = InventoryAdjustmentDirection.Increase,
            reason = InventoryAdjustmentReason.FoundStock,
            quantity = 1m,
            performedAt = DateTimeOffset.UtcNow.AddDays(1),
            notes = "Future stock count",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("future", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueEmail() => $"adjustment-{Guid.NewGuid():N}@test.com";

    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

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

    private static async Task<(Guid ShopId, string AccessToken)> CreateShopAsync(HttpClient client, string token)
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

    private static async Task<(Guid ItemId, Guid BatchId)> CreateInboundAsync(
        HttpClient client,
        string token,
        decimal quantity,
        decimal costPrice)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            itemName = $"Adjustment Item {Guid.NewGuid():N}",
            barcode = $"ADJ-{Guid.NewGuid():N}",
            itemDescription = "Adjustment test item",
            uom = "kg",
            batchNumber = $"B-{Guid.NewGuid():N}"[..18],
            quantity,
            costPrice,
            mrp = 120m,
            salesPrice = 110m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = "PO-ADJ",
            notes = "Initial inbound",
            performedAt = (DateTimeOffset?)null,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("itemId").GetGuid(), body.GetProperty("inventoryBatchId").GetGuid());
    }

    private static async Task<string> AddUserAndLoginAsync(
        HttpClient client,
        string ownerToken,
        Guid shopId,
        string role)
    {
        var email = UniqueEmail();
        const string password = "StaffPass1!";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = role,
            lastName = "Member",
            phoneNumber = UniquePhone(),
            password,
            confirmPassword = password,
            role,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password,
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return loginBody.GetProperty("accessToken").GetString()!;
    }
}
