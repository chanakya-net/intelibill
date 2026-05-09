using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
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

    [Fact]
    public async Task VoidAdjustment_AsOwnerForDecrease_RestoresStockCreatesReversalAndMarksAdjustmentVoided()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);
        var inbound = await CreateInboundAsync(client, ownerToken, 10m, 80m);
        var adjustment = await CreateAdjustmentAsync(
            client,
            ownerToken,
            inbound.BatchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            3m,
            "Damaged in storage");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustment.AdjustmentId}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { reason = "Entered twice" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reversalStockTransactionId = body.GetProperty("reversalStockTransactionId").GetGuid();
        Assert.Equal(7m, body.GetProperty("batchQuantityBefore").GetDecimal());
        Assert.Equal(10m, body.GetProperty("batchQuantityAfter").GetDecimal());
        Assert.Equal(7m, body.GetProperty("inventoryQuantityBefore").GetDecimal());
        Assert.Equal(10m, body.GetProperty("inventoryQuantityAfter").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == inbound.BatchId);
        Assert.Equal(10m, batch.Quantity);

        var inventory = await db.Inventory.SingleAsync(i => i.ItemId == inbound.ItemId);
        Assert.Equal(10m, inventory.Quantity);

        var reversal = await db.StockTransactions.SingleAsync(t => t.Id == reversalStockTransactionId);
        Assert.Equal(StockTransactionType.Reversal, reversal.TransactionType);
        Assert.Equal(3m, reversal.Quantity);
        Assert.Equal(adjustment.AdjustmentNumber, reversal.ReferenceNumber);
        Assert.Equal("Entered twice", reversal.Notes);

        var voidedAdjustment = await db.InventoryAdjustments.SingleAsync(a => a.Id == adjustment.AdjustmentId);
        Assert.True(voidedAdjustment.IsVoided);
        Assert.Equal("Entered twice", voidedAdjustment.VoidReason);
        Assert.Equal(reversalStockTransactionId, voidedAdjustment.ReversalStockTransactionId);
        Assert.NotNull(voidedAdjustment.VoidedAt);
        Assert.NotNull(voidedAdjustment.VoidedBy);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Staff")]
    public async Task VoidAdjustment_AsManagerOrStaff_ReturnsForbidden(string role)
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (shopId, ownerScopedToken) = await CreateShopAsync(client, ownerToken);
        var inbound = await CreateInboundAsync(client, ownerScopedToken, 5m, 50m);
        var adjustment = await CreateAdjustmentAsync(
            client,
            ownerScopedToken,
            inbound.BatchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1m,
            "Damaged");
        var memberToken = await AddUserAndLoginAsync(client, ownerScopedToken, shopId, role);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustment.AdjustmentId}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        request.Content = JsonContent.Create(new { reason = "No access" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VoidAdjustment_WhenAlreadyVoided_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);
        var inbound = await CreateInboundAsync(client, ownerToken, 5m, 50m);
        var adjustment = await CreateAdjustmentAsync(
            client,
            ownerToken,
            inbound.BatchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1m,
            "Damaged");

        using var first = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustment.AdjustmentId}/void");
        first.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        first.Content = JsonContent.Create(new { reason = "Entered twice" });
        var firstResponse = await client.SendAsync(first);
        firstResponse.EnsureSuccessStatusCode();

        using var second = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustment.AdjustmentId}/void");
        second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        second.Content = JsonContent.Create(new { reason = "Try again" });
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        var body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("already voided", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VoidAdjustment_ForIncreaseWhenReversalWouldMakeStockNegative_ReturnsConflictAndDoesNotVoid()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);
        var inbound = await CreateInboundAsync(client, ownerToken, 1m, 50m);
        var increase = await CreateAdjustmentAsync(
            client,
            ownerToken,
            inbound.BatchId,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            3m,
            "Found stock");

        using (var setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await setupDb.Inventory
                .Where(i => i.ItemId == inbound.ItemId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Quantity, 1m));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{increase.AdjustmentId}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new { reason = "Invalid found stock" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adjustment = await db.InventoryAdjustments.SingleAsync(a => a.Id == increase.AdjustmentId);
        Assert.False(adjustment.IsVoided);
        Assert.Null(adjustment.ReversalStockTransactionId);
        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == inbound.BatchId);
        Assert.Equal(4m, batch.Quantity);
    }

    [Fact]
    public async Task VoidAdjustment_ForOtherShopAdjustment_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA);
        var inboundA = await CreateInboundAsync(client, ownerTokenA, 5m, 50m);
        var adjustmentA = await CreateAdjustmentAsync(
            client,
            ownerTokenA,
            inboundA.BatchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1m,
            "Damaged");

        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustmentA.AdjustmentId}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        request.Content = JsonContent.Create(new { reason = "Wrong shop" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdjustmentHistory_FiltersPaginatesSortsAndExcludesVoidedByDefault()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);
        var performedAt = new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero);

        Guid itemId;
        Guid batchId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var performer = User.CreateWithEmail($"history-performer-{Guid.NewGuid():N}@test.com", "hash", "History", "Actor");
            db.Users.Add(performer);

            var item = Item.Create(shopId, $"Filtered Item {Guid.NewGuid():N}", null, "kg", $"HIST-{Guid.NewGuid():N}", true, performer.Id);
            var batch = InventoryBatch.Create(
                shopId,
                item.Id,
                $"HB-{Guid.NewGuid():N}"[..18],
                20m,
                40m,
                70m,
                60m,
                5m,
                false,
                null,
                null,
                null,
                performer.Id).Value;
            var otherItem = Item.Create(shopId, $"Other Item {Guid.NewGuid():N}", null, "kg", $"OTHER-{Guid.NewGuid():N}", true, performer.Id);
            var otherBatch = InventoryBatch.Create(
                shopId,
                otherItem.Id,
                $"OB-{Guid.NewGuid():N}"[..18],
                20m,
                50m,
                80m,
                70m,
                5m,
                false,
                null,
                null,
                null,
                performer.Id).Value;

            itemId = item.Id;
            batchId = batch.Id;

            var olderMatch = CreateAdjustmentForTest(
                shopId,
                item.Id,
                batch.Id,
                performer.Id,
                "ADJ-HIST-0001",
                InventoryAdjustmentDirection.Decrease,
                InventoryAdjustmentReason.Damaged,
                2m,
                40m,
                performedAt,
                new DateTimeOffset(2026, 5, 2, 10, 1, 0, TimeSpan.Zero),
                "Older matching row");
            var newerMatch = CreateAdjustmentForTest(
                shopId,
                item.Id,
                batch.Id,
                performer.Id,
                "ADJ-HIST-0002",
                InventoryAdjustmentDirection.Decrease,
                InventoryAdjustmentReason.Damaged,
                3m,
                40m,
                performedAt,
                new DateTimeOffset(2026, 5, 2, 10, 2, 0, TimeSpan.Zero),
                "Newer matching row");
            var wrongReason = CreateAdjustmentForTest(
                shopId,
                item.Id,
                batch.Id,
                performer.Id,
                "ADJ-HIST-0003",
                InventoryAdjustmentDirection.Decrease,
                InventoryAdjustmentReason.Stolen,
                1m,
                40m,
                performedAt,
                new DateTimeOffset(2026, 5, 2, 10, 3, 0, TimeSpan.Zero),
                "Wrong reason");
            var wrongItem = CreateAdjustmentForTest(
                shopId,
                otherItem.Id,
                otherBatch.Id,
                performer.Id,
                "ADJ-HIST-0004",
                InventoryAdjustmentDirection.Increase,
                InventoryAdjustmentReason.FoundStock,
                1m,
                50m,
                performedAt.AddDays(1),
                new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero),
                "Wrong item");

            db.Items.AddRange(item, otherItem);
            db.InventoryBatches.AddRange(batch, otherBatch);
            db.InventoryAdjustments.AddRange(olderMatch, newerMatch, wrongReason, wrongItem);
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/inventory/adjustments?pageNumber=1&pageSize=1&itemId={itemId}&batchId={batchId}&direction=Decrease&reason=Damaged&from={Uri.EscapeDataString(new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero).ToString("O"))}&to={Uri.EscapeDataString(new DateTimeOffset(2026, 5, 2, 23, 59, 59, TimeSpan.Zero).ToString("O"))}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(1, body.GetProperty("pageSize").GetInt32());

        var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("ADJ-HIST-0002", row.GetProperty("adjustmentNumber").GetString());
        Assert.Equal(itemId, row.GetProperty("itemId").GetGuid());
        Assert.Equal(batchId, row.GetProperty("batchId").GetGuid());
        Assert.Equal("Filtered Item", row.GetProperty("itemName").GetString()![..13]);
        Assert.Equal("HB-", row.GetProperty("batchNumber").GetString()![..3]);
        Assert.Equal("Decrease", row.GetProperty("direction").GetString());
        Assert.Equal("Damaged", row.GetProperty("reason").GetString());
        Assert.Equal(3m, row.GetProperty("quantity").GetDecimal());
        Assert.Equal(120m, row.GetProperty("costImpact").GetDecimal());
        Assert.Equal("Newer matching row", row.GetProperty("notes").GetString());
        Assert.Equal("History Actor", row.GetProperty("performedByDisplayName").GetString());
        Assert.False(row.GetProperty("isVoided").GetBoolean());
    }

    [Fact]
    public async Task AdjustmentHistory_OwnerManagerAndStaffCanViewActiveShopHistory()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (shopId, ownerScopedToken) = await CreateShopAsync(client, ownerToken);
        var managerToken = await AddUserAndLoginAsync(client, ownerScopedToken, shopId, "Manager");
        var staffToken = await AddUserAndLoginAsync(client, ownerScopedToken, shopId, "Staff");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSimpleAdjustmentAsync(db, shopId);
        }

        foreach (var token in new[] { ownerScopedToken, managerToken, staffToken })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/adjustments");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        }
    }

    [Fact]
    public async Task AdjustmentHistory_IsScopedToActiveShop()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var (shopA, ownerTokenA) = await CreateShopAsync(client, tokenA);
        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedSimpleAdjustmentAsync(db, shopA);
        }

        using var requestA = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/adjustments");
        requestA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var responseA = await client.SendAsync(requestA);
        responseA.EnsureSuccessStatusCode();
        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, bodyA.GetProperty("totalCount").GetInt32());

        using var requestB = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/adjustments");
        requestB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var responseB = await client.SendAsync(requestB);
        responseB.EnsureSuccessStatusCode();
        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, bodyB.GetProperty("totalCount").GetInt32());
        Assert.Empty(bodyB.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AdjustmentHistory_IncludeVoidedReturnsVoidMetadataAndDisplayFallbacks()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token);
        var performerEmail = $"fallback-performer-{Guid.NewGuid():N}@test.com";
        var voidedByPhone = UniquePhone();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var performer = User.CreateWithEmail(performerEmail, "hash", string.Empty, string.Empty);
            var voidedBy = User.CreateWithPhone(voidedByPhone, string.Empty, string.Empty);
            db.Users.AddRange(performer, voidedBy);

            var item = Item.Create(shopId, $"Voided Item {Guid.NewGuid():N}", null, "kg", $"VOID-{Guid.NewGuid():N}", true, performer.Id);
            var batch = InventoryBatch.Create(
                shopId,
                item.Id,
                $"VB-{Guid.NewGuid():N}"[..18],
                10m,
                25m,
                40m,
                35m,
                5m,
                false,
                null,
                null,
                null,
                performer.Id).Value;
            var adjustment = CreateAdjustmentForTest(
                shopId,
                item.Id,
                batch.Id,
                performer.Id,
                "ADJ-VOID-0001",
                InventoryAdjustmentDirection.Decrease,
                InventoryAdjustmentReason.Damaged,
                2m,
                25m,
                new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 4, 12, 1, 0, TimeSpan.Zero),
                "Voided row");
            var reversal = StockTransaction.Create(
                shopId,
                item.Id,
                batch.Id,
                StockTransactionType.In,
                2m,
                "REV-VOID-0001",
                "Void reversal",
                new DateTimeOffset(2026, 5, 4, 13, 0, 0, TimeSpan.Zero),
                voidedBy.Id,
                voidedBy.Id).Value;
            var voidResult = adjustment.Void(
                new DateTimeOffset(2026, 5, 4, 13, 0, 0, TimeSpan.Zero),
                voidedBy.Id,
                "Wrong adjustment",
                reversal.Id);
            Assert.False(voidResult.IsError);

            db.Items.Add(item);
            db.InventoryBatches.Add(batch);
            db.StockTransactions.Add(reversal);
            db.InventoryAdjustments.Add(adjustment);
            await db.SaveChangesAsync();
        }

        using var defaultRequest = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/adjustments");
        defaultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var defaultResponse = await client.SendAsync(defaultRequest);
        defaultResponse.EnsureSuccessStatusCode();
        var defaultBody = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, defaultBody.GetProperty("totalCount").GetInt32());

        using var includeVoidedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/adjustments?includeVoided=true");
        includeVoidedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var includeVoidedResponse = await client.SendAsync(includeVoidedRequest);

        Assert.Equal(HttpStatusCode.OK, includeVoidedResponse.StatusCode);
        var includeVoidedBody = await includeVoidedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(includeVoidedBody.GetProperty("items").EnumerateArray());
        Assert.Equal("ADJ-VOID-0001", row.GetProperty("adjustmentNumber").GetString());
        Assert.True(row.GetProperty("isVoided").GetBoolean());
        Assert.Equal("Wrong adjustment", row.GetProperty("voidReason").GetString());
        Assert.Equal(performerEmail, row.GetProperty("performedByDisplayName").GetString());
        Assert.Equal(voidedByPhone, row.GetProperty("voidedByDisplayName").GetString());
        Assert.True(row.GetProperty("voidedAt").ValueKind == JsonValueKind.String);
        Assert.True(row.GetProperty("reversalStockTransactionId").ValueKind == JsonValueKind.String);
    }

    private static string UniqueEmail() => $"adjustment-{Guid.NewGuid():N}@test.com";

    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

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

    private static async Task<(Guid AdjustmentId, string AdjustmentNumber)> CreateAdjustmentAsync(
        HttpClient client,
        string token,
        Guid batchId,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity,
        string notes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{batchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            direction,
            reason,
            quantity,
            performedAt = (DateTimeOffset?)null,
            notes,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("adjustmentId").GetGuid(), body.GetProperty("adjustmentNumber").GetString()!);
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

    private static async Task SeedSimpleAdjustmentAsync(ApplicationDbContext db, Guid shopId)
    {
        var performer = User.CreateWithEmail($"history-simple-{Guid.NewGuid():N}@test.com", "hash", "History", "Viewer");
        db.Users.Add(performer);
        var item = Item.Create(shopId, $"Simple Item {Guid.NewGuid():N}", null, "kg", $"SIMPLE-{Guid.NewGuid():N}", true, performer.Id);
        var batch = InventoryBatch.Create(
            shopId,
            item.Id,
            $"SB-{Guid.NewGuid():N}"[..18],
            10m,
            30m,
            50m,
            45m,
            5m,
            false,
            null,
            null,
            null,
            performer.Id).Value;
        var adjustment = CreateAdjustmentForTest(
            shopId,
            item.Id,
            batch.Id,
            performer.Id,
            $"ADJ-SIMPLE-{Guid.NewGuid():N}"[..24],
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            1m,
            30m,
            new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 1, 9, 1, 0, TimeSpan.Zero),
            null);

        db.Items.Add(item);
        db.InventoryBatches.Add(batch);
        db.InventoryAdjustments.Add(adjustment);
        await db.SaveChangesAsync();
    }

    private static InventoryAdjustment CreateAdjustmentForTest(
        Guid shopId,
        Guid itemId,
        Guid batchId,
        Guid performedBy,
        string adjustmentNumber,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity,
        decimal unitCost,
        DateTimeOffset performedAt,
        DateTimeOffset createdAt,
        string? notes)
    {
        const decimal quantityBefore = 10m;
        var quantityAfter = direction == InventoryAdjustmentDirection.Increase
            ? quantityBefore + quantity
            : quantityBefore - quantity;
        var adjustment = InventoryAdjustment.Create(
            shopId,
            itemId,
            batchId,
            adjustmentNumber,
            direction,
            reason,
            quantity,
            unitCost,
            decimal.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero),
            quantityBefore,
            quantityAfter,
            quantityBefore,
            quantityAfter,
            performedAt,
            performedBy,
            notes,
            performedBy).Value;

        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!.SetValue(adjustment, createdAt);
        return adjustment;
    }
}
