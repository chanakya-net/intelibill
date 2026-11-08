using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Expiry;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Services.Expiry;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryOnlineSalesTests : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly TestTime _time = new();
    private readonly SaleBarrier _barrier = new();
    private readonly ProjectionReadBarrier _projectionRead = new();
    public ExpiryOnlineSalesTests(PostgreSqlTestFixture fixture)
    {
        _factory = new(fixture, services =>
        {
            services.RemoveAll<IBusinessClock>();
            services.AddSingleton<IBusinessClock>(new BusinessClock(_time));
            services.AddScoped<IInterceptor>(sp => new ProjectionReadInterceptor(sp.GetRequiredService<IHttpContextAccessor>(), _projectionRead));
            services.RemoveAll<IExpiryPolicyFence>();
            services.AddScoped<IExpiryPolicyFence>(sp => new BarrierFence(new ExpiryPolicyFence(sp.GetRequiredService<ApplicationDbContext>()), sp.GetRequiredService<IHttpContextAccessor>(), _barrier));
        });
    }
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }
    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-21T18:29:59Z", global::System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class SaleBarrier
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class BarrierFence(IExpiryPolicyFence inner, IHttpContextAccessor context, SaleBarrier barrier) : IExpiryPolicyFence
    {
        public async Task<ErrorOr<Success>> AcquireAsync(Guid shopId, bool exclusive, CancellationToken ct)
        {
            var sale = barrier.Enabled && !exclusive && context.HttpContext?.Request.Path == "/api/sales";
            if (sale) barrier.Entered.TrySetResult();
            var result = await inner.AcquireAsync(shopId, exclusive, ct);
            if (sale && !result.IsError) { barrier.Acquired.TrySetResult(); await barrier.Release.Task.WaitAsync(ct); }
            return result;
        }
    }
    private sealed class ProjectionReadBarrier
    {
        public bool Enabled { get; set; }
        public int Paused;
        public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ProjectionReadInterceptor(IHttpContextAccessor context, ProjectionReadBarrier barrier) : DbCommandInterceptor
    {
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            var path = context.HttpContext?.Request.Path.Value;
            if (barrier.Enabled && path is "/api/sales/preview" or "/api/inventory/batches" && command.CommandText.Contains("inventory_batches", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref barrier.Paused, 1, 0) == 0)
            {
                // The SELECT has its PostgreSQL snapshot, but EF has not consumed its batch facts yet.
                barrier.Read.TrySetResult();
                await barrier.Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    private async Task<(HttpClient Client, Guid Shop, Guid Actor)> Shop()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var enrolled = await client.EnrollAsync(new { email = $"expiry-online-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Expiry", lastName = "Owner" });
        Assert.True(enrolled.IsSuccessStatusCode, await enrolled.Content.ReadAsStringAsync());
        var auth = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new { name = "Expiry online", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001", taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" } });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var shop = await created.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", shop.GetProperty("accessToken").GetString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shopId = await db.Shops.Where(x => x.Name == "Expiry online").OrderByDescending(x => x.CreatedAt).Select(x => x.Id).FirstAsync();
        var actor = await db.ShopMemberships.Where(x => x.ShopId == shopId && x.Role == ShopRole.Owner).Select(x => x.UserId).SingleAsync();
        return (client, shopId, actor);
    }
    private static object Intake(string barcode, string number, DateOnly? expiry, DateOnly? manufacturing = null) => new
    {
        itemName = $"Expiry item {barcode}",
        barcode,
        uom = "pcs",
        batchNumber = number,
        quantity = 10m,
        totalPurchaseCost = 10m,
        mrp = 2m,
        salesPrice = 2m,
        taxRatePercent = 0m,
        taxIncluded = false,
        purchaseTaxIncluded = false,
        expiryDate = expiry,
        manufacturingDate = manufacturing,
    };
    private static async Task<Guid> Receive(HttpClient client, string barcode, DateOnly? expiry, string number = "lot")
    {
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", Intake(barcode, number, expiry));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inventoryBatchId").GetGuid();
    }
    private static async Task Policy(HttpClient client, string mode, bool required = false, bool overrides = false, long expected = 1)
    {
        using var result = await client.PutAsJsonAsync("/api/expiry-policies", new { expectedCatalogVersion = expected, policy = new { mode, expiryRequired = required, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = overrides }, reason = "verify selling policy" });
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());
    }
    private static object Line(Guid batch, string barcode, decimal quantity = 1) => new { inventoryBatchId = batch, barcode, batchNumber = "lot", itemName = $"Expiry item {barcode}", quantity, costPrice = 1, salesPrice = 2, mrp = 2, taxRatePercent = 0, isPriceIncludingTax = false, itemDiscount = new { type = "None", value = 0 } };
    private static object Sale(Guid batch, string barcode, string? key = null, object? approvals = null, int duplicate = 1) => new { idempotencyKey = key ?? Guid.NewGuid().ToString(), paymentMethod = "Cash", paidAmount = 2 * duplicate, dueAmount = 0, items = Enumerable.Range(0, duplicate).Select(_ => Line(batch, barcode)).ToArray(), expiryApprovals = approvals };
    private async Task<(int Sales, int Sequences, int Stock, int Ledger, int Till, decimal Quantity, decimal AggregateQuantity)> Facts(Guid shopId, Guid batchId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Sales.CountAsync(x => x.ShopId == shopId), await db.InvoiceSequences.Where(x => x.ShopId == shopId).SumAsync(x => (int?)x.NextNumber) ?? 0,
            await db.StockTransactions.CountAsync(x => x.ShopId == shopId), await db.CustomerLedgerEntries.CountAsync(x => x.ShopId == shopId),
            await db.Set<TillSourceRecord>().CountAsync(x => x.ShopId == shopId), await db.InventoryBatches.Where(x => x.Id == batchId).Select(x => x.Quantity).SingleAsync(), await db.Inventory.Where(x => x.ShopId == shopId).SumAsync(x => x.Quantity));
    }
    private async Task LoginAgain(HttpClient client, Guid actor, Guid shopId)
    {
        using var scope = _factory.Services.CreateScope();
        var email = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(x => x.Id == actor).Select(x => x.Email).SingleAsync();
        using var signed = await client.PostAsJsonAsync("/api/auth/login/email", new { email, password = "Pass123!Aa" });
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await signed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        using var switched = await client.PostAsJsonAsync("/api/shops/switch", new { shopId });
        Assert.True(switched.IsSuccessStatusCode, await switched.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
    }
    [Fact]
    public async Task ExpiryBoundary_UsesShopBusinessDateAcrossBarcodeSearchPreviewAndPost()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2026, 10, 21));
        await Policy(client, "Block");
        foreach (var route in new[] { $"/api/inventory/batches/available?barcode={barcode}", $"/api/inventory/batches/available?searchTerm={barcode}", $"/api/sales/sellables?barcode={barcode}", $"/api/sales/sellables?searchTerm={barcode}" })
            Assert.Single((await client.GetFromJsonAsync<JsonElement>(route)).EnumerateArray());
        using var previewBefore = await client.PostAsJsonAsync("/api/sales/preview", new { saleDiscount = new { type = "None", value = 0 }, items = new[] { Line(batch, barcode) } });
        Assert.True(previewBefore.IsSuccessStatusCode, await previewBefore.Content.ReadAsStringAsync());
        Assert.True((await previewBefore.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiryDecisions")[0].GetProperty("decision").GetProperty("allowed").GetBoolean());
        using var first = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        _time.Now = _time.Now.AddSeconds(1);
        foreach (var route in new[] { $"/api/inventory/batches/available?barcode={barcode}", $"/api/inventory/batches/available?searchTerm={barcode}", $"/api/sales/sellables?barcode={barcode}", $"/api/sales/sellables?searchTerm={barcode}" })
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>(route)).EnumerateArray());
        var diagnostic = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}&includeUnavailable=true");
        Assert.Equal(0m, diagnostic[0].GetProperty("availableQuantity").GetDecimal());
        Assert.Equal("Expiry.Expired", diagnostic[0].GetProperty("sellability").GetProperty("reasonCode").GetString());
        var administrative = await client.GetFromJsonAsync<JsonElement>("/api/inventory/batches");
        var administrativeBatch = administrative.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == batch);
        Assert.Equal(9m, administrativeBatch.GetProperty("quantity").GetDecimal());
        Assert.Equal(0m, administrativeBatch.GetProperty("availableQuantity").GetDecimal());
        using var previewAfter = await client.PostAsJsonAsync("/api/sales/preview", new { saleDiscount = new { type = "None", value = 0 }, items = new[] { Line(batch, barcode) } });
        Assert.False((await previewAfter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiryDecisions")[0].GetProperty("decision").GetProperty("allowed").GetBoolean());
        var before = await Facts(seed.Shop, batch);
        using var refused = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Expiry.Expired", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(before, await Facts(seed.Shop, batch));
    }
    [Theory]
    [InlineData("Block", false, "Expiry.Expired")]
    [InlineData("Ignore", true, "Expiry.RequiredDateMissing")]
    [InlineData("Warn", false, "Expiry.OverrideDisabled")]
    public async Task DirectBatchId_HardBlockCannotBeOverriddenAndHasNoPartialEffects(string mode, bool required, string error)
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, required ? null : new DateOnly(2020, 1, 1));
        await Policy(client, mode, required);
        var before = await Facts(seed.Shop, batch);
        using var result = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode, approvals: new[] { new { inventoryBatchId = batch, quantity = 1, batchVersion = 1, policyVersion = 2, catalogVersion = 2, reason = "forged permission", actorUserId = seed.Actor, approved = true } }));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(error, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(before, await Facts(seed.Shop, batch));
    }
    [Fact]
    public async Task WarningOverride_ExactSummedBindingAndPersistedActorReasonAndReplay()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2020, 1, 1));
        await Policy(client, "Warn", overrides: true);
        var diagnostic = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}");
        var version = diagnostic[0].GetProperty("version").GetUInt32();
        Assert.True(diagnostic[0].GetProperty("sellability").GetProperty("requiresApproval").GetBoolean());
        var picker = await client.GetFromJsonAsync<JsonElement>($"/api/sales/sellables?barcode={barcode}");
        Assert.True(picker[0].GetProperty("sellability").GetProperty("requiresApproval").GetBoolean());
        var approval = new ExpiryApprovalRequest(batch, 2, version, 2, 2, "Owner inspected exact lot");
        var before = await Facts(seed.Shop, batch);
        using var unapproved = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode, duplicate: 2));
        Assert.Equal(HttpStatusCode.Conflict, unapproved.StatusCode);
        Assert.Contains("Expiry.ApprovalRequired", await unapproved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await Facts(seed.Shop, batch));
        foreach (var bad in new[] { new[] { approval with { Quantity = 1 } }, new[] { approval with { BatchVersion = 0 } }, new[] { approval with { PolicyVersion = 1 } }, new[] { approval with { CatalogVersion = 1 } }, new[] { approval with { Reason = " " } }, new[] { approval, approval }, new[] { approval with { InventoryBatchId = Guid.NewGuid() } } })
        {
            using var refused = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode, approvals: bad, duplicate: 2));
            Assert.False(refused.IsSuccessStatusCode);
            Assert.Equal(before, await Facts(seed.Shop, batch));
        }
        // A separate valid Staff session cannot turn a client assertion into approval authority.
        using var staffClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var staffEmail = $"expiry-staff-{Guid.NewGuid():N}@example.test";
        using var staffSeedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new
            {
                email = staffEmail,
                password = "Pass123!Aa",
                firstName = "Expiry",
                lastName = "Staff",
                phoneNumber = (string?)null,
                role = "Staff",
                shopIds = new[] { seed.Shop },
            })
        };
        using var staffSeed = await staffClient.SeedVerifiedStaffAsync(staffSeedRequest);
        var staffId = (await staffSeed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();
        await LoginAgain(staffClient, staffId, seed.Shop);
        var forged = JsonSerializer.SerializeToNode(Sale(batch, barcode, approvals: new[] { approval }, duplicate: 2))!;
        forged["expiryApprovals"]![0]!["approved"] = true; forged["expiryApprovals"]![0]!["actorUserId"] = seed.Actor;
        using var staff = await staffClient.PostAsJsonAsync("/api/sales", forged);
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.Equal(before, await Facts(seed.Shop, batch));
        var key = Guid.NewGuid().ToString(); var payload = Sale(batch, barcode, key, new[] { approval }, 2);
        using var posted = await client.PostAsJsonAsync("/api/sales", payload);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var body = await posted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("expiryDecisions").GetArrayLength());
        Assert.All(body.GetProperty("expiryDecisions").EnumerateArray(), x => { Assert.Equal(seed.Actor, x.GetProperty("approvedBy").GetGuid()); Assert.Equal(approval.Reason, x.GetProperty("approvalReason").GetString()); });
        await Policy(client, "Block", expected: 2);
        using var replay = await client.PostAsJsonAsync("/api/sales", payload);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(body.GetProperty("saleId").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid());
        Assert.Equal(8m, (await Facts(seed.Shop, batch)).Quantity);
        using var persistedScope = _factory.Services.CreateScope();
        var db = persistedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.SingleAsync(x => x.ShopId == seed.Shop);
        Assert.Equal(2, sale.ExpiryDecisions!.Count);
        Assert.All(sale.ExpiryDecisions, x => Assert.Equal(sale.Id, x.SaleId));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE sales SET expiry_decisions = NULL WHERE id = {sale.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM sales WHERE id = {sale.Id}"));
    }
    [Fact]
    public async Task ReceiptPaths_ValidateRequiredAndDatePairsAndBulkRowsRemainIndependent()
    {
        var seed = await Shop(); using var client = seed.Client;
        await Policy(client, "Ignore", required: true);
        var barcode = Guid.NewGuid().ToString("N");
        using var missing = await client.PostAsJsonAsync("/api/inventory/inbound", Intake(barcode, "missing", null));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("Expiry.RequiredDateMissing", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        using var invalid = await client.PostAsJsonAsync("/api/inventory/inbound", Intake(barcode, "invalid", new(2026, 1, 1), new(2027, 1, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Items.Where(x => x.ShopId == seed.Shop).ToListAsync());
            Assert.Empty(await db.PurchaseReceiptValues.Where(x => x.ShopId == seed.Shop).ToListAsync());
            Assert.Empty(await db.SupplierLedgerEntries.Where(x => x.ShopId == seed.Shop).ToListAsync());
        }
        var rows = new[]
        {
            new { clientRowId = "good", itemName = $"Expiry item {barcode}", barcode, uom = "pcs", batchNumber = "good", quantity = 10, totalPurchaseCost = 10, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)new DateOnly(2030, 1, 1), manufacturingDate = (DateOnly?)null },
            new { clientRowId = "missing", itemName = $"Expiry item {barcode}", barcode, uom = "pcs", batchNumber = "missing", quantity = 10, totalPurchaseCost = 10, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)null, manufacturingDate = (DateOnly?)null },
            new { clientRowId = "invalid", itemName = $"Expiry item {barcode}", barcode, uom = "pcs", batchNumber = "invalid", quantity = 10, totalPurchaseCost = 10, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)new DateOnly(2026, 1, 1), manufacturingDate = (DateOnly?)new DateOnly(2027, 1, 1) },
        };
        using var bulk = await client.PostAsJsonAsync("/api/inventory/inbound/batch", new { operationKey = Guid.NewGuid().ToString(), items = rows });
        Assert.True(bulk.IsSuccessStatusCode, await bulk.Content.ReadAsStringAsync());
        var result = await bulk.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("successCount").GetInt32());
        Assert.Equal(2, result.GetProperty("failedCount").GetInt32());
        using var verify = _factory.Services.CreateScope();
        var saved = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await saved.InventoryBatches.Where(x => x.ShopId == seed.Shop).ToListAsync());
        Assert.Single(await saved.PurchaseReceiptValues.Where(x => x.ShopId == seed.Shop).ToListAsync());
        Assert.Single(await saved.SupplierLedgerEntries.Where(x => x.ShopId == seed.Shop).ToListAsync());
        Assert.Equal(10m, await saved.Inventory.Where(x => x.ShopId == seed.Shop).Select(x => x.Quantity).SingleAsync());
    }
    [Fact]
    public async Task DateOnlyEdit_RequiresCasAndReasonAndRetainsImmutableHistory()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, null);
        var diagnostic = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}");
        var version = diagnostic[0].GetProperty("version").GetUInt32();
        object Edit(uint? expected, string? reason, DateOnly? expiry, DateOnly? manufacturing = null) => new { quantity = 10m, costPrice = 1m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m, taxIncluded = false, expiryDate = expiry, manufacturingDate = manufacturing, expectedVersion = expected, notes = reason };
        using var noVersion = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}", Edit(null, "date correction", new(2030, 1, 1)));
        Assert.Equal(HttpStatusCode.Conflict, noVersion.StatusCode);
        using var noReason = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}", Edit(version, null, new(2030, 1, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        using var badPair = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}", Edit(version, "bad label", new(2030, 1, 1), new(2031, 1, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, badPair.StatusCode);
        using var corrected = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}", Edit(version, "Read original label", new(2030, 1, 1)));
        Assert.True(corrected.IsSuccessStatusCode, await corrected.Content.ReadAsStringAsync());
        using var stale = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}", Edit(version, "stale correction", new(2031, 1, 1)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var revision = await db.BatchExpiryRevisions.SingleAsync(x => x.ShopId == seed.Shop);
        Assert.Null(revision.PreviousExpiryDate); Assert.Equal(new DateOnly(2030, 1, 1), revision.NewExpiryDate);
        Assert.Equal(seed.Actor, revision.ActorUserId); Assert.Equal("Read original label", revision.Reason);
        Assert.Equal(version, revision.PreviousBatchVersion); Assert.Equal(2, revision.CatalogVersion);
        Assert.Empty(await db.BatchPriceRevisions.Where(x => x.ShopId == seed.Shop).ToListAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM batch_expiry_revisions WHERE id = {revision.Id}"));
        var updated = await db.InventoryBatches.SingleAsync(x => x.Id == batch);
        using var retail = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}/prices", new { expectedVersion = updated.Version, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, expiryDate = new DateOnly(2031, 1, 1), reason = "second label correction" });
        Assert.True(retail.IsSuccessStatusCode, await retail.Content.ReadAsStringAsync());
        Assert.Equal(2, await db.BatchExpiryRevisions.CountAsync(x => x.ShopId == seed.Shop));
    }
    [Fact]
    public async Task Fefo_EligibleDatesThenUndatedStableIdentityAndQuarantineConservation()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N");
        var undated = await Receive(client, barcode, null, "undated");
        var later = await Receive(client, barcode, new(2031, 1, 1), "later");
        var early = await Receive(client, barcode, new(2030, 1, 1), "early");
        var blocked = await Receive(client, barcode, new(2020, 1, 1), "blocked");
        await Policy(client, "Block");
        using (var scope = _factory.Services.CreateScope())
            await QuarantineFixture.SeedAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), early, 9m);
        var goods = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}");
        Assert.Equal(new[] { early, later, undated }, goods.EnumerateArray().Select(x => x.GetProperty("inventoryBatchId").GetGuid()));
        Assert.Equal(10m, goods[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(1m, goods[0].GetProperty("availableQuantity").GetDecimal());
        var all = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}&includeUnavailable=true");
        Assert.Equal(4, all.GetArrayLength());
        Assert.Equal(0m, all.EnumerateArray().Single(x => x.GetProperty("inventoryBatchId").GetGuid() == blocked).GetProperty("availableQuantity").GetDecimal());
        using var crafted = await client.PostAsJsonAsync("/api/sales", Sale(early, barcode, duplicate: 2));
        Assert.Equal(HttpStatusCode.BadRequest, crafted.StatusCode);
        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var physical = await db.InventoryBatches.SingleAsync(x => x.Id == early);
        Assert.Equal(10m, physical.Quantity); Assert.Equal(9m, physical.QuarantinedQuantity);
    }
    private async Task WaitForFenceWaiter(CancellationToken cancellationToken = default)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeout = DateTimeOffset.UtcNow.AddSeconds(4);
        while (DateTimeOffset.UtcNow < timeout)
        {
            var count = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%shop_expiry_states%'").SingleAsync(cancellationToken);
            if (count > 0) return;
            await Task.Delay(20, cancellationToken);
        }
        Assert.Fail("Expected a real PostgreSQL expiry-fence waiter.");
    }
    [Fact]
    public async Task SaleSharedFence_PolicyEditWaitsThenSaleRetainsOriginalDecision()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2020, 1, 1));
        _barrier.Enabled = true;
        var sale = client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        await _barrier.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var edit = client.PutAsJsonAsync("/api/expiry-policies", new { expectedCatalogVersion = 1, policy = new { mode = "Block", expiryRequired = false, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "change during sale" });
        try { await WaitForFenceWaiter(); Assert.False(edit.IsCompleted); }
        finally { _barrier.Release.TrySetResult(); }
        using var posted = await sale; Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        using var changed = await edit; Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var evidence = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiryDecisions")[0];
        Assert.Equal("Ignore", evidence.GetProperty("policy").GetProperty("mode").GetString());
        Assert.Equal(9m, (await Facts(seed.Shop, batch)).Quantity);
    }
    [Fact]
    public async Task PolicyExclusiveFence_WinningEditMakesWaitingSaleSeeBlock()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2020, 1, 1));
        var before = await Facts(seed.Shop, batch);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.False((await new ExpiryPolicyFence(db).AcquireAsync(seed.Shop, true, default)).IsError);
        var state = await db.ShopExpiryStates.SingleAsync(x => x.ShopId == seed.Shop);
        var version = state.Advance(true);
        db.ExpiryPolicyRevisions.Add(ExpiryPolicyRevision.Create(seed.Shop, null, version, new(version, Intelibill.Domain.ValueObjects.ExpiryMode.Block, false, true, 7, false), seed.Actor, "before sale", _time.Now));
        await db.SaveChangesAsync();
        _barrier.Enabled = true;
        var sale = client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        await _barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForFenceWaiter(); Assert.False(_barrier.Acquired.Task.IsCompleted);
        await tx.CommitAsync();
        _barrier.Release.TrySetResult();
        using var refused = await sale;
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Expiry.Expired", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(before, await Facts(seed.Shop, batch));
    }
    [Fact]
    public async Task PurchaseOrderReceipt_AnyInvalidDateRollsBackWholeGroup()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var original = await Receive(client, barcode, new(2030, 1, 1));
        await Policy(client, "Block", required: true);
        Guid orderId; Guid lineId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var batch = await db.InventoryBatches.SingleAsync(x => x.Id == original);
            var supplierId = await db.Suppliers.Where(x => x.ShopId == seed.Shop && x.IsSystem).Select(x => x.Id).SingleAsync();
            var order = PurchaseOrder.CreateDraft(seed.Shop, $"PO-{Guid.NewGuid():N}"[..24], supplierId, null, null, null, null);
            var line = order.AddLine(batch.ItemId, "Expiry product", 2, 1);
            order.Place(supplierId); db.PurchaseOrders.Add(order); await db.SaveChangesAsync(); orderId = order.Id; lineId = line.Id;
        }
        foreach (var dates in new[] { (Expiry: (DateOnly?)null, Manufacturing: (DateOnly?)null), (Expiry: (DateOnly?)new DateOnly(2030, 1, 1), Manufacturing: (DateOnly?)new DateOnly(2031, 1, 1)) })
        {
            using var refused = await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/receipts", new
            {
                operationId = Guid.NewGuid(),
                notes = "two lots",
                lines = new[]
                {
                    new { purchaseOrderLineId = lineId, barcode, batchNumber = "po-good", quantity = 1, totalPurchaseCost = 1, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = (DateOnly?)new DateOnly(2030, 1, 1), manufacturingDate = (DateOnly?)null, clientLineId = Guid.NewGuid() },
                    new { purchaseOrderLineId = lineId, barcode, batchNumber = "po-bad", quantity = 1, totalPurchaseCost = 1, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, purchaseTaxIncluded = false, expiryDate = dates.Expiry, manufacturingDate = dates.Manufacturing, clientLineId = Guid.NewGuid() },
                },
            });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(dates.Expiry is null ? "Expiry.RequiredDateMissing" : "Expiry.InvalidDateOrder", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.PurchaseOrderReceipts.Where(x => x.ShopId == seed.Shop).ToListAsync());
            Assert.Single(await db.InventoryBatches.Where(x => x.ShopId == seed.Shop).ToListAsync());
            Assert.Single(await db.PurchaseReceiptValues.Where(x => x.ShopId == seed.Shop).ToListAsync());
            Assert.Single(await db.SupplierLedgerEntries.Where(x => x.ShopId == seed.Shop).ToListAsync());
            Assert.Equal(0, await db.PurchaseOrderLines.Where(x => x.Id == lineId).Select(x => x.ReceivedQuantity).SingleAsync());
        }
    }
    [Fact]
    public async Task HardBlock_WithActiveTillLeavesSessionVersionAndMoneyUnchanged()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2020, 1, 1));
        await Policy(client, "Block");
        var options = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Intelibill.Infrastructure.Options.TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [seed.Shop];
        using var tillResponse = await client.PostAsJsonAsync("/api/tills", new { name = "Expiry test till", operationKey = Guid.NewGuid().ToString() });
        Assert.True(tillResponse.IsSuccessStatusCode, await tillResponse.Content.ReadAsStringAsync());
        var tillId = (await tillResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var opened = await client.PostAsJsonAsync($"/api/tills/{tillId}/sessions", new { cashierId = seed.Actor, openingFloat = 100, operationKey = Guid.NewGuid().ToString() });
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        var session = await opened.Content.ReadFromJsonAsync<JsonElement>(); var sessionId = session.GetProperty("id").GetGuid();
        var payload = JsonSerializer.SerializeToNode(Sale(batch, barcode))!;
        payload["till"] = JsonSerializer.SerializeToNode(new { sessionId });
        payload["tenderLines"] = JsonSerializer.SerializeToNode(new[] { new { method = "Cash", appliedAmount = 2, cashTendered = 2, change = 0 } });
        var before = await Facts(seed.Shop, batch);
        using var refused = await client.PostAsJsonAsync("/api/sales", payload);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Expiry.Expired", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await Facts(seed.Shop, batch));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.TillSessions.SingleAsync(x => x.Id == sessionId);
        Assert.Equal(session.GetProperty("version").GetUInt32(), persisted.Version); Assert.Equal(0, persisted.SourceCount);
        Assert.Empty(await db.TillMovements.Where(x => x.ShopId == seed.Shop).ToListAsync());
    }
    [Fact]
    public async Task SaleSharedFence_DateEditWaitsAndThenRequiresFreshBatchVersion()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2030, 1, 1));
        var initial = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}");
        var version = initial[0].GetProperty("version").GetUInt32();
        object Edit(uint v) => new { expectedVersion = v, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, expiryDate = new DateOnly(2020, 1, 1), reason = "correct date after sale" };
        _barrier.Enabled = true;
        var sale = client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        await _barrier.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var edit = client.PutAsJsonAsync($"/api/inventory/batches/{batch}/prices", Edit(version));
        try { await WaitForFenceWaiter(); Assert.False(edit.IsCompleted); }
        finally { _barrier.Release.TrySetResult(); }
        using var posted = await sale; Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        using var stale = await edit; Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var evidence = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiryDecisions")[0];
        Assert.Equal("2030-01-01", evidence.GetProperty("expiryDate").GetString());
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}");
        using var retried = await client.PutAsJsonAsync($"/api/inventory/batches/{batch}/prices", Edit(current[0].GetProperty("version").GetUInt32()));
        Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.BatchExpiryRevisions.Where(x => x.ShopId == seed.Shop).ToListAsync());
    }
    [Fact]
    public async Task InformationalNearExpiry_WarnsWithoutRequiringApproval()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2026, 10, 22));
        await Policy(client, "Block");
        var projected = await client.GetFromJsonAsync<JsonElement>($"/api/sales/sellables?barcode={barcode}");
        Assert.True(projected[0].GetProperty("sellability").GetProperty("warning").GetBoolean());
        Assert.False(projected[0].GetProperty("sellability").GetProperty("requiresApproval").GetBoolean());
        Assert.Equal(10m, projected[0].GetProperty("availableQuantity").GetDecimal());
        using var posted = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var evidence = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiryDecisions")[0];
        Assert.Equal("Expiry.ExpiringSoon", evidence.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("approvedBy").ValueKind);
    }
    [Fact]
    public async Task ItemOverride_BlocksInheritedIgnoreThenResetRestoresCompatibility()
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2020, 1, 1));
        Guid itemId;
        using (var scope = _factory.Services.CreateScope()) itemId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InventoryBatches.Where(x => x.Id == batch).Select(x => x.ItemId).SingleAsync();
        using var changed = await client.PutAsJsonAsync($"/api/expiry-policies/items/{itemId}", new { expectedCatalogVersion = 1, policy = new { mode = "Block", expiryRequired = false, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "item requires blocking" });
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/sales/sellables?barcode={barcode}")).EnumerateArray());
        using var blocked = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var reset = await client.PutAsJsonAsync($"/api/expiry-policies/items/{itemId}", new { expectedCatalogVersion = 2, policy = (object?)null, reason = "inherit again" });
        Assert.True(reset.IsSuccessStatusCode, await reset.Content.ReadAsStringAsync());
        using var posted = await client.PostAsJsonAsync("/api/sales", Sale(batch, barcode));
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var evidence = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiryDecisions")[0];
        Assert.Equal(3, evidence.GetProperty("catalogVersion").GetInt64()); Assert.Equal(1, evidence.GetProperty("policy").GetProperty("version").GetInt64());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryReadSnapshot_DateCorrectionCannotMixOldBatchWithNewCatalog(bool administrative)
    {
        var seed = await Shop(); using var client = seed.Client;
        var barcode = Guid.NewGuid().ToString("N"); var batch = await Receive(client, barcode, new(2030, 1, 1));
        await Policy(client, "Block");
        var initial = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/batches/available?barcode={barcode}");
        var version = initial[0].GetProperty("version").GetUInt32();
        _projectionRead.Enabled = true;
        var read = administrative ? client.GetAsync("/api/inventory/batches") : client.PostAsJsonAsync("/api/sales/preview", new { saleDiscount = new { type = "None", value = 0 }, items = new[] { Line(batch, barcode) } });
        await _projectionRead.Read.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var edit = client.PutAsJsonAsync($"/api/inventory/batches/{batch}/prices", new { expectedVersion = version, mrp = 2, salesPrice = 2, taxRatePercent = 0, taxIncluded = false, expiryDate = new DateOnly(2020, 1, 1), reason = "correct during projection" });
        using var waitCancellation = new CancellationTokenSource();
        var waiting = WaitForFenceWaiter(waitCancellation.Token);
        try
        {
            // Before the fix, the edit commits while the SELECT is paused. Afterwards it waits
            // behind the read's shared fence. Both schedules must return a complete old snapshot.
            await Task.WhenAny(edit, waiting).WaitAsync(TimeSpan.FromSeconds(5));
            if (waiting.IsCompleted) await waiting;
            else { using var prematurelyCommitted = await edit; Assert.True(prematurelyCommitted.IsSuccessStatusCode, await prematurelyCommitted.Content.ReadAsStringAsync()); }
        }
        finally
        {
            waitCancellation.Cancel();
            _projectionRead.Release.TrySetResult();
            try { await waiting; } catch (OperationCanceledException) { }
        }
        using var projected = await read;
        Assert.True(projected.IsSuccessStatusCode, await projected.Content.ReadAsStringAsync());
        var body = await projected.Content.ReadFromJsonAsync<JsonElement>();
        var decision = administrative ? body[0].GetProperty("sellability") : body.GetProperty("expiryDecisions")[0].GetProperty("decision");
        Assert.Equal(2, decision.GetProperty("catalogVersion").GetInt64());
        Assert.True(decision.GetProperty("allowed").GetBoolean());
        Assert.Equal(version, (administrative ? body[0].GetProperty("version") : body.GetProperty("expiryDecisions")[0].GetProperty("batchVersion")).GetUInt32());
        using var changed = await edit; Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        using var reread = administrative ? await client.GetAsync("/api/inventory/batches") : await client.PostAsJsonAsync("/api/sales/preview", new { saleDiscount = new { type = "None", value = 0 }, items = new[] { Line(batch, barcode) } });
        var current = await reread.Content.ReadFromJsonAsync<JsonElement>();
        var currentDecision = administrative ? current[0].GetProperty("sellability") : current.GetProperty("expiryDecisions")[0].GetProperty("decision");
        Assert.Equal(3, currentDecision.GetProperty("catalogVersion").GetInt64());
        Assert.False(currentDecision.GetProperty("allowed").GetBoolean());
        if (administrative) Assert.Equal("2020-01-01", current[0].GetProperty("expiryDate").GetString());
    }
}
