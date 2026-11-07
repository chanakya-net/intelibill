using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Intelibill.Application.Common.Time;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Intelibill.Infrastructure.Options;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleCancellationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory factory = new(fixture, services =>
    {
        services.RemoveAll<IBusinessClock>();
        services.AddSingleton<SettableClock>();
        services.AddSingleton<IBusinessClock>(sp => new Intelibill.Infrastructure.Services.Time.BusinessClock(sp.GetRequiredService<SettableClock>()));
    });
    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public Task InitializeAsync() => factory.InitializeAsync();
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { factory.Dispose(); GC.SuppressFinalize(this); }

    private sealed record Seed(HttpClient Client, Guid ShopId, Guid ActorId, Sale Sale, Guid BatchId, Guid CustomerId);
    private async Task<Seed> SeedAsync(decimal paid = 200m, PaymentMethod method = PaymentMethod.Cash, bool split = false, decimal cost = 60m, decimal quantity = 2m)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        var registration = await client.EnrollAsync(new { email = $"cancel-{Guid.NewGuid():N}@test.com", password = "Pass123!Aa", firstName = "Cancel", lastName = "Owner", phoneNumber = $"+91{Random.Shared.NextInt64(1000000000, 9999999999)}" });
        registration.EnsureSuccessStatusCode();
        var token = (await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var create = await client.PostAsJsonAsync("/api/shops", new { name = "Cancellation shop", address = "Address", city = "City", state = "Karnataka", pincode = "560001" });
        create.EnsureSuccessStatusCode();
        token = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var actor = Guid.Parse(jwt.Subject);
        var shop = Guid.Parse(jwt.Claims.Single(x => x.Type == "active_shop_id").Value);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customer = Customer.Create(shop, "Buyer", Guid.NewGuid().ToString("N")[..10], null);
        var item = Item.Create(shop, "Sugar", null, "kg", Guid.NewGuid().ToString("N"), true, actor);
        var batch = InventoryBatch.Create(shop, item.Id, "LOT", 8m, cost, 100m, 100m, 0m, false, null, null, null, actor).Value;
        var stock = Inventory.Create(shop, item.Id, 8m, 0m, 100m, actor).Value;
        var now = factory.Services.GetRequiredService<IBusinessClock>().UtcNow;
        var total = quantity * 100m;
        SaleLineInput Line(decimal q) => new(shop, SaleLineType.Goods, item.Id, batch.Id, null, "Frozen sugar", "SUGAR", q, cost, 100m, 100m, 0m, false, false);
        var sale = Sale.Record(shop, actor, Guid.NewGuid().ToString("N"), "HASH", "A/2627/000001", split ? [Line(1m), Line(1m)] : [Line(quantity)], customer.Id, "Buyer", null, method, paid, total - paid, now, BusinessDay.InDefaultZone(now)).Value;
        sale.AttachInvoiceDocument(new(2, InvoiceDocumentType.Receipt, now, sale.BusinessDate, 2026, "A", sale.InvoiceNumber,
            new("Frozen seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered), new("Frozen buyer", null, null, null, null, null), "29", "Karnataka", false, false,
            sale.Items.Select((x, i) => new InvoiceDocumentLine(i + 1, "Frozen sugar", null, "KGS", x.Quantity, 100m, 0m, x.TotalAmount, 0m, true, [], x.TotalAmount)).ToArray(),
            new(0m, total, [], 0m, 0m, total), [InvoiceDeclaration.NotRegisteredUnderGst], InvoiceSignatureMode.AuthorisedSignatory, "component-split/1"));
        db.AddRange(customer, item, batch, stock, sale);
        if (paid < total) db.Add(CustomerLedgerEntry.Create(shop, customer.Id, sale.Id, CustomerLedgerEntryType.SaleDue, total - paid, sale.BusinessDate, null, actor).Value);
        await db.SaveChangesAsync();
        var rollout = factory.Services.GetRequiredService<IOptionsMonitor<SaleCancellationOptions>>().CurrentValue;
        rollout.Enabled = true; rollout.AllowedShopIds = [shop];
        return new(client, shop, actor, sale, batch.Id, customer.Id);
    }
    private static object Request(Seed s, string key = "cancel-key", uint? version = null, bool goods = true, bool? cash = null, string reason = "Mistaken entry") => new { operationKey = key, expectedVersion = version ?? s.Sale.Version, reason, goodsRecoveredConfirmed = goods, cashRefundConfirmed = cash ?? s.Sale.PaidAmount > 0m };

    [Theory]
    [InlineData(200, false)]
    [InlineData(0, false)]
    [InlineData(80, false)]
    [InlineData(200, true)]
    public async Task ExactStockAndSettlement_RetainsIssuedReceipt(decimal paid, bool split)
    {
        var s = await SeedAsync(paid, split: split);
        var preview = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var p = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(p.GetProperty("eligible").GetBoolean());
        Assert.Equal(200m - paid, p.GetProperty("dueReductionAmount").GetDecimal());
        Assert.Equal(paid, p.GetProperty("cashRefundAmount").GetDecimal());
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(10m, (await db.InventoryBatches.SingleAsync(x => x.Id == s.BatchId)).Quantity);
        Assert.Equal(10m, (await db.Inventory.SingleAsync(x => x.ShopId == s.ShopId)).Quantity);
        var persisted = await db.Sales.SingleAsync(x => x.Id == s.Sale.Id);
        Assert.Equal(SaleStatus.Cancelled, persisted.Status);
        Assert.Equal(200m, persisted.TotalAmount);
        Assert.Equal("Frozen seller", persisted.InvoiceDocument!.Seller.LegalName);
        Assert.Equal(200m, (await db.Set<SaleCancellation>().SingleAsync()).TotalAmount);
        Assert.Equal(0m,await scope.ServiceProvider.GetRequiredService<ICustomerLedgerEntryRepository>().GetCustomerBalanceAsync(s.ShopId,s.CustomerId));
        Assert.Equal(2m, await db.StockTransactions.Where(x => x.ShopId == s.ShopId && x.TransactionType == StockTransactionType.SaleCancellation).SumAsync(x => x.Quantity));
    }

    [Fact]
    public async Task RefundConfirmationCannotTurnCardIntoCash()
    {
        var s = await SeedAsync(method: PaymentMethod.Card);
        var p = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, p.StatusCode);
        var preview = await p.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200m, preview.GetProperty("externalRefundAmount").GetDecimal());
        Assert.False(preview.GetProperty("eligible").GetBoolean());
        var result = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Set<SaleCancellation>().ToListAsync());
        Assert.Empty(await db.OperationReceipts.ToListAsync());
        Assert.Equal(8m, (await db.InventoryBatches.SingleAsync()).Quantity);
    }
    private async Task AssertUntouchedAsync(Seed s, decimal expectedQuantity = 8m)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Set<SaleCancellation>().Where(x => x.ShopId == s.ShopId).ToListAsync());
        Assert.Empty(await db.OperationReceipts.Where(x => x.ShopId == s.ShopId).ToListAsync());
        Assert.Empty(await db.StockTransactions.Where(x => x.ShopId == s.ShopId).ToListAsync());
        Assert.Empty(await db.CustomerLedgerEntries.Where(x => x.ShopId == s.ShopId && x.EntryType == CustomerLedgerEntryType.SaleCancellationCredit).ToListAsync());
        Assert.Equal(8m, (await db.Inventory.SingleAsync(x => x.ShopId == s.ShopId)).Quantity);
        Assert.Equal(expectedQuantity, (await db.InventoryBatches.SingleAsync(x => x.Id == s.BatchId)).Quantity);
        Assert.Equal(SaleStatus.Posted, (await db.Sales.SingleAsync(x => x.Id == s.Sale.Id)).Status);
    }

    [Theory]
    [InlineData("goods", 400)]
    [InlineData("cash", 400)]
    [InlineData("reason", 400)]
    [InlineData("key", 400)]
    [InlineData("zero-version", 400)]
    [InlineData("stale-version", 409)]
    [InlineData("unexpected-cash", 400)]
    public async Task InvalidRequestAndStaleVersion_DoNotPost(string change, int status)
    {
        var s = await SeedAsync(change == "unexpected-cash" ? 0m : 200m);
        var result = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s,
            key: change == "key" ? " " : "valid-key", version: change == "zero-version" ? 0u : change == "stale-version" ? uint.MaxValue : null,
            goods: change != "goods", cash: change == "cash" ? false : change == "unexpected-cash" ? true : null, reason: change == "reason" ? " x " : "Mistaken entry"));
        Assert.Equal(status, (int)result.StatusCode);
        await AssertUntouchedAsync(s);
    }

    [Fact]
    public async Task Preview_IsReadOnly_AndDisabledPostingNeedsBothGates()
    {
        var s = await SeedAsync();
        var options = factory.Services.GetRequiredService<IOptionsMonitor<SaleCancellationOptions>>().CurrentValue;
        options.Enabled = false;
        var preview = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var p = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(p.GetProperty("eligible").GetBoolean());
        Assert.False(p.GetProperty("postingEnabled").GetBoolean());
        var result = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        options.Enabled = true; options.AllowedShopIds = [];
        result = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        await AssertUntouchedAsync(s);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PostingPeriods.ToListAsync());
    }

    [Fact]
    public async Task SameKeyReplays_AfterDisableAndClosure_ChangedPayloadConflicts_DifferentKeyAlreadyCancelled()
    {
        var s = await SeedAsync(80m);
        var first = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, reason: "  Mistaken entry  "));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        factory.Services.GetRequiredService<IOptionsMonitor<SaleCancellationOptions>>().CurrentValue.Enabled = false;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var period = await db.PostingPeriods.SingleAsync();
            Assert.False(period.Close(s.ActorId, DateTimeOffset.UtcNow, period.EndDate.AddDays(1), "Closed after cancellation").IsError);
            await db.SaveChangesAsync();
        }
        var replay = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body.GetProperty("id").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var recovery = await s.Client.GetAsync("/api/financial-operations/sale-cancellation/cancel-key");
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var mismatch = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, reason: "Changed reason"));
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Contains("Operation.PayloadMismatch", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        factory.Services.GetRequiredService<IOptionsMonitor<SaleCancellationOptions>>().CurrentValue.Enabled = true;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var period = await db.PostingPeriods.SingleAsync();
            period.Reopen(s.ActorId, DateTimeOffset.UtcNow, "Reopen for test"); await db.SaveChangesAsync();
        }
        var another = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, key: "other-key"));
        Assert.Equal(HttpStatusCode.Conflict, another.StatusCode);
        Assert.Contains("SaleCancellation.AlreadyCancelled", await another.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var finalScope = factory.Services.CreateAsyncScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await finalDb.Set<SaleCancellation>().ToListAsync());
        Assert.Equal(10m, (await finalDb.InventoryBatches.SingleAsync()).Quantity);
        Assert.Equal(120m, (await finalDb.CustomerLedgerEntries.SingleAsync(x => x.EntryType == CustomerLedgerEntryType.SaleCancellationCredit)).Amount);
    }

    [Theory]
    [InlineData("legacy", "LegacySnapshotBlocked")]
    [InlineData("tax", "TaxInvoiceBlocked")]
    [InlineData("offline", "OfflineOriginBlocked")]
    [InlineData("old-month", "OtherMonthBlocked")]
    [InlineData("closed", "ClosedMonthBlocked")]
    [InlineData("credit", "CreditRestorationRequired")]
    [InlineData("mismatch", "SnapshotMismatch")]
    [InlineData("voided-batch", "StockLinksInvalid")]
    public async Task PolicyRefusals_HaveReasonsAndNoEffects(string variant, string reason)
    {
        var s = await SeedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (variant == "legacy") await db.Database.ExecuteSqlInterpolatedAsync($"update sales set invoice_document=null where id={s.Sale.Id}");
            if (variant == "tax") await db.Database.ExecuteSqlInterpolatedAsync($"update sales set invoice_document=jsonb_set(invoice_document,'{{documentType}}','\"TaxInvoice\"') where id={s.Sale.Id}");
            if (variant == "offline") await db.Database.ExecuteSqlInterpolatedAsync($"update sales set source=2 where id={s.Sale.Id}");
            if (variant == "old-month") await db.Database.ExecuteSqlInterpolatedAsync($"update sales set business_date=business_date - interval '1 month' where id={s.Sale.Id}");
            if (variant == "credit") await db.Database.ExecuteSqlInterpolatedAsync($"update sales set credit_note_applied_amount=10 where id={s.Sale.Id}");
            if (variant == "mismatch") await db.Database.ExecuteSqlInterpolatedAsync($"update sales set total_amount=201 where id={s.Sale.Id}");
            if (variant == "voided-batch") { var b = await db.InventoryBatches.SingleAsync(); b.Void(s.ActorId); await db.SaveChangesAsync(); }
            if (variant == "closed") { var p = PostingPeriod.Open(s.ShopId, s.Sale.BusinessDate); p.Close(s.ActorId, DateTimeOffset.UtcNow, p.EndDate.AddDays(1), "Closed test"); db.Add(p); await db.SaveChangesAsync(); }
        }
        // Read current token, so the policy rather than stale-version check decides the result.
        uint version;
        await using (var scope = factory.Services.CreateAsyncScope()) version = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Sales.SingleAsync()).Version;
        var preview = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains(reason, await preview.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var result = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, version: version));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        await AssertUntouchedAsync(s, variant == "voided-batch" ? 0m : 8m);
    }

    [Theory]
    [InlineData("unallocated")]
    [InlineData("allocated")]
    [InlineData("account-only")]
    public async Task CollectionsRequireReview_ForUnallocatedAndAllocatedReceipts(string collection)
    {
        var s = await SeedAsync(0m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = CustomerLedgerEntry.Create(s.ShopId, s.CustomerId, null, CustomerLedgerEntryType.PaymentReceived, 20m, s.Sale.BusinessDate, null, s.ActorId).Value;
            var receipt = PaymentReceipt.Record(s.ShopId, PaymentPartyType.Customer, s.CustomerId, 20m, ReceiptTenderMethod.Cash, null, null, s.Sale.BusinessDate, DateTimeOffset.UtcNow, null, payment.Id, null, s.ActorId).Value;
            db.Add(payment);
            if (collection != "account-only") db.Add(receipt);
            if (collection == "allocated") db.Add(PaymentAllocation.Create(receipt, s.Sale.Id, 20m, s.ActorId).Value);
            await db.SaveChangesAsync();
        }
        var preview = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Contains("CollectionsRequireReview", await preview.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUntouchedAsync(s);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnHistory_IncludingVoided_BlocksCancellation(bool voided)
    {
        var s = await SeedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var line = s.Sale.Items[0]; var now = DateTimeOffset.UtcNow;
            var returned = SaleReturn.Record(s.ShopId, s.Sale.Id, "RET-TEST", now, BusinessDay.InDefaultZone(now), s.ActorId, null, 100m, 0m, 100m, ReturnPayoutDestination.Refund, 100m, 0m, null, null,
                [new(s.ShopId, line.Id, 1m, SaleReturnCondition.Restockable, 60m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)]).Value;
            if (voided) returned.Void(now, s.Sale.BusinessDate, s.ActorId, "Voided return");
            db.Add(returned); await db.SaveChangesAsync();
        }
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ReturnHistoryBlocked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertUntouchedAsync(s);
    }

    [Fact]
    public async Task StaffAndForeignTenant_CannotReplayOrRecover()
    {
        var s = await SeedAsync();
        var committed = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.ShopMemberships.SingleAsync(x => x.ShopId == s.ShopId)).SetRole(ShopRole.Staff); await db.SaveChangesAsync();
            var email = (await db.Users.SingleAsync(x => x.Id == s.ActorId)).Email;
            s.Client.DefaultRequestHeaders.Authorization = null;
            var login = await s.Client.PostAsJsonAsync("/api/auth/login/email", new { email, password = "Pass123!Aa" });
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            s.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/financial-operations/sale-cancellation/cancel-key")).StatusCode);
        var foreign = await SeedAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.GetAsync("/api/financial-operations/sale-cancellation/cancel-key")).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailure_RollsBackAllEffectsAndReceipt()
    {
        var s = await SeedAsync(80m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION cancellation_test_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test serialization failure' USING ERRCODE='40001'; END $$; CREATE TRIGGER cancellation_test_fail BEFORE INSERT ON sale_cancellations FOR EACH ROW EXECUTE FUNCTION cancellation_test_fail();");
        }
        try
        {
            var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertUntouchedAsync(s);
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.CustomerLedgerEntries.Where(x => x.EntryType == CustomerLedgerEntryType.SaleCancellationCredit).ToListAsync());
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER cancellation_test_fail ON sale_cancellations; DROP FUNCTION cancellation_test_fail();");
        }
        // The same exact operation remains safe to retry after the rolled-back failure.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentDifferentKeys_PostExactlyOnce()
    {
        var s = await SeedAsync(80m);
        var responses = await Task.WhenAll(s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, key: "one")), s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, key: "two")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.Set<SaleCancellation>().ToListAsync());
        Assert.Single(await db.OperationReceipts.ToListAsync());
        Assert.Equal(10m, (await db.InventoryBatches.SingleAsync()).Quantity);
    }

    [Theory]
    [InlineData("2026-11-30T18:29:59Z", true)]
    [InlineData("2026-11-30T18:30:00Z", false)]
    public async Task IstMidnightMonthBoundary_UsesCapturedShopDay(string instant, bool eligible)
    {
        factory.Services.GetRequiredService<SettableClock>().Now = DateTimeOffset.Parse("2026-11-08T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var s = await SeedAsync();
        factory.Services.GetRequiredService<SettableClock>().Now = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        var p = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, p.StatusCode);
        Assert.Equal(eligible, (await p.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eligible").GetBoolean());
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(eligible ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
        if (!eligible) await AssertUntouchedAsync(s);
        else
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("2026-11-30", body.GetProperty("businessDate").GetString());
            Assert.Equal(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture), body.GetProperty("cancelledAt").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task FractionalRecordedCost_FreezesRawUnitCostAndRoundedPerLineCost()
    {
        var s = await SeedAsync(paid: 100.50m, cost: 0.33m, quantity: 1.005m);
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var line = await db.Set<SaleCancellationLine>().SingleAsync();
        Assert.Equal(0.33m, line.OriginalCostPrice);
        Assert.Equal(0.33165m, line.Quantity * line.OriginalCostPrice);
        Assert.Equal(0.33m, line.RecordedCostAmount);
        Assert.Equal(100.50m, line.RevenueAmount);
    }

    [Fact]
    public async Task BatchLockTimeout_IsRetryableAndRollsBackAllEffects()
    {
        var s = await SeedAsync(80m);
        await using var connection = new NpgsqlConnection(factory.MigratorConnectionString);
        await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("select id from inventory_batches where id=@id for update", connection, transaction))
        {
            command.Parameters.AddWithValue("id", s.BatchId); await command.ExecuteScalarAsync();
        }
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("SaleCancellation.Busy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await transaction.RollbackAsync();
        await AssertUntouchedAsync(s);
    }

    [Fact]
    public async Task SubMicrosecondJsonIssuedAt_MatchesPersistedSaleInstant()
    {
        factory.Services.GetRequiredService<SettableClock>().Now = DateTimeOffset.Parse("2026-11-08T10:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture);
        var s = await SeedAsync();
        var response = await s.Client.PostAsync($"/api/sales/{s.Sale.Id}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eligible").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s))).StatusCode);
    }

    [Fact]
    public async Task PostedOnlineSale_WithSevenDigitTimestamp_PreviewsAndCancels()
    {
        factory.Services.GetRequiredService<SettableClock>().Now = DateTimeOffset.Parse("2026-11-08T10:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture);
        var s = await SeedAsync();
        var declared = await s.Client.PutAsJsonAsync($"/api/shops/{s.ShopId}/tax-profile", new { taxRegime = "Unregistered", stateCode = "29", legalName = "Retailer", invoiceSeriesCode = "B", eInvoiceStatus = "BelowThreshold" });
        Assert.Equal(HttpStatusCode.OK, declared.StatusCode);
        string barcode;
        await using (var scope = factory.Services.CreateAsyncScope()) barcode = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Items.SingleAsync()).Barcode;
        var issueRequest = new
        {
            customerId = s.CustomerId,
            idempotencyKey = "real-posted-sale",
            paymentMethod = "Cash",
            paidAmount = 200m,
            dueAmount = 0m,
            items = new[] { new { barcode, batchNumber = "LOT", itemName = "Sugar", quantity = 2m, costPrice = 60m, salesPrice = 100m, mrp = 100m, taxRatePercent = 0m, isPriceIncludingTax = false, inventoryBatchId = s.BatchId } }
        };
        var posted = await s.Client.PostAsJsonAsync("/api/sales", issueRequest);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var original = await posted.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = original.GetProperty("saleId").GetGuid();
        var preview = await s.Client.PostAsync($"/api/sales/{saleId}/cancellation-preview", null);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var p = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(p.GetProperty("eligible").GetBoolean());
        var cancelled = await s.Client.PostAsJsonAsync($"/api/sales/{saleId}/cancellations", new { operationKey = "cancel-real-sale", expectedVersion = p.GetProperty("expectedVersion").GetUInt32(), reason = "Mistaken real sale", goodsRecoveredConfirmed = true, cashRefundConfirmed = true });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var outcome = await cancelled.Content.ReadFromJsonAsync<JsonElement>();
        var replay = await s.Client.PostAsJsonAsync("/api/sales", issueRequest);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saleId, replayed.GetProperty("saleId").GetGuid());
        Assert.Equal("Cancelled", replayed.GetProperty("status").GetString());
        Assert.Equal(outcome.GetProperty("id").GetGuid(), replayed.GetProperty("cancellation").GetProperty("id").GetGuid());
        Assert.Equal(0m, replayed.GetProperty("items")[0].GetProperty("returnableQuantity").GetDecimal());
        Assert.Equal(original.GetProperty("invoiceNumber").GetString(), outcome.GetProperty("originalInvoiceNumber").GetString());
        await using var finalScope = factory.Services.CreateAsyncScope(); var db = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(8m, (await db.InventoryBatches.SingleAsync()).Quantity);
        Assert.Equal(200m, (await db.Sales.SingleAsync(x => x.Id == saleId)).TotalAmount);
    }

    [Fact]
    public async Task CancelledSale_ReadersKeepIssueFactsAndExposeTerminalBalances()
    {
        factory.Services.GetRequiredService<SettableClock>().Now = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var s = await SeedAsync(0m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var buyer = await db.Customers.SingleAsync();
            buyer.Update(buyer.Name, buyer.PhoneNumber, buyer.Address, true, 1000m);
            Assert.False(buyer.SetCreditPolicy(CreditPolicyMode.Enforced, 1).IsError);
            var issued = await db.Sales.SingleAsync();
            issued.ApplyCreditTerms(1);
            await db.SaveChangesAsync();
            s = s with { Sale = issued };
        }
        factory.Services.GetRequiredService<SettableClock>().Now = factory.Services.GetRequiredService<SettableClock>().Now.AddDays(2);
        var statementPath = $"/api/customers/{s.CustomerId}/account";
        var before = await s.Client.GetFromJsonAsync<JsonElement>(statementPath);
        Assert.Equal(200m, before.GetProperty("overdueAmount").GetDecimal());
        Assert.Equal(800m, before.GetProperty("availableCredit").GetDecimal());
        var posted = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s));
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        var cancellation = await posted.Content.ReadFromJsonAsync<JsonElement>();
        var detail = await s.Client.GetFromJsonAsync<JsonElement>($"/api/sales/{s.Sale.Id}");
        Assert.Equal("Cancelled", detail.GetProperty("status").GetString());
        Assert.True(detail.GetProperty("version").GetUInt32() > 0);
        Assert.Equal(cancellation.GetProperty("id").GetGuid(), detail.GetProperty("cancellation").GetProperty("id").GetGuid());
        Assert.Equal(200m, detail.GetProperty("dueAmount").GetDecimal());
        Assert.Equal(0m, detail.GetProperty("items")[0].GetProperty("returnableQuantity").GetDecimal());
        var account = await s.Client.GetFromJsonAsync<JsonElement>(statementPath);
        Assert.Equal(0m, account.GetProperty("outstandingDue").GetDecimal());
        Assert.Equal(0m, account.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(0m, account.GetProperty("overdueAmount").GetDecimal());
        Assert.Equal(1000m, account.GetProperty("availableCredit").GetDecimal());
        var cancellationCredit = Assert.Single(account.GetProperty("ledgerEntries").EnumerateArray(), e => e.GetProperty("entryType").GetString() == "SaleCancellationCredit");
        Assert.Equal(-200m, cancellationCredit.GetProperty("signedAmount").GetDecimal());
        var sale = account.GetProperty("sales")[0];
        Assert.Equal(200m, sale.GetProperty("dueAmount").GetDecimal());
        Assert.Equal(200m, sale.GetProperty("cancellationCreditAmount").GetDecimal());
        Assert.Equal(0m, sale.GetProperty("returnCreditAmount").GetDecimal());
        Assert.Equal(0m, sale.GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.False(sale.GetProperty("isOverdue").GetBoolean());
        var balances = await s.Client.GetFromJsonAsync<JsonElement>($"/api/payments/balances?partyType=Customer&partyId={s.CustomerId}");
        Assert.Equal(200m, balances.GetProperty("invoices")[0].GetProperty("cancellationCreditAmount").GetDecimal());
        Assert.Equal(0m, balances.GetProperty("invoices")[0].GetProperty("outstandingAmount").GetDecimal());
        foreach (var status in new[] { "cancelled", "paid", "partiallyPaid", "refunded", "unknown" })
        {
            var history = await s.Client.GetFromJsonAsync<JsonElement>($"/api/sales?status={status}");
            Assert.Equal(status == "cancelled" ? 1 : 0, history.GetProperty("totalCount").GetInt32());
            if (status == "cancelled")
            {
                var row = history.GetProperty("items")[0];
                Assert.Equal("cancelled", row.GetProperty("status").GetString());
                Assert.Equal(200m, row.GetProperty("totalAmount").GetDecimal());
                Assert.Equal(cancellation.GetProperty("reference").GetString(), row.GetProperty("cancellationReference").GetString());
            }
        }
        await using var latestScope = factory.Services.CreateAsyncScope();
        var repository = latestScope.ServiceProvider.GetRequiredService<ISaleRepository>();
        var latest = Assert.Single(await repository.GetLatestDashboardSalesAsync(s.ShopId));
        Assert.Equal(SaleStatus.Cancelled, latest.Status);
        Assert.Equal(cancellation.GetProperty("reference").GetString(), latest.CancellationReference);
        Assert.NotNull(latest.CancelledAt);
        Assert.Single(await repository.GetByShopAndDateRangeAsync(s.ShopId, s.Sale.BusinessDate, s.Sale.BusinessDate));
    }

    [Fact]
    public async Task ExistingCancelledOfflineDuplicate_RetainsIdentityWithoutReposting()
    {
        var s = await SeedAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s))).StatusCode);
        const string device = "defensive-device";
        var original = s.Sale;
        var payload = new OfflineSaleSyncCommand("defensive-client", original.InvoiceNumber, original.SoldAt, original.CustomerId, original.CustomerName, original.CustomerPhone,
            PaymentMethod.Cash, 200m, 0m, 200m, 200m, 0m, 0m, 200m, InstantDiscountType.None, 0m, null, null, null, null,
            [new("SUGAR", "LOT", "Frozen sugar", 2m, 60m, 100m, 100m, 0m, false, s.BatchId, 200m, 0m, 0m, 200m, 0m, 200m, null, null, InstantDiscountType.None, 0m, null)]);
        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(s.ShopId, device, payload);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Cancellation of offline origin is forbidden; this defensive historical fixture proves
            // dedup never turns a terminal record back into a fresh posting.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET source=2, client_sale_id={payload.ClientSaleId}, device_id={device}, request_hash={hash} WHERE id={original.Id}");
        }
        var response = await s.Client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = device, sales = new[] { payload } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0];
        Assert.Equal("duplicate", result.GetProperty("status").GetString());
        Assert.Equal(original.Id, result.GetProperty("saleId").GetGuid());
        Assert.Equal(original.InvoiceNumber, result.GetProperty("invoiceNumber").GetString());
        await using var final = factory.Services.CreateAsyncScope(); var data = final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await data.Sales.ToListAsync());
        Assert.Equal(SaleStatus.Cancelled, (await data.Sales.SingleAsync()).Status);
        Assert.Equal(10m, (await data.InventoryBatches.SingleAsync()).Quantity);
        Assert.Single(await data.StockTransactions.ToListAsync());
        Assert.Single(await data.Set<SaleCancellation>().ToListAsync());
        Assert.Empty(await data.CustomerLedgerEntries.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppliedOrIssuedRedeemedCredit_NeverRestoresCreditHistory(bool applied)
    {
        var s = await SeedAsync();
        Guid noteId;
        Guid redemptionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;
            var sourceLine = s.Sale.Items[0];
            var other = Sale.Record(s.ShopId, s.ActorId, "credit-history-sale", "HASH", "A/2627/000002",
                [new(s.ShopId, SaleLineType.Goods, sourceLine.ItemId, s.BatchId, null, "Other sugar", "SUGAR", 2m, 60m, 100m, 100m, 0m, false, false)],
                s.CustomerId, "Buyer", null, PaymentMethod.Cash, 200m, 0m, now, BusinessDay.InDefaultZone(now)).Value;
            var origin = applied ? other : s.Sale;
            var line = origin.Items[0];
            var returned = SaleReturn.Record(s.ShopId, origin.Id, "RET-CREDIT", now, BusinessDay.InDefaultZone(now), s.ActorId, null, 100m, 0m, 100m, ReturnPayoutDestination.CreditNote, 100m, 0m, null, null,
                [new(s.ShopId, line.Id, 1m, SaleReturnCondition.Restockable, 60m, 100m, 0m, false, 100m, 100m, 100m, 0m, null)]).Value;
            var note = CreditNote.Issue(s.ShopId, returned.Id, 100m, "Credit history", "CN-CANCEL", null).Value;
            var redemption = note.Redeem(s.ShopId, applied ? s.Sale.Id : other.Id, 20m).Value;
            db.Add(other);
            noteId = note.Id; redemptionId = redemption.Id;
            db.AddRange(returned, note); await db.SaveChangesAsync();
            if (applied) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sales SET credit_note_applied_amount=20 WHERE id={s.Sale.Id}");
        }
        uint version;
        await using (var scope = factory.Services.CreateAsyncScope()) version = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Sales.SingleAsync(x => x.Id == s.Sale.Id)).Version;
        var response = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, version: version));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(applied ? "CreditRestorationRequired" : "ReturnHistoryBlocked", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var final = factory.Services.CreateAsyncScope(); var data = final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await data.CreditNotes.SingleAsync(x => x.Id == noteId);
        Assert.Equal(80m, persisted.AvailableBalance);
        Assert.False(persisted.IsVoided);
        Assert.Equal(redemptionId, (await data.CreditNoteRedemptions.SingleAsync()).Id);
        Assert.Equal(20m, (await data.CreditNoteRedemptions.SingleAsync()).Amount);
        await AssertUntouchedAsync(s);
    }

}
