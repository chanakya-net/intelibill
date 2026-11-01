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

namespace Intelibill.Integration.Tests;

/// <summary>
/// Proves against PostgreSQL that every stock count, legacy batch correction and receipt void leaves
/// one signed, attributable movement per quantity change, and that per item
/// <c>sum(movements) = inventory.quantity = sum(active batch quantities)</c> holds afterwards.
/// </summary>
/// <remarks>
/// Every assertion about stored rows reads through a fresh context, so it sees what was committed.
/// </remarks>
[Collection("Integration Tests")]
public sealed class StockCorrectionInvariantTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    // Disarmed unless a test names the count reason whose save should fail.
    private static readonly FailingStockCountInterceptor Fault = new();
    private static readonly decimal[] ParallelCounts = [9m, 7m];

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
    public async Task TenToEight_WritesOutMinusTwo()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId), 8m, "Shelf count found two short");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10m, body.GetProperty("observedQuantity").GetDecimal());
        Assert.Equal(8m, body.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(-2m, body.GetProperty("delta").GetDecimal());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var movement = await db.StockTransactions.SingleAsync(t => t.InventoryBatchId == receipt.BatchId && t.TransactionType != StockTransactionType.In);
        Assert.Equal(StockTransactionType.Out, movement.TransactionType);
        Assert.Equal(-2m, movement.Quantity);
        Assert.Equal(shop.OwnerId, movement.PerformedBy);
        Assert.Equal(body.GetProperty("stockTransactionId").GetGuid(), movement.Id);

        var adjustment = await db.InventoryAdjustments.SingleAsync(a => a.InventoryBatchId == receipt.BatchId);
        Assert.Equal(InventoryAdjustmentDirection.Decrease, adjustment.Direction);
        Assert.Equal(InventoryAdjustmentReason.StockCountCorrection, adjustment.Reason);
        Assert.Equal(2m, adjustment.Quantity);
        Assert.Equal(10m, adjustment.BatchQuantityBefore);
        Assert.Equal(8m, adjustment.BatchQuantityAfter);
        Assert.Equal(shop.OwnerId, adjustment.PerformedBy);
        Assert.Equal("Shelf count found two short", adjustment.Notes);
        Assert.Equal(movement.ReferenceNumber, adjustment.AdjustmentNumber);

        var count = await db.StockCounts.SingleAsync(c => c.InventoryBatchId == receipt.BatchId);
        Assert.Equal(adjustment.Id, count.InventoryAdjustmentId);
        Assert.Equal(movement.Id, count.StockTransactionId);
        Assert.Equal(shop.OwnerId, count.PerformedBy);
        Assert.Equal("Shelf count found two short", count.Reason);

        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 8m);
        await AssertPayableAsync(receipt.BatchId, 800m);
    }

    [Fact]
    public async Task EightToTen_WritesInPlusTwo()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 8m, 80m);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId), 10m, "Found two in the back room");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var movements = await db.StockTransactions.Where(t => t.InventoryBatchId == receipt.BatchId).OrderBy(t => t.CreatedAt).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(StockTransactionType.In, movements[1].TransactionType);
        Assert.Equal(2m, movements[1].Quantity);

        var adjustment = await db.InventoryAdjustments.SingleAsync(a => a.InventoryBatchId == receipt.BatchId);
        Assert.Equal(InventoryAdjustmentDirection.Increase, adjustment.Direction);
        Assert.Equal(2m, adjustment.Quantity);

        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 10m);
        await AssertPayableAsync(receipt.BatchId, 640m);
    }

    [Fact]
    public async Task EqualCount_NoPhysicalMovement()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var version = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, version, 10m, "Monthly stocktake");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("delta").GetDecimal());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("stockTransactionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("adjustmentId").ValueKind);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.InventoryBatchId == receipt.BatchId));
        Assert.False(await db.InventoryAdjustments.AnyAsync(a => a.InventoryBatchId == receipt.BatchId));

        var count = await db.StockCounts.SingleAsync(c => c.InventoryBatchId == receipt.BatchId);
        Assert.Equal(10m, count.ObservedQuantity);
        Assert.Equal(10m, count.CountedQuantity);
        Assert.Null(count.StockTransactionId);
        Assert.Equal(shop.OwnerId, count.PerformedBy);

        // A confirmation is checked against the version it was taken at, so it advances the version
        // and answers the new one: a count racing it from the same screen is refused, not merged.
        var newVersion = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);
        Assert.NotEqual(version, newVersion);
        Assert.Equal(newVersion, body.GetProperty("batchVersion").GetUInt32());
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 10m);
        await AssertPayableAsync(receipt.BatchId, 800m);
    }

    [Fact]
    public async Task MovementCreationFails_RollsBackAllChanges()
    {
        const string reason = "Count whose audit row fails to save";
        Fault.Arm(reason);
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var version = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, version, 7m, reason);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId);
        Assert.Equal(10m, batch.Quantity);
        Assert.Equal(version, batch.Version);
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.InventoryBatchId == receipt.BatchId));
        Assert.False(await db.InventoryAdjustments.AnyAsync(a => a.InventoryBatchId == receipt.BatchId));
        Assert.False(await db.StockCounts.AnyAsync(c => c.InventoryBatchId == receipt.BatchId));
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 10m);
        await AssertPayableAsync(receipt.BatchId, 800m);
    }

    [Fact]
    public async Task PartlySoldBatch_CannotVoidOriginalReceipt()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        await SellAsync(client, shop.OwnerToken, receipt, 4m);

        using (var voidRequest = Authorized(HttpMethod.Post, $"/api/inventory/batches/{receipt.BatchId}/void", shop.OwnerToken))
        {
            var voidResponse = await client.SendAsync(voidRequest);
            Assert.Equal(HttpStatusCode.Conflict, voidResponse.StatusCode);
            var problem = await voidResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Inventory.BatchConsumed", problem.GetProperty("title").GetString());
        }

        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 6m);
        await AssertPayableAsync(receipt.BatchId, 800m);

        // Disposing of the remaining six is a count to zero: one OUT(-6), the purchase still owed.
        var countResponse = await CountAsync(client, shop.OwnerToken, receipt.BatchId, await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId), 0m, "Remaining stock spoiled");
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var removal = await db.StockTransactions.SingleAsync(t => t.InventoryBatchId == receipt.BatchId && t.ReferenceNumber != null && t.ReferenceNumber.StartsWith("ADJ"));
        Assert.Equal(StockTransactionType.Out, removal.TransactionType);
        Assert.Equal(-6m, removal.Quantity);
        Assert.False((await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId)).IsVoided);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 0m);
        await AssertPayableAsync(receipt.BatchId, 800m);
    }

    [Fact]
    public async Task UnsoldReceipt_VoidRemovesExactlyItsQuantity()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);

        using var voidRequest = Authorized(HttpMethod.Post, $"/api/inventory/batches/{receipt.BatchId}/void", shop.OwnerToken);
        var voidResponse = await client.SendAsync(voidRequest);

        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reversal = await db.StockTransactions.SingleAsync(t => t.InventoryBatchId == receipt.BatchId && t.TransactionType == StockTransactionType.Reversal);
        Assert.Equal(-10m, reversal.Quantity);
        Assert.Equal(shop.OwnerId, reversal.PerformedBy);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 0m);
        Assert.Equal(0m, (await db.SupplierLedgerEntries.Where(e => e.BatchId == receipt.BatchId).ToListAsync()).Sum(e => e.Amount));
    }

    [Fact]
    public async Task TwoCountsAgainstSameVersion_OnlyOneCommits()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var version = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);

        using var start = new SemaphoreSlim(0, 2);
        var attempts = ParallelCounts.Select(counted => Task.Run(async () =>
        {
            using var parallelClient = CreateClient();
            await start.WaitAsync();
            return await CountAsync(parallelClient, shop.OwnerToken, receipt.BatchId, version, counted, $"Parallel count {counted}");
        })).ToArray();
        start.Release(2);
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var problem = await loser.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Inventory.BatchVersionConflict", problem.GetProperty("title").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.StockCounts.CountAsync(c => c.InventoryBatchId == receipt.BatchId));
        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId);
        Assert.Contains(batch.Quantity, ParallelCounts);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: batch.Quantity);
    }

    [Fact]
    public async Task StaleVersionAfterSale_IsRefusedAndNotReapplied()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var versionBeforeSale = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);
        await SellAsync(client, shop.OwnerToken, receipt, 1m);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, versionBeforeSale, 8m, "Count taken before the sale");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Inventory.BatchVersionConflict", problem.GetProperty("title").GetString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.StockCounts.AnyAsync(c => c.InventoryBatchId == receipt.BatchId));
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 9m);
    }

    [Fact]
    public async Task KeyedCount_ReplaysWithoutSecondMovement()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var version = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);
        var key = $"count-{Guid.NewGuid():N}";

        var first = await CountAsync(client, shop.OwnerToken, receipt.BatchId, version, 6m, "Keyed count", key);
        var resend = await CountAsync(client, shop.OwnerToken, receipt.BatchId, version, 6m, "Keyed count", key);
        var changed = await CountAsync(client, shop.OwnerToken, receipt.BatchId, version, 5m, "Keyed count", key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stockCountId").GetGuid(),
            (await resend.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stockCountId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.StockCounts.CountAsync(c => c.InventoryBatchId == receipt.BatchId));
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 6m);
    }

    [Fact]
    public async Task Count_RequiresReason()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId), 8m, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 10m);
    }

    [Fact]
    public async Task Count_AsStaff_IsForbidden()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var version = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);
        var staffToken = await AddMemberAndLoginAsync(client, shop.OwnerToken, shop.ShopId, "Staff");

        var response = await CountAsync(client, staffToken, receipt.BatchId, version, 8m, "Staff count");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 10m);
    }

    [Fact]
    public async Task Count_AsManager_Succeeds()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var version = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);
        var managerToken = await AddMemberAndLoginAsync(client, shop.OwnerToken, shop.ShopId, "Manager");

        var response = await CountAsync(client, managerToken, receipt.BatchId, version, 9m, "Manager count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 9m);
    }

    [Fact]
    public async Task LegacyBatchCorrection_QuantityChange_IsRefusedAndNothingMoves()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);

        using var editRequest = Authorized(HttpMethod.Put, $"/api/inventory/batches/{receipt.BatchId}", shop.OwnerToken);
        editRequest.Content = JsonContent.Create(new
        {
            newBatchNumber = $"{receipt.BatchNumber}-C",
            quantity = 8m,
            costPrice = 80m,
            mrp = 150m,
            salesPrice = 140m,
            taxRatePercent = 0m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = receipt.SupplierId,
            notes = "Receipt keyed as 10, delivered 8",
            entryDate = (DateOnly?)null,
        });
        var editResponse = await client.SendAsync(editRequest);

        Assert.Equal(HttpStatusCode.BadRequest, editResponse.StatusCode);
        Assert.Equal("Inventory.BatchQuantityEditMoved", (await editResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId);
        Assert.False(batch.IsVoided);
        Assert.Equal(receipt.BatchNumber, batch.BatchNumber);
        Assert.Equal(1, await db.InventoryBatches.CountAsync(b => b.ShopId == shop.ShopId));
        await AssertPayableAsync(receipt.BatchId, 800m);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 10m);
    }

    [Fact]
    public async Task ThreeDecimalBatch_CanBeCountedToZero()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 2.375m, 80m);

        var response = await CountAsync(client, shop.OwnerToken, receipt.BatchId, await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId), 1.25m, "Weighed on the scale");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(-1.125m, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("delta").GetDecimal());

        var removal = await CountAsync(client, shop.OwnerToken, receipt.BatchId, await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId), 0m, "Spoiled");
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adjustments = await db.InventoryAdjustments.Where(a => a.InventoryBatchId == receipt.BatchId).OrderBy(a => a.PerformedAt).ToListAsync();
        Assert.Equal(1.125m, adjustments[0].Quantity);
        Assert.Equal(1.25m, adjustments[1].Quantity);
        Assert.Equal(2.375m, adjustments[0].BatchQuantityBefore);
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 0m);
    }

    [Fact]
    public async Task LegacyBatchCorrection_FromStaleForm_IsRefusedAndSaleKept()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 80m);
        var shownVersion = await VersionOfAsync(client, shop.OwnerToken, receipt.BatchId);
        await SellAsync(client, shop.OwnerToken, receipt, 3m);

        using var editRequest = Authorized(HttpMethod.Put, $"/api/inventory/batches/{receipt.BatchId}", shop.OwnerToken);
        editRequest.Content = JsonContent.Create(new
        {
            newBatchNumber = $"{receipt.BatchNumber}-C",
            quantity = 10m,
            costPrice = 80m,
            mrp = 150m,
            salesPrice = 140m,
            taxRatePercent = 0m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = receipt.SupplierId,
            notes = "Edited from a form opened before the sale",
            entryDate = (DateOnly?)null,
            expectedVersion = shownVersion,
        });
        var editResponse = await client.SendAsync(editRequest);

        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
        Assert.Equal("Inventory.BatchVersionConflict", (await editResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        await AssertReconciledAsync(shop.ShopId, receipt.ItemId, expectedQuantity: 7m);
    }

    private async Task AssertReconciledAsync(Guid shopId, Guid itemId, decimal expectedQuantity)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var movements = await db.StockTransactions.Where(t => t.ShopId == shopId && t.ItemId == itemId).SumAsync(t => t.Quantity);
        var aggregate = (await db.Inventory.SingleAsync(i => i.ShopId == shopId && i.ItemId == itemId)).Quantity;
        var batches = await db.InventoryBatches.Where(b => b.ShopId == shopId && b.ItemId == itemId && !b.IsVoided).SumAsync(b => b.Quantity);

        Assert.Equal(expectedQuantity, aggregate);
        Assert.Equal(aggregate, movements);
        Assert.Equal(aggregate, batches);
    }

    private async Task AssertPayableAsync(Guid batchId, decimal expected)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entries = await db.SupplierLedgerEntries.Where(e => e.BatchId == batchId).ToListAsync();
        Assert.Equal(expected, entries.Sum(e => e.Amount));
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<HttpResponseMessage> CountAsync(
        HttpClient client, string token, Guid batchId, uint expectedVersion, decimal countedQuantity, string reason, string? operationKey = null)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/inventory/batches/{batchId}/count", token);
        request.Content = JsonContent.Create(new { expectedVersion, countedQuantity, reason, operationKey });
        return await client.SendAsync(request);
    }

    private static async Task<uint> VersionOfAsync(HttpClient client, string token, Guid batchId)
    {
        using var request = Authorized(HttpMethod.Get, "/api/inventory/batches", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var batches = await response.Content.ReadFromJsonAsync<JsonElement>();
        return batches.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batchId).GetProperty("version").GetUInt32();
    }

    private sealed record ShopContext(Guid ShopId, Guid OwnerId, string OwnerToken);

    private sealed record Receipt(Guid ItemId, Guid BatchId, string Barcode, string BatchNumber, Guid SupplierId);

    private static async Task<ShopContext> CreateShopAsync(HttpClient client)
    {
        var enrollment = await client.EnrollAsync(new
        {
            email = $"stock-count-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Count",
            lastName = "Owner",
            phoneNumber = UniquePhone(),
        });
        enrollment.EnsureSuccessStatusCode();
        var enrollmentBody = await enrollment.Content.ReadFromJsonAsync<JsonElement>();
        var ownerId = enrollmentBody.GetProperty("user").GetProperty("id").GetGuid();

        using var request = Authorized(HttpMethod.Post, "/api/shops", enrollmentBody.GetProperty("accessToken").GetString()!);
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
        return new ShopContext(body.GetProperty("activeShopId").GetGuid(), ownerId, body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<Receipt> ReceiveAsync(HttpClient client, string token, decimal quantity, decimal unitCost)
    {
        using var supplierRequest = Authorized(HttpMethod.Post, "/api/suppliers", token);
        supplierRequest.Content = JsonContent.Create(new
        {
            name = $"Supplier {Guid.NewGuid():N}",
            contactPersonName = "Supplier Contact",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            amount = 0m,
            status = 0,
            isActive = true,
            isPreferred = false,
        });
        var supplierResponse = await client.SendAsync(supplierRequest);
        supplierResponse.EnsureSuccessStatusCode();
        var supplierId = (await supplierResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("supplierId").GetGuid();

        var barcode = $"COUNT-{Guid.NewGuid():N}";
        var batchNumber = $"B-{Guid.NewGuid():N}"[..20];
        using var request = Authorized(HttpMethod.Post, "/api/inventory/inbound", token);
        request.Content = JsonContent.Create(new
        {
            itemName = $"Item {barcode}",
            barcode,
            itemDescription = (string?)null,
            uom = "pcs",
            batchNumber,
            quantity,
            totalPurchaseCost = quantity * unitCost,
            mrp = 150m,
            salesPrice = 140m,
            taxRatePercent = 0m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId,
            referenceNumber = "GRN-1",
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Receipt(body.GetProperty("itemId").GetGuid(), body.GetProperty("inventoryBatchId").GetGuid(), barcode, batchNumber, supplierId);
    }

    private static async Task SellAsync(HttpClient client, string token, Receipt receipt, decimal quantity)
    {
        using var request = Authorized(HttpMethod.Post, "/api/sales", token);
        request.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Walk-in",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 140m * quantity,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode = receipt.Barcode,
                    batchNumber = receipt.BatchNumber,
                    itemName = $"Item {receipt.Barcode}",
                    quantity,
                    costPrice = 80m,
                    salesPrice = 140m,
                    mrp = 150m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = receipt.BatchId,
                },
            },
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> AddMemberAndLoginAsync(HttpClient client, string ownerToken, Guid shopId, string role)
    {
        var email = $"stock-count-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.com";
        const string password = "StaffPass1!";

        using var request = Authorized(HttpMethod.Post, "/api/users", ownerToken);
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
        var response = await client.SeedVerifiedStaffAsync(request);
        response.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/email", new { email, password });
        loginResponse.EnsureSuccessStatusCode();
        return (await loginResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(6_000_000_000, 9_999_999_999)}";

    /// <summary>
    /// Fails the save that stages a stock count with an armed reason, after the handler has already
    /// changed the batch, the aggregate and staged the movement and adjustment: the latest point at
    /// which a count can fail.
    /// </summary>
    private sealed class FailingStockCountInterceptor : SaveChangesInterceptor
    {
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();

        public void Arm(string reason)
        {
            lock (_gate)
                _pending.Add(reason);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var staged = eventData.Context?.ChangeTracker.Entries<StockCount>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity.Reason)
                .ToArray() ?? [];

            lock (_gate)
            {
                foreach (var reason in staged)
                {
                    if (_pending.Remove(reason))
                        throw new InvalidOperationException("Simulated audit failure.");
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
