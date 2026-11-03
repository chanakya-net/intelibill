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

/// <summary>
/// Proves against PostgreSQL that a retail price edit keeps the physical lot, its stock and its
/// receipt payable, and that a receipt cost correction posts only its difference, leaving every
/// sale's cost snapshot and the lot's returns intact.
/// </summary>
/// <remarks>
/// Every assertion about stored rows reads through a fresh context, so it sees what was committed.
/// </remarks>
[Collection("Integration Tests")]
public sealed class PurchaseCostLineageTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    [Fact]
    public async Task PriceOnlyEdit_PreservesBatchAndPayable()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var saleId = await SellAsync(client, shop.OwnerToken, receipt, 4m);
        var soldCost = await SoldCostAsync(saleId);

        var response = await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, (await BatchAsync(client, shop.OwnerToken, receipt.BatchId)).Version, salesPrice: 130m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.InventoryBatches.SingleAsync(b => b.ShopId == shop.ShopId);
        Assert.Equal(receipt.BatchId, batch.Id);
        Assert.False(batch.IsVoided);
        Assert.Equal(6m, batch.Quantity);
        Assert.Equal(130m, batch.SalesPrice);
        Assert.Equal(100m, batch.CostPrice);
        Assert.Equal(soldCost, await SoldCostAsync(saleId));

        var revision = await db.BatchPriceRevisions.SingleAsync(r => r.InventoryBatchId == receipt.BatchId);
        Assert.Equal(140m, revision.PreviousSalesPrice);
        Assert.Equal(130m, revision.NewSalesPrice);
        Assert.Equal(shop.OwnerId, revision.PerformedBy);
        await AssertPayableAsync(receipt.BatchId, 1000m);
    }

    [Fact]
    public async Task ReturnAfterPriceEdit_RestocksOriginalLot()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var saleId = await SellAsync(client, shop.OwnerToken, receipt, 4m);
        var edit = await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, (await BatchAsync(client, shop.OwnerToken, receipt.BatchId)).Version, salesPrice: 120m);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var returnResponse = await ReturnAsync(client, shop.OwnerToken, saleId, 1m);

        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var active = await db.InventoryBatches.Where(b => b.ShopId == shop.ShopId && !b.IsVoided).ToListAsync();
        var batch = Assert.Single(active);
        Assert.Equal(receipt.BatchId, batch.Id);
        Assert.Equal(7m, batch.Quantity);
        await AssertPayableAsync(receipt.BatchId, 1000m);
    }

    [Fact]
    public async Task TwoPriceEditsAgainstVersion_OneConflicts()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var shownVersion = (await BatchAsync(client, shop.OwnerToken, receipt.BatchId)).Version;

        var first = await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, shownVersion, salesPrice: 130m);
        var second = await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, shownVersion, salesPrice: 135m);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Inventory.BatchVersionConflict", await TitleAsync(second));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(130m, (await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId)).SalesPrice);
        Assert.Equal(1, await db.InventoryBatches.CountAsync(b => b.ShopId == shop.ShopId));
        Assert.Equal(1, await db.BatchPriceRevisions.CountAsync(r => r.InventoryBatchId == receipt.BatchId));
        await AssertPayableAsync(receipt.BatchId, 1000m);
    }

    [Fact]
    public async Task UnchangedPricesAgainstStaleVersion_StillConflicts()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var shownVersion = (await BatchAsync(client, shop.OwnerToken, receipt.BatchId)).Version;
        await SellAsync(client, shop.OwnerToken, receipt, 1m);

        var response = await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, shownVersion, salesPrice: 140m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PartlySoldReceiptCostCorrection_PostsOnlyHundredDelta()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var saleId = await SellAsync(client, shop.OwnerToken, receipt, 4m);
        var soldCost = await SoldCostAsync(saleId);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        Assert.Equal("Resolved", batch.ReceiptStatus);
        Assert.Equal(1000m, batch.ReceiptTotalCost);

        var response = await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Supplier invoice was ₹1100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1000m, body.GetProperty("priorTotalCost").GetDecimal());
        Assert.Equal(1100m, body.GetProperty("revisedTotalCost").GetDecimal());
        Assert.Equal(100m, body.GetProperty("delta").GetDecimal());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId);
        Assert.False(stored.IsVoided);
        Assert.Equal(6m, stored.Quantity);
        Assert.Equal(100m, stored.CostPrice);
        Assert.Equal(soldCost, await SoldCostAsync(saleId));

        var adjustment = await db.PurchaseReceiptCostAdjustments.SingleAsync(a => a.InventoryBatchId == receipt.BatchId);
        Assert.Equal(100m, adjustment.Delta);
        Assert.Equal("Supplier invoice was ₹1100", adjustment.Reason);
        Assert.Equal(shop.OwnerId, adjustment.PerformedBy);
        var ledger = await db.SupplierLedgerEntries.SingleAsync(e => e.Id == adjustment.SupplierLedgerEntryId);
        Assert.Equal(SupplierLedgerEntryType.CostAdjusted, ledger.EntryType);
        Assert.Equal(100m, ledger.Amount);
        Assert.Equal(receipt.SupplierId, ledger.SupplierId);

        var value = await db.PurchaseReceiptValues.SingleAsync(r => r.InventoryBatchId == receipt.BatchId);
        Assert.Equal(1000m, value.OriginalTotalCost);
        Assert.Equal(1100m, value.EffectiveTotalCost);
        await AssertPayableAsync(receipt.BatchId, 1100m);
    }

    [Fact]
    public async Task SuccessivePriceThenCostEdits_NoDuplicateLiability()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        await SellAsync(client, shop.OwnerToken, receipt, 4m);

        Assert.Equal(HttpStatusCode.OK, (await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, (await BatchAsync(client, shop.OwnerToken, receipt.BatchId)).Version, salesPrice: 130m)).StatusCode);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        Assert.Equal(HttpStatusCode.OK, (await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdatePricesAsync(client, shop.OwnerToken, receipt.BatchId, (await BatchAsync(client, shop.OwnerToken, receipt.BatchId)).Version, salesPrice: 125m)).StatusCode);
        batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        Assert.Equal(HttpStatusCode.OK, (await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1050m, "Credit for damaged cartons")).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entries = await db.SupplierLedgerEntries.Where(e => e.BatchId == receipt.BatchId).OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal([1000m, 100m, -50m], entries.Select(e => e.Amount));
        Assert.Equal(1, entries.Count(e => e.EntryType == SupplierLedgerEntryType.GoodsReceived));
        Assert.Equal(1, await db.InventoryBatches.CountAsync(b => b.ShopId == shop.ShopId));
        Assert.Equal(6m, (await db.InventoryBatches.SingleAsync(b => b.Id == receipt.BatchId)).Quantity);
        await AssertPayableAsync(receipt.BatchId, 1050m);
    }

    [Fact]
    public async Task MissingLegacyReceiptEvidence_BlocksFinancialCorrection()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        await MarkLegacyUnresolvedAsync(receipt.BatchId);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        Assert.Equal("LegacyUnresolved", batch.ReceiptStatus);
        Assert.Null(batch.ReceiptTotalCost);

        var response = await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PurchaseReceipt.EvidenceMissing", await TitleAsync(response));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.PurchaseReceiptCostAdjustments.AnyAsync(a => a.InventoryBatchId == receipt.BatchId));
        await AssertPayableAsync(receipt.BatchId, 1000m);
    }

    [Fact]
    public async Task CostCorrection_KeyedResend_ReplaysWithoutSecondDelta()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        var key = $"receipt-cost-{Guid.NewGuid():N}";

        var first = await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice", key);
        var resend = await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice", key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("adjustmentId").GetGuid();
        var resendId = (await resend.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("adjustmentId").GetGuid();
        Assert.Equal(firstId, resendId);
        await AssertPayableAsync(receipt.BatchId, 1100m);
    }

    [Fact]
    public async Task CostCorrection_StaleReceiptVersion_Conflicts()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);

        var first = await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice");
        var stale = await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1200m, "Second invoice");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("PurchaseReceipt.VersionConflict", await TitleAsync(stale));
        await AssertPayableAsync(receipt.BatchId, 1100m);
    }

    [Fact]
    public async Task CostCorrection_ByManager_IsForbidden()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        var managerToken = await AddMemberAndLoginAsync(client, shop.OwnerToken, shop.ShopId, "Manager");

        var response = await CorrectCostAsync(client, managerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertPayableAsync(receipt.BatchId, 1000m);
    }

    [Fact]
    public async Task VoidAfterCostCorrection_NetsPayableToZero()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);
        var batch = await BatchAsync(client, shop.OwnerToken, receipt.BatchId);
        Assert.Equal(HttpStatusCode.OK, (await CorrectCostAsync(client, shop.OwnerToken, batch.ReceiptValueId!.Value, batch.ReceiptVersion!.Value, 1100m, "Invoice")).StatusCode);

        using var voidRequest = Authorized(HttpMethod.Post, $"/api/inventory/batches/{receipt.BatchId}/void", shop.OwnerToken);
        voidRequest.Content = JsonContent.Create(new { });
        var voidResponse = await client.SendAsync(voidRequest);

        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        await AssertPayableAsync(receipt.BatchId, 0m);
    }

    [Fact]
    public async Task VoidOfRoundedReceipt_ReversesThePostedTotal()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);

        // 3 for ₹100.00: the unit cost rounds to ₹33.33, but ₹100.00 was posted.
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 3m, 100m);

        using var voidRequest = Authorized(HttpMethod.Post, $"/api/inventory/batches/{receipt.BatchId}/void", shop.OwnerToken);
        voidRequest.Content = JsonContent.Create(new { });
        var voidResponse = await client.SendAsync(voidRequest);

        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        await AssertPayableAsync(receipt.BatchId, 0m);
    }

    [Fact]
    public async Task PriceEdit_WithoutExpectedVersion_IsMalformedNotConflict()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var receipt = await ReceiveAsync(client, shop.OwnerToken, 10m, 1000m);

        using var request = Authorized(HttpMethod.Put, $"/api/inventory/batches/{receipt.BatchId}/prices", shop.OwnerToken);
        request.Content = JsonContent.Create(new { mrp = 150m, salesPrice = 130m, taxRatePercent = 0m, taxIncluded = false });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Inventory.PriceVersionRequired", await TitleAsync(response));
    }

    [Fact]
    public async Task Backfill_ResolvesClearReceiptsAndLeavesLegacyCorrectionsUnresolved()
    {
        using var client = CreateClient();
        var shop = await CreateShopAsync(client);
        var clear = await ReceiveAsync(client, shop.OwnerToken, 3m, 100m);
        var replaced = await ReceiveAsync(client, shop.OwnerToken, 6m, 660m);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Both as they stood before receipt values; the second as the old destructive PUT left a
            // replacement batch: its only movement is the correction's own.
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM purchase_receipt_values WHERE inventory_batch_id IN ({clear.BatchId}, {replaced.BatchId})");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE stock_transactions SET reference_number = 'Batch Correction' WHERE inventory_batch_id = {replaced.BatchId}");

            var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "scripts", "db", "backfill-purchase-receipt-values.sql"));
            await db.Database.ExecuteSqlRawAsync(script);
            Assert.Equal(0, await db.Database.ExecuteSqlRawAsync(script));
        }

        await using var verify = _factory.Services.CreateAsyncScope();
        var stored = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var resolved = await stored.PurchaseReceiptValues.SingleAsync(r => r.InventoryBatchId == clear.BatchId);
        Assert.Equal(PurchaseReceiptValueStatus.Resolved, resolved.Status);
        Assert.Equal(PurchaseReceiptSource.LegacyBackfill, resolved.Source);
        Assert.Equal(100m, resolved.OriginalTotalCost);
        Assert.Equal(clear.SupplierId, resolved.SupplierId);

        var unresolved = await stored.PurchaseReceiptValues.SingleAsync(r => r.InventoryBatchId == replaced.BatchId);
        Assert.Equal(PurchaseReceiptValueStatus.LegacyUnresolved, unresolved.Status);
        Assert.Null(unresolved.OriginalTotalCost);
        Assert.Null(unresolved.EffectiveTotalCost);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private async Task<decimal> SoldCostAsync(Guid saleId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId)).Items.Single().CostPrice;
    }

    private async Task MarkLegacyUnresolvedAsync(Guid batchId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE purchase_receipt_values
            SET status = 'LEGACY_UNRESOLVED', source = 'LEGACY_BACKFILL', original_total_cost = NULL, effective_total_cost = NULL
            WHERE inventory_batch_id = {batchId}
            """);
        Assert.Equal(1, updated);
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

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();

    private static async Task<HttpResponseMessage> UpdatePricesAsync(
        HttpClient client, string token, Guid batchId, uint expectedVersion, decimal salesPrice)
    {
        using var request = Authorized(HttpMethod.Put, $"/api/inventory/batches/{batchId}/prices", token);
        request.Content = JsonContent.Create(new
        {
            expectedVersion,
            mrp = 150m,
            salesPrice,
            taxRatePercent = 0m,
            taxIncluded = false,
            batchNumber = (string?)null,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            reason = "Price change",
        });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CorrectCostAsync(
        HttpClient client, string token, Guid receiptValueId, uint expectedVersion, decimal revisedTotalCost, string reason, string? operationKey = null)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/inventory/receipts/{receiptValueId}/cost-corrections", token);
        request.Content = JsonContent.Create(new { expectedVersion, revisedTotalCost, reason, operationKey });
        return await client.SendAsync(request);
    }

    private static async Task<BatchView> BatchAsync(HttpClient client, string token, Guid batchId)
    {
        using var request = Authorized(HttpMethod.Get, "/api/inventory/batches", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var batches = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batch = batches.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == batchId);
        return new BatchView(
            batch.GetProperty("version").GetUInt32(),
            batch.GetProperty("receiptValueId").ValueKind == JsonValueKind.Null ? null : batch.GetProperty("receiptValueId").GetGuid(),
            batch.GetProperty("receiptStatus").ValueKind == JsonValueKind.Null ? null : batch.GetProperty("receiptStatus").GetString(),
            batch.GetProperty("receiptTotalCost").ValueKind == JsonValueKind.Null ? null : batch.GetProperty("receiptTotalCost").GetDecimal(),
            batch.GetProperty("receiptVersion").ValueKind == JsonValueKind.Null ? null : batch.GetProperty("receiptVersion").GetUInt32());
    }

    private sealed record BatchView(uint Version, Guid? ReceiptValueId, string? ReceiptStatus, decimal? ReceiptTotalCost, uint? ReceiptVersion);

    private sealed record ShopContext(Guid ShopId, Guid OwnerId, string OwnerToken);

    private sealed record Receipt(Guid ItemId, Guid BatchId, string Barcode, string BatchNumber, Guid SupplierId);

    private static async Task<ShopContext> CreateShopAsync(HttpClient client)
    {
        var enrollment = await client.EnrollAsync(new
        {
            email = $"lineage-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Lineage",
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
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new ShopContext(body.GetProperty("activeShopId").GetGuid(), ownerId, body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<Receipt> ReceiveAsync(HttpClient client, string token, decimal quantity, decimal totalPurchaseCost)
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

        var barcode = $"LIN-{Guid.NewGuid():N}";
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
            totalPurchaseCost,
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

    private static async Task<Guid> SellAsync(HttpClient client, string token, Receipt receipt, decimal quantity)
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
                    costPrice = 100m,
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
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid();
    }

    private async Task<HttpResponseMessage> ReturnAsync(HttpClient client, string token, Guid saleId, decimal quantity)
    {
        Guid saleItemId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            saleItemId = (await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId)).Items.Single().Id;
        }

        using var request = Authorized(HttpMethod.Post, $"/api/sales/{saleId}/returns", token);
        request.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Customer returned one",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 140m * quantity,
                    notes = (string?)null,
                },
            },
        });
        return await client.SendAsync(request);
    }

    private static async Task<string> AddMemberAndLoginAsync(HttpClient client, string ownerToken, Guid shopId, string role)
    {
        var email = $"lineage-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.com";
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
}
