using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryOfflineApiTests : IAsyncLifetime, IDisposable
{
    private static readonly string[] PostedStatuses = ["created", "syncedWithWarnings"];
    private static readonly string[] EscalateOnly = ["Escalate"];
    private readonly ApiWebApplicationFactory _factory;
    private readonly string _migratorConnection;
    private readonly TestTime _time = new();
    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public ExpiryOfflineApiTests(PostgreSqlTestFixture fixture)
    {
        _migratorConnection = fixture.MigratorConnectionString;
        _factory = new(fixture, services =>
        {
        services.RemoveAll<IBusinessClock>(); services.AddSingleton<IBusinessClock>(new BusinessClock(_time));
        services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(_time);
        });
    }
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }
    private async Task<(HttpClient Client, Guid Shop, Guid Actor, Guid Batch, JsonObject Sale)> Seed(bool activatePolicy = true)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var enrolled = await client.EnrollAsync(new { email = $"expiry-offline-api-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Expiry", lastName = "Owner" });
        Assert.True(enrolled.IsSuccessStatusCode, await enrolled.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new { name = "Offline expiry", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001", taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" } });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Shops.Where(x => x.Name == "Offline expiry").OrderByDescending(x => x.CreatedAt).FirstAsync();
        var actor = await db.ShopMemberships.Where(x => x.ShopId == shop.Id && x.Role == ShopRole.Owner).Select(x => x.UserId).SingleAsync();
        var barcode = Guid.NewGuid().ToString("N"); var expiry = new BusinessClock(_time).Today("Asia/Kolkata").AddDays(1);
        using var inbound = await client.PostAsJsonAsync("/api/inventory/inbound", new { itemName = "Offline expiry item", barcode, uom = "pcs", batchNumber = "lot", quantity = 10m, totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = expiry });
        Assert.True(inbound.IsSuccessStatusCode, await inbound.Content.ReadAsStringAsync());
        var batch = (await inbound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inventoryBatchId").GetGuid();
        if (activatePolicy)
        {
        using var policy = await client.PutAsJsonAsync("/api/expiry-policies", new { expectedCatalogVersion = 1, policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "test policy" });
        Assert.True(policy.IsSuccessStatusCode, await policy.Content.ReadAsStringAsync());
        }
        using var leaseResponse = await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = "expiry-device", blockSize = 50 });
        Assert.True(leaseResponse.IsSuccessStatusCode, await leaseResponse.Content.ReadAsStringAsync());
        var lease = await leaseResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var snapshot = await client.GetAsync("/api/sales/offline-snapshot/stream");
        var records = (await snapshot.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => JsonNode.Parse(x)!.AsObject()).ToArray();
        Assert.Equal("complete", records[^1]["type"]!.GetValue<string>());
        var metadata = records[0]["metadata"]!; Assert.Equal(4, metadata["schemaVersion"]!.GetValue<int>());
        var lot = records.Single(x => x["type"]!.GetValue<string>() == "batch")["batch"]!;
        var number = lease.GetProperty("prefix").GetString() + lease.GetProperty("rangeStart").GetInt32().ToString("D6", CultureInfo.InvariantCulture);
        var sale = new JsonObject
        {
            ["clientSaleId"] = Guid.NewGuid().ToString(), ["invoiceNumber"] = number, ["soldAt"] = _time.Now,
            ["paymentMethod"] = "Cash", ["paidAmount"] = 2m, ["dueAmount"] = 0m, ["subtotalBeforeDiscount"] = 2m,
            ["totalBeforeDiscount"] = 2m, ["totalDiscountAmount"] = 0m, ["totalTaxAmount"] = 0m, ["totalAmount"] = 2m,
            ["saleDiscountOverrideType"] = "None", ["saleDiscountOverrideValue"] = 0m, ["calculationVersion"] = 1,
            ["issuedByUserId"] = actor,
            ["items"] = new JsonArray(new JsonObject
            {
                ["barcode"] = barcode, ["batchNumber"] = "lot", ["itemName"] = "Offline expiry item", ["quantity"] = 1m,
                ["costPrice"] = 1m, ["salesPrice"] = 2m, ["mrp"] = 2m, ["taxRatePercent"] = 0m, ["isPriceIncludingTax"] = false,
                ["inventoryBatchId"] = batch, ["preTaxAmountBeforeDiscount"] = 2m, ["itemDiscountAmount"] = 0m, ["saleDiscountAmount"] = 0m,
                ["taxableAmount"] = 2m, ["taxAmount"] = 0m, ["totalAmount"] = 2m, ["itemDiscountOverrideType"] = "None", ["itemDiscountOverrideValue"] = 0m,
                ["expiryEvidence"] = new JsonObject { ["evidenceId"] = lot["expiryEvidenceId"]!.DeepClone(), ["catalogVersion"] = metadata["catalogVersion"]!.DeepClone(),
                    ["policyVersion"] = lot["effectivePolicy"]!["version"]!.DeepClone(), ["businessDate"] = expiry.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["expiryDate"] = expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["decisionCode"] = activatePolicy ? "Expiry.ExpiringSoon" : "Allowed" }
            })
        };
        return (client, shop.Id, actor, batch, sale);
    }
    private static async Task<JsonElement> Sync(HttpClient client, JsonObject sale)
    {
        using var response = await client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = "expiry-device", sales = new[] { sale } });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0].Clone();
    }
    private async Task<(int Sales, int Stock, int Ledger, int Till, decimal Quantity)> Facts(Guid shopId, Guid batch)
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Sales.CountAsync(x => x.ShopId == shopId), await db.StockTransactions.CountAsync(x => x.ShopId == shopId), await db.CustomerLedgerEntries.CountAsync(x => x.ShopId == shopId), await db.Set<TillSourceRecord>().CountAsync(x => x.ShopId == shopId), await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Quantity).SingleAsync());
    }
    [Fact]
    public async Task OfflineIssueBeforeExpiry_SyncAfterExpiry_PostsFrozenInvoiceAndReplaysOriginalDecisions()
    {
        var seed = await Seed(); using var client = seed.Client;
        _time.Now = _time.Now.AddDays(3);
        var result = await Sync(client, seed.Sale);
        Assert.Contains(result.GetProperty("status").GetString(), PostedStatuses);
        var saleId = result.GetProperty("saleId").GetGuid();
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.SingleAsync(x => x.Id == saleId); var decision = Assert.Single(sale.ExpiryDecisions!);
        Assert.Equal(seed.Sale["invoiceNumber"]!.GetValue<string>(), sale.InvoiceNumber); Assert.Equal(2m, sale.TotalAmount);
        Assert.Equal(2, decision.Policy.Version); Assert.Null(decision.ApprovedBy);
        // Keep this authenticated test device valid while moving the application clock past the proof window.
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET expires_at = {_time.Now.AddDays(30)} WHERE user_id = {seed.Actor}");
        _time.Now = _time.Now.AddDays(10);
        var replay = await Sync(client, seed.Sale); Assert.Equal(saleId, replay.GetProperty("saleId").GetGuid());
        Assert.Equal(9, (await Facts(seed.Shop, seed.Batch)).Quantity);
    }
    [Theory]
    [InlineData("missing")][InlineData("partial")][InlineData("forged")]
    public async Task OrdinarySync_UntrustedProofRetainsEnvelopeAndHasNoPostingEffects(string kind)
    {
        var seed = await Seed(); using var client = seed.Client; var before = await Facts(seed.Shop, seed.Batch);
        var line = seed.Sale["items"]![0]!;
        line["expiryEvidence"] = kind switch { "missing" => null, "partial" => new JsonObject { ["catalogVersion"] = 2 }, _ => new JsonObject { ["evidenceId"] = Guid.NewGuid(), ["catalogVersion"] = 2, ["policyVersion"] = 2, ["businessDate"] = "2026-10-20", ["decisionCode"] = "Allowed" } };
        var result = await Sync(client, seed.Sale); Assert.Equal("NeedsReview", result.GetProperty("status").GetString());
        Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == seed.Shop);
        var issue = await db.ReconciliationIssues.SingleAsync(x => x.OfflineSubmissionId == envelope.Id);
        Assert.Equal(ReconciliationIssueType.ExpiryReview, issue.IssueType); Assert.Null(envelope.SaleId);
        using var detailResponse = await client.GetAsync($"/api/offline-reconciliation/{issue.Id}"); var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(EscalateOnly, detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        foreach (var action in new[] { "RetryHistoricalLease", "ReconcileTillSale", "LinkStockCorrection", "AcknowledgeVariance", "LinkExistingSale" })
        {
            using var resolved = await client.PostAsJsonAsync($"/api/offline-reconciliation/{issue.Id}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = issue.Version, action, reason = "Manager reason cannot manufacture authority", linkedOperationId = Guid.NewGuid(), tillReconciliationEvidence = new { evidenceStatus = "Unknown", tenderLines = Array.Empty<object>(), reason = "legacy" } });
            Assert.Equal(HttpStatusCode.BadRequest, resolved.StatusCode);
        }
        Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
    }
    [Fact]
    public async Task RecoverySubmission_UntrustedProofRetainsExpiryReviewWithoutEffects()
    {
        var seed = await Seed(); using var client = seed.Client;
        seed.Sale["items"]![0]!["expiryEvidence"]!["evidenceId"] = Guid.NewGuid();
        var before = await Facts(seed.Shop, seed.Batch);
        var invoices = new JsonArray(seed.Sale.DeepClone()); var json = invoices.ToJsonString();
        using var response = await client.PostAsJsonAsync("/api/offline-reconciliation/recovery-submissions", new
        {
            operationId = Guid.NewGuid().ToString(), bundle = new { format = "intelibill.offline-recovery", version = 1, shopId = seed.Shop,
                deviceId = "expiry-device", exportedAt = _time.Now, checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), invoices }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var issue = await db.ReconciliationIssues.SingleAsync(x => x.ShopId == seed.Shop);
        Assert.Equal(ReconciliationIssueType.ExpiryReview, issue.IssueType);
        Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
    }

    private async Task<Guid> Recover(HttpClient client, Guid shopId, JsonObject sale)
    {
        var invoices = new JsonArray(sale.DeepClone());
        using var response = await client.PostAsJsonAsync("/api/offline-reconciliation/recovery-submissions", new
        {
            operationId = Guid.NewGuid().ToString(), bundle = new { format = "intelibill.offline-recovery", version = 1, shopId,
                deviceId = "expiry-device", exportedAt = _time.Now, checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invoices.ToJsonString()))), invoices }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ReconciliationIssues.Where(x => x.ShopId == shopId).Select(x => x.Id).SingleAsync();
    }
    private static async Task<JsonElement> Detail(HttpClient client, Guid caseId)
    {
        using var response = await client.GetAsync($"/api/offline-reconciliation/{caseId}"); Assert.True(response.IsSuccessStatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
    [Fact]
    public async Task HistoricalRetry_PreservesProofAndNeverConsumesCurrentQuarantine()
    {
        var seed = await Seed(); using var client = seed.Client;
        var caseId = await Recover(client, seed.Shop, seed.Sale); var detail = await Detail(client, caseId);
        Assert.Contains("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlAsync($"UPDATE inventory_batches SET quarantined_quantity = quantity WHERE id = {seed.Batch}");
        _time.Now = _time.Now.AddDays(3);
        using var retry = await client.PostAsJsonAsync($"/api/offline-reconciliation/{caseId}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "RetryHistoricalLease", reason = "historical issue supported by retained proof" });
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        using var check = _factory.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.SingleAsync(x => x.ShopId == seed.Shop); Assert.Single(sale.ExpiryDecisions!);
        var batch = await db.InventoryBatches.SingleAsync(x => x.Id == seed.Batch); Assert.Equal((10m, 10m), (batch.Quantity, batch.QuarantinedQuantity));
        Assert.Contains(await db.ReconciliationIssues.Where(x => x.ShopId == seed.Shop).ToListAsync(), x => x.IssueType == ReconciliationIssueType.StockVariance);
        Assert.Equal(seed.Sale["invoiceNumber"]!.GetValue<string>(), sale.InvoiceNumber);
    }
    [Fact]
    public async Task ExpiryReview_LinkExistingSaleRequiresExactRetainedDecisions()
    {
        var seed = await Seed(); using var client = seed.Client; var result = await Sync(client, seed.Sale);
        Assert.Contains(result.GetProperty("status").GetString(), PostedStatuses); var saleId = result.GetProperty("saleId").GetGuid();
        Guid caseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == seed.Shop);
            var issue = ReconciliationIssue.Create(seed.Shop, null, envelope.ClientSaleId, envelope.DeviceId, ReconciliationIssueType.ExpiryReview, "Expiry.ReviewRequired", "retained historical proof review", seed.Actor);
            issue.AttachEvidence(envelope.InvoiceNumber, envelope.Id); db.Add(issue); await db.SaveChangesAsync(); caseId = issue.Id;
        }
        var detail = await Detail(client, caseId); Assert.Contains("LinkExistingSale", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(detail.GetProperty("linkCandidates").EnumerateArray(), x => x.GetProperty("operationId").GetGuid() == saleId);
        var before = await Facts(seed.Shop, seed.Batch);
        using var response = await client.PostAsJsonAsync($"/api/offline-reconciliation/{caseId}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "LinkExistingSale", reason = "exact posted evidence", linkedOperationId = saleId });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
    }
    [Fact]
    public async Task PreviouslyOfferedExpiryRetry_RechecksRetainedCatalogAtExecution()
    {
        var seed = await Seed(); using var client = seed.Client; Guid caseId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Import first to retain authenticated attribution without posting anything.
            caseId = await Recover(client, seed.Shop, seed.Sale);
            var issue = await db.ReconciliationIssues.SingleAsync(x => x.Id == caseId);
            await db.Database.ExecuteSqlAsync($"UPDATE reconciliation_issues SET issue_type = {(int)ReconciliationIssueType.ExpiryReview}, code = 'Expiry.ReviewRequired' WHERE id = {issue.Id}");
        }
        var offered = await Detail(client, caseId); Assert.Contains("RetryHistoricalLease", offered.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        var before = await Facts(seed.Shop, seed.Batch);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlAsync($"UPDATE shop_expiry_states SET catalog_version = catalog_version + 1 WHERE shop_id = {seed.Shop}");
        foreach (var action in new[] { "RetryHistoricalLease", "ReconcileTillSale" })
        {
            using var response = await client.PostAsJsonAsync($"/api/offline-reconciliation/{caseId}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = offered.GetProperty("version").GetUInt32(), action, reason = "stale offered action", tillReconciliationEvidence = new { evidenceStatus = "Unknown", tenderLines = Array.Empty<object>(), reason = "legacy" } });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
        var current = await Detail(client, caseId); Assert.Equal(EscalateOnly, current.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task NewShopDefaultIgnore_DoesNotManufactureLegacyHistory()
    {
        var seed = await Seed(activatePolicy: false); using var client = seed.Client;
        seed.Sale["items"]![0]!.AsObject().Remove("expiryEvidence");
        var before = await Facts(seed.Shop, seed.Batch); var result = await Sync(client, seed.Sale);
        Assert.Equal("NeedsReview", result.GetProperty("status").GetString()); Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(ReconciliationIssueType.ExpiryReview, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ReconciliationIssues.SingleAsync(x => x.ShopId == seed.Shop)).IssueType);
    }

    private async Task<Guid> ExpiryCase(HttpClient client, Guid shop, JsonObject sale)
    {
        var id = await Recover(client, shop, sale);
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlAsync($"UPDATE reconciliation_issues SET issue_type = {(int)ReconciliationIssueType.ExpiryReview}, code = 'Expiry.ReviewRequired' WHERE id = {id}");
        return id;
    }
    [Fact]
    public async Task ExpiryReview_DisabledTillNeverOffersTillReconciliation()
    {
        var seed = await Seed(); using var client = seed.Client; var before = await Facts(seed.Shop, seed.Batch);
        var id = await ExpiryCase(client, seed.Shop, seed.Sale); var detail = await Detail(client, id);
        var actions = detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("RetryHistoricalLease", actions); Assert.DoesNotContain("ReconcileTillSale", actions);
        using var crafted = await client.PostAsJsonAsync($"/api/offline-reconciliation/{id}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "ReconcileTillSale", reason = "disabled till", tillReconciliationEvidence = new { evidenceStatus = "Unknown", tenderLines = Array.Empty<object>(), reason = "legacy" } });
        Assert.Equal(HttpStatusCode.BadRequest, crafted.StatusCode); Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
    }
    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(true, true)]
    public async Task ExpiryReview_TillReconciliationOnlyOfferedAfterOriginalDrawerCloses(bool close, bool disableAfterOffer)
    {
        var seed = await Seed(); using var client = seed.Client;
        var options = _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value; options.Enabled = true; options.AllowedShopIds = [seed.Shop];
        await using var app = new TillTestContext(_migratorConnection, seed.Shop, new() { Now = _time.Now.AddSeconds(-1) });
        var till = await app.CreateAsync(seed.Shop, seed.Actor); var session = await app.OpenAsync(seed.Shop, seed.Actor, till.Id);
        seed.Sale["till"] = new JsonObject { ["sessionId"] = session.Id };
        seed.Sale["tillBusinessDate"] = session.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); seed.Sale["tillCashierId"] = seed.Actor;
        seed.Sale["tenderLines"] = JsonNode.Parse("[{\"method\":2,\"appliedAmount\":2}]");
        if (close)
        {
            app.Time.Now = _time.Now.AddSeconds(1); _time.Now = app.Time.Now;
            var current = await app.Db.TillSessions.AsNoTracking().SingleAsync(x => x.Id == session.Id);
            TillTestContext.Success(await app.Lifecycle.CloseAsync(new(seed.Shop, seed.Actor, session.Id, 1000m, null, current.Version, Guid.NewGuid().ToString()), default));
        }
        var before = await Facts(seed.Shop, seed.Batch); var id = await ExpiryCase(client, seed.Shop, seed.Sale); var detail = await Detail(client, id);
        var actions = detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(close, actions.Contains("ReconcileTillSale")); Assert.Contains("RetryHistoricalLease", actions);
        if (disableAfterOffer) options.Enabled = false;
        using var attempt = await client.PostAsJsonAsync($"/api/offline-reconciliation/{id}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "ReconcileTillSale", reason = "historical till review", tillReconciliationEvidence = new { evidenceStatus = "Recorded", tenderLines = seed.Sale["tenderLines"], reason = "frozen recorded tender" } });
        if (close && !disableAfterOffer)
        {
            Assert.True(attempt.IsSuccessStatusCode, await attempt.Content.ReadAsStringAsync());
            using var verify = _factory.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single((await db.Sales.SingleAsync(x => x.ShopId == seed.Shop)).ExpiryDecisions!); Assert.Single(await db.TillLatePostings.Where(x => x.ShopId == seed.Shop).ToListAsync());
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode); Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
            if (disableAfterOffer)
            {
                Assert.DoesNotContain("ReconcileTillSale", (await Detail(client, id)).GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
                return;
            }
            using var retry = await client.PostAsJsonAsync($"/api/offline-reconciliation/{id}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "RetryHistoricalLease", reason = "original current drawer" });
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            using var verify = _factory.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2m, (await db.TillMovements.SingleAsync(x => x.SessionId == session.Id)).SignedAmount);
        }
    }
    [Fact]
    public async Task ExpiryReview_FormerlyFutureIssueUsesInjectedClockForProjectionAndExecution()
    {
        var seed = await Seed(); using var client = seed.Client; var issued = _time.Now.AddHours(1);
        seed.Sale["soldAt"] = issued; seed.Sale["items"]![0]!["expiryEvidence"]!["businessDate"] = new BusinessClock(_time).GetBusinessDate(issued, "Asia/Kolkata").ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var id = await ExpiryCase(client, seed.Shop, seed.Sale);
        Assert.DoesNotContain("RetryHistoricalLease", (await Detail(client, id)).GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        _time.Now = issued.AddMinutes(1);
        var detail = await Detail(client, id); Assert.Contains("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        using var retry = await client.PostAsJsonAsync($"/api/offline-reconciliation/{id}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "RetryHistoricalLease", reason = "issue instant is historical under host clock" });
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope(); var sale = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Sales.SingleAsync(x => x.ShopId == seed.Shop);
        Assert.Equal(seed.Sale["invoiceNumber"]!.GetValue<string>(), sale.InvoiceNumber); Assert.Equal(2m, sale.TotalAmount); Assert.Equal(issued.UtcTicks / 10, sale.SoldAt.UtcTicks / 10);
    }
    [Theory]
    [InlineData("issuer")][InlineData("lease")]
    public async Task ValidExpiryProof_CannotAuthorizeInvalidIssuerOrInvoiceLease(string mismatch)
    {
        var seed = await Seed(); using var client = seed.Client; var before = await Facts(seed.Shop, seed.Batch);
        if (mismatch == "issuer") seed.Sale["issuedByUserId"] = Guid.NewGuid(); else seed.Sale["invoiceNumber"] = "A/2627/999999";
        var result = await Sync(client, seed.Sale); Assert.Equal("NeedsReview", result.GetProperty("status").GetString());
        Assert.Equal(mismatch == "issuer" ? "OfflineReconciliation.IssuerNotSubmitter" : "Sale.InvoiceLeaseNotFound", result.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
        var detail = await Detail(client, result.GetProperty("reviewCaseId").GetGuid());
        Assert.DoesNotContain("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        using var crafted = await client.PostAsJsonAsync($"/api/offline-reconciliation/{result.GetProperty("reviewCaseId").GetGuid()}/resolve", new { operationId = Guid.NewGuid().ToString(), expectedVersion = detail.GetProperty("version").GetUInt32(), action = "RetryHistoricalLease", reason = "catalog proof is not issuer or lease permission" });
        Assert.False(crafted.IsSuccessStatusCode); Assert.Equal(before, await Facts(seed.Shop, seed.Batch));
    }

}
