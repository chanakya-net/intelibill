using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
public sealed class SaleCancellationConcurrencyTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory factory = new(fixture, services =>
    {
        services.AddSingleton<SaleReturnConcurrencyTests.SaveBarrier>();
        services.AddScoped<UnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => new BarrierUnitOfWork(sp.GetRequiredService<UnitOfWork>(), sp.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<SaleReturnConcurrencyTests.SaveBarrier>()));
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


    private static Task<HttpResponseMessage> CancelAsync(Seed s, string key = "cancel-key") =>
        s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/cancellations", Request(s, key));
    private static Task<HttpResponseMessage> ReturnAsync(Seed s) => s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/returns", new
    {
        payoutDestination = (int)ReturnPayoutDestination.Refund,
        notes = "Goods returned",
        items = new[] { new { saleItemId = s.Sale.Items[0].Id, quantity = 2m, lineType = (int)SaleLineType.Goods, condition = (int)SaleReturnCondition.Restockable } }
    });
    private static Task<HttpResponseMessage> CollectAsync(Seed s, bool allocated) => s.Client.PostAsJsonAsync($"/api/customers/{s.CustomerId}/payments", new
    {
        operationKey = "collect-key", amount = 20m, paymentDate = s.Sale.BusinessDate, method = "Cash",
        allocations = allocated ? new[] { new { targetId = s.Sale.Id, amount = 20m } } : []
    });
    private static bool Cancelling(ChangeTracker t) => t.Entries<SaleCancellation>().Any(e => e.State == EntityState.Added);
    private static bool Returning(ChangeTracker t) => t.Entries<SaleReturn>().Any(e => e.State == EntityState.Added);
    private static bool Collecting(ChangeTracker t) => t.Entries<PaymentReceipt>().Any(e => e.State == EntityState.Added);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReturnAndCancellation_SerializeBothWinningOrders(bool cancelFirst)
    {
        var s = await SeedAsync(0m);
        var (first, second) = await RaceAsync(cancelFirst ? Cancelling : Returning,
            () => cancelFirst ? CancelAsync(s) : ReturnAsync(s),
            () => cancelFirst ? ReturnAsync(s) : CancelAsync(s));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(cancelFirst ? "SaleReturn.SaleCancelled" : "ReturnHistoryBlocked", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertEffectsAsync(s, cancelFirst, 0m, 0, 0);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task CollectionAndCancellation_SerializeBothWinningOrders(bool cancelFirst, bool allocated)
    {
        var s = await SeedAsync(0m);
        var (first, second) = await RaceAsync(cancelFirst ? Cancelling : Collecting,
            () => cancelFirst ? CancelAsync(s) : CollectAsync(s, allocated),
            () => cancelFirst ? CollectAsync(s, allocated) : CancelAsync(s));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        if (cancelFirst && !allocated) Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        else Assert.False(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
        if (!cancelFirst) Assert.Contains("CollectionsRequireReview", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertEffectsAsync(s, cancelFirst, cancelFirst ? (allocated ? 0m : -20m) : 180m,
            cancelFirst && allocated ? 0 : 1, !cancelFirst && allocated ? 1 : 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationKeys_CommitOneImmutableCorrection(bool sameKey)
    {
        var s = await SeedAsync(80m);
        var (first, second) = await RaceAsync(Cancelling, () => CancelAsync(s), () => CancelAsync(s, sameKey ? "cancel-key" : "another-key"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(sameKey ? HttpStatusCode.OK : HttpStatusCode.Conflict, second.StatusCode);
        if (sameKey) Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        await AssertEffectsAsync(s, true, 0m, 0, 0);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OperationReceipts.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentBatchCount_CancellationRollsBackAllPendingEffects()
    {
        var s = await SeedAsync(80m);
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Reuse an existing open fence: creating the first period is itself serialized.
            db.Add(PostingPeriod.Open(s.ShopId, s.Sale.BusinessDate));
            await db.SaveChangesAsync();
        }
        var pause = Barrier.Hold(Cancelling);
        var pending = CancelAsync(s);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var batch = await db.InventoryBatches.SingleAsync();
            // A real stock count commits without the sale lock, changing xmin after cancellation read.
            var count = await s.Client.PostAsJsonAsync($"/api/inventory/batches/{s.BatchId}/count", new
            {
                expectedVersion = batch.Version, countedQuantity = 7m, reason = "Physical count", operationKey = "count-key",
            });
            Assert.True(count.IsSuccessStatusCode, await count.Content.ReadAsStringAsync());
        }
        finally { pause.Release(); }
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var final = factory.Services.CreateAsyncScope(); var data = final.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(7m, (await data.InventoryBatches.SingleAsync()).Quantity);
        Assert.Equal(7m, (await data.Inventory.SingleAsync()).Quantity);
        Assert.Equal(SaleStatus.Posted, (await data.Sales.SingleAsync()).Status);
        Assert.Empty(await data.Set<SaleCancellation>().ToListAsync());
        Assert.Single(await data.OperationReceipts.ToListAsync());
        Assert.Single(await data.StockTransactions.ToListAsync());
        Assert.Single(await data.StockCounts.ToListAsync());
        Assert.Empty(await data.StockTransactions.Where(x => x.TransactionType == StockTransactionType.SaleCancellation).ToListAsync());
        Assert.Empty(await data.CustomerLedgerEntries.Where(e => e.EntryType == CustomerLedgerEntryType.SaleCancellationCredit).ToListAsync());
    }

    [Fact]
    public async Task PreLockTrackedSale_ReturnGuardReadsCommittedCancellation()
    {
        var s = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var preLock = await db.Sales.Include(x => x.Items).SingleAsync();
        Assert.Equal(SaleStatus.Posted, preLock.Status);
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(s)).StatusCode);
        var handler = ActivatorUtilities.CreateInstance<Intelibill.Application.Features.Sales.Commands.RecordSaleReturn.RecordSaleReturnCommandHandler>(scope.ServiceProvider);
        var response = await handler.HandleAsync(new(s.ActorId, s.ShopId, s.Sale.Id, ReturnPayoutDestination.Refund, null, null, null, null, null,
            [new(s.Sale.Items[0].Id, 2m, SaleLineType.Goods, SaleReturnCondition.Restockable, null, null)]), CancellationToken.None);
        Assert.True(response.IsError);
        Assert.Equal("SaleReturn.SaleCancelled", response.FirstError.Code);
        await AssertEffectsAsync(s, true, 0m, 0, 0);
    }

    private async Task AssertEffectsAsync(Seed s, bool cancelled, decimal balance, int receipts, int allocations)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cancellation = await db.Set<SaleCancellation>().SingleOrDefaultAsync();
        Assert.Equal(cancelled, cancellation is not null);
        var returned = await db.SaleReturns.SingleOrDefaultAsync();
        Assert.False(cancellation is not null && returned is not null);
        var quantity = cancelled || returned is not null ? 10m : 8m;
        Assert.Equal(quantity, (await db.InventoryBatches.SingleAsync()).Quantity);
        Assert.Equal(quantity, (await db.Inventory.SingleAsync()).Quantity);
        Assert.Equal(cancelled ? SaleStatus.Cancelled : SaleStatus.Posted, (await db.Sales.SingleAsync()).Status);
        Assert.Equal(balance, await scope.ServiceProvider.GetRequiredService<ICustomerLedgerEntryRepository>().GetCustomerBalanceAsync(s.ShopId, s.CustomerId));
        Assert.Equal(receipts, await db.PaymentReceipts.CountAsync());
        Assert.Equal(allocations, await db.PaymentAllocations.CountAsync());
        Assert.Equal(cancelled && s.Sale.DueAmount > 0m ? 1 : 0, await db.CustomerLedgerEntries.CountAsync(e => e.EntryType == CustomerLedgerEntryType.SaleCancellationCredit));
        Assert.Equal(cancelled ? s.Sale.PaidAmount : 0m, cancellation?.CashRefundAmount ?? 0m);
        Assert.Equal(cancelled || returned is not null ? 1 : 0, await db.StockTransactions.CountAsync());
    }
    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RaceAsync(
        Func<ChangeTracker, bool> holdWhen,
        Func<Task<HttpResponseMessage>> first,
        Func<Task<HttpResponseMessage>> second)
    {
        var pause = Barrier.Hold(holdWhen);
        var firstTask = Task.Run(first);
        await Task.WhenAny(pause.Reached.Task, firstTask).Unwrap().WaitAsync(TimeSpan.FromSeconds(20));
        if (!pause.Reached.Task.IsCompleted)
        {
            var early = await firstTask;
            Assert.Fail($"The first request finished without reaching save: {early.StatusCode} {await early.Content.ReadAsStringAsync()}");
        }

        var secondTask = Task.Run(second);
        try { await WaitForFinishOrLockWaitAsync(secondTask); }
        finally { pause.Release(); }

        var responses = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));
        return (responses[0], responses[1]);
    }

    private async Task WaitForFinishOrLockWaitAsync(Task competitor)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (competitor.IsCompleted)
                return;

            // A backend blocked by another's row lock — the competitor queued behind the held
            // request — rather than anything merely waiting on some lock.
            var waiting = await db.Database
                .SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND backend_type = 'client backend' AND cardinality(pg_blocking_pids(pid)) > 0")
                .SingleAsync();
            if (waiting > 0)
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("The competing request neither finished nor queued behind a lock.");
    }

    private SaleReturnConcurrencyTests.SaveBarrier Barrier => factory.Services.GetRequiredService<SaleReturnConcurrencyTests.SaveBarrier>();
    private sealed class BarrierUnitOfWork(UnitOfWork inner, ApplicationDbContext db, SaleReturnConcurrencyTests.SaveBarrier barrier) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            db.ChangeTracker.DetectChanges();
            await barrier.ArriveAsync(db.ChangeTracker);
            return await inner.SaveChangesAsync(cancellationToken);
        }

        public void ClearChanges() => inner.ClearChanges();

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => inner.CommitTransactionAsync(cancellationToken);

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => inner.RollbackTransactionAsync(cancellationToken);
    }

}
