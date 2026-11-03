using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Returns, return voids and credit-note actions compete for one sale's entitlement. Each test runs
/// two real requests against real PostgreSQL, each with its own DbContext, and holds the first one
/// on its way into save — every read and decision made, nothing written — until the competitor has
/// either finished or is seen queued behind a lock. The interleaving under test is therefore the
/// one that actually happens, not whichever the scheduler picked.
/// </summary>
[Collection("Integration Tests")]
public sealed class SaleReturnConcurrencyTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture, services =>
    {
        services.AddSingleton<SaveBarrier>();
        services.AddScoped<UnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => new BarrierUnitOfWork(
            sp.GetRequiredService<UnitOfWork>(),
            sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<SaveBarrier>()));
    });

    public Task InitializeAsync() => _factory.InitializeAsync();

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

    private SaveBarrier Barrier => _factory.Services.GetRequiredService<SaveBarrier>();

    // ── Entitlement ─────────────────────────────────────────────────────────────

    /// <summary>A service line moves no stock, so no versioned row stands between two returns of it.</summary>
    [Fact]
    public async Task TwoServiceReturns_OnlyOneEntitlementConsumed()
    {
        var shop = await OpenShopAsync();
        var sale = await SellServiceAsync(shop);

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnRecorded,
            () => RecordReturnAsync(shop, sale.SaleId, ServiceLine(sale.ServiceLineId)),
            () => RecordReturnAsync(shop, sale.SaleId, ServiceLine(sale.ServiceLineId)));

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.BadRequest, "SaleReturn.QuantityExceedsRemaining");
        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.ServiceLineId));
        Assert.Equal(118m, await ActiveRefundTotalAsync(sale.SaleId));
    }

    /// <summary>A wasted goods line moves no stock either.</summary>
    [Fact]
    public async Task TwoWastageReturns_OnlyOneEntitlementConsumed()
    {
        var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnRecorded,
            () => RecordReturnAsync(shop, sale.SaleId, GoodsLine(sale.GoodsLineId, SaleReturnCondition.Wastage)),
            () => RecordReturnAsync(shop, sale.SaleId, GoodsLine(sale.GoodsLineId, SaleReturnCondition.Wastage)));

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.BadRequest, "SaleReturn.QuantityExceedsRemaining");
        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.GoodsLineId));
        Assert.Equal(118m, await ActiveRefundTotalAsync(sale.SaleId));
        Assert.Equal(sale.StockAfterSale, await BatchQuantityAsync(sale.BatchId));
    }

    /// <summary>
    /// The batch row version alone would refuse the second write, but as a generic conflict that
    /// says nothing about entitlement. Under the sale lock the loser re-reads and is told why.
    /// </summary>
    [Fact]
    public async Task TwoRestockableReturns_StockAddedOnce()
    {
        var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnRecorded,
            () => RecordReturnAsync(shop, sale.SaleId, GoodsLine(sale.GoodsLineId, SaleReturnCondition.Restockable)),
            () => RecordReturnAsync(shop, sale.SaleId, GoodsLine(sale.GoodsLineId, SaleReturnCondition.Restockable)));

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.BadRequest, "SaleReturn.QuantityExceedsRemaining");
        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.GoodsLineId));
        Assert.Equal(sale.StockAfterSale + 1m, await BatchQuantityAsync(sale.BatchId));
        Assert.Equal(sale.StockAfterSale + 1m, await InventoryQuantityAsync(sale.ItemId));
        Assert.Equal(1, await ReturnMovementCountAsync(sale.BatchId));
    }

    [Fact]
    public async Task MixedGoodsAndServiceReturns_EachLineBounded()
    {
        var shop = await OpenShopAsync();
        var sale = await SellGoodsAndServiceAsync(shop);
        object[] lines =
        [
            GoodsLine(sale.GoodsLineId, SaleReturnCondition.Restockable),
            ServiceLine(sale.ServiceLineId),
        ];

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnRecorded,
            () => RecordReturnAsync(shop, sale.SaleId, lines),
            () => RecordReturnAsync(shop, sale.SaleId, lines));

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.BadRequest, "SaleReturn.QuantityExceedsRemaining");
        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.GoodsLineId));
        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.ServiceLineId));
        Assert.Equal(236m, await ActiveRefundTotalAsync(sale.SaleId));
        Assert.Equal(sale.StockAfterSale + 1m, await BatchQuantityAsync(sale.BatchId));
    }

    /// <summary>
    /// A due reduction is capped by the customer's balance across all of their sales, so returns on
    /// two different sales of one customer must not both spend the same ₹118 of due.
    /// </summary>
    [Fact]
    public async Task ReturnsOnTwoSalesOfOneCustomer_ReduceDueOnce()
    {
        var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var onCredit = await SellAsync(shop, [ServiceSaleLine(await AddServiceAsync(shop))], 118m, customerId);
        var paid = await SellAsync(shop, [ServiceSaleLine(await AddServiceAsync(shop))], 118m, customerId, paidInFull: true);

        var (first, second) = await RaceAsync(
            HoldWhen.ReturnRecorded,
            () => RecordReturnAsync(shop, onCredit.SaleId, ServiceLine(onCredit.ServiceLineId)),
            () => RecordReturnAsync(shop, paid.SaleId, ServiceLine(paid.ServiceLineId)));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var dueReduced = await QueryAsync(db => db.SaleReturns
            .Where(r => r.SaleId == onCredit.SaleId || r.SaleId == paid.SaleId)
            .SumAsync(r => r.DueReductionAmount));
        Assert.Equal(118m, dueReduced);
        Assert.Equal(118m, await QueryAsync(db => db.CustomerLedgerEntries
            .Where(e => e.CustomerId == customerId && e.EntryType == CustomerLedgerEntryType.ReturnCredit)
            .SumAsync(e => e.Amount)));
    }

    /// <summary>A preview is advice. Posting re-reads entitlement under the lock.</summary>
    [Fact]
    public async Task PreviewThenCompetingReturn_PostingRevalidates()
    {
        var shop = await OpenShopAsync();
        var sale = await SellServiceAsync(shop);

        using (var preview = await SendAsync(shop, HttpMethod.Post, $"/api/sales/{sale.SaleId}/returns/preview", new
        {
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            items = new[] { ServiceLine(sale.ServiceLineId) },
        }))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        }

        using (var competing = await RecordReturnAsync(shop, sale.SaleId, ServiceLine(sale.ServiceLineId)))
            Assert.Equal(HttpStatusCode.OK, competing.StatusCode);

        using var posting = await RecordReturnAsync(shop, sale.SaleId, ServiceLine(sale.ServiceLineId));
        await AssertRefusedAsync(posting, HttpStatusCode.BadRequest, "SaleReturn.QuantityExceedsRemaining");
        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.ServiceLineId));
    }

    // ── Voids ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A due-reducing service return voided twice at once. Exactly one void may post the reversal of
    /// the customer's credit; the other learns the return is already voided.
    /// </summary>
    [Fact]
    public async Task DoubleVoid_CreatesOneCreditReversal()
    {
        var shop = await OpenShopAsync();
        var sale = await SellServiceOnCreditAsync(shop);
        using (var recorded = await RecordReturnAsync(shop, sale.SaleId, ServiceLine(sale.ServiceLineId)))
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var returnId = await SingleReturnIdAsync(sale.SaleId);
        Assert.Equal(1, await LedgerCountAsync(sale.SaleId, CustomerLedgerEntryType.ReturnCredit));

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnVoided,
            () => VoidReturnAsync(shop, returnId),
            () => VoidReturnAsync(shop, returnId));

        Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.BadRequest, "SaleReturn.AlreadyVoided");
        Assert.Equal(1, await LedgerCountAsync(sale.SaleId, CustomerLedgerEntryType.ReturnCreditReversal));
        Assert.Equal(0m, await ActiveReturnedQuantityAsync(sale.ServiceLineId));
    }

    /// <summary>Restocked goods are pulled back exactly once.</summary>
    [Fact]
    public async Task DoubleVoidOfRestockedReturn_RemovesStockOnce()
    {
        var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);
        using (var recorded = await RecordReturnAsync(shop, sale.SaleId, GoodsLine(sale.GoodsLineId, SaleReturnCondition.Restockable)))
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var returnId = await SingleReturnIdAsync(sale.SaleId);

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnVoided,
            () => VoidReturnAsync(shop, returnId),
            () => VoidReturnAsync(shop, returnId));

        Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.BadRequest, "SaleReturn.AlreadyVoided");
        Assert.Equal(sale.StockAfterSale, await BatchQuantityAsync(sale.BatchId));
        Assert.Equal(sale.StockAfterSale, await InventoryQuantityAsync(sale.ItemId));
    }

    /// <summary>A void that has decided holds the credit note; a redemption waits and finds it void.</summary>
    [Fact]
    public async Task VoidThenRedeem_RedemptionRefused()
    {
        var shop = await OpenShopAsync();
        var note = await IssueCreditNoteAsync(shop);

        var (winner, loser) = await RaceAsync(
            HoldWhen.ReturnVoided,
            () => VoidReturnAsync(shop, note.ReturnId),
            () => RedeemAsync(shop, note));

        Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.Conflict, "CreditNote.VoidedCannotRedeem");
        await AssertNoVoidedRedeemedNoteAsync(note.Code);
    }

    /// <summary>A redemption that has decided holds the credit note; a void waits and finds it spent.</summary>
    [Fact]
    public async Task RedeemThenVoid_VoidRefused()
    {
        var shop = await OpenShopAsync();
        var note = await IssueCreditNoteAsync(shop);

        var (winner, loser) = await RaceAsync(
            HoldWhen.CreditNoteRedeemed,
            () => RedeemAsync(shop, note),
            () => VoidReturnAsync(shop, note.ReturnId));

        Assert.Equal(HttpStatusCode.Created, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.Conflict, "SaleReturn.CreditNoteHasRedemptions");
        await AssertNoVoidedRedeemedNoteAsync(note.Code);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.SaleReturns.Where(r => r.Id == note.ReturnId).Select(r => r.IsVoided).SingleAsync());
    }

    /// <summary>Voiding the credit note directly races the same redemption.</summary>
    [Fact]
    public async Task RedeemThenVoidCreditNote_VoidRefused()
    {
        var shop = await OpenShopAsync();
        var note = await IssueCreditNoteAsync(shop);

        var (winner, loser) = await RaceAsync(
            HoldWhen.CreditNoteRedeemed,
            () => RedeemAsync(shop, note),
            () => SendAsync(shop, HttpMethod.Post, $"/api/credit-notes/{note.Code}/void", new { reason = "Issued in error" }));

        Assert.Equal(HttpStatusCode.Created, winner.StatusCode);
        await AssertRefusedAsync(loser, HttpStatusCode.Conflict, "CreditNote.CannotVoidAfterRedemption");
        await AssertNoVoidedRedeemedNoteAsync(note.Code);
    }

    // ── Lock contract ───────────────────────────────────────────────────────────

    /// <summary>
    /// A sale held longer than the lock budget answers a retryable conflict, and the abandoned
    /// attempt leaves nothing behind.
    /// </summary>
    [Fact]
    public async Task LockTimeout_ReturnsRetryableConflictWithNoEffects()
    {
        var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);

        await using (await HoldSaleRowAsync(sale.SaleId))
        {
            using var response = await RecordReturnAsync(shop, sale.SaleId, GoodsLine(sale.GoodsLineId, SaleReturnCondition.Restockable));
            await AssertRefusedAsync(response, HttpStatusCode.Conflict, "SaleReturn.SaleBusy");
        }

        Assert.Equal(0m, await ActiveReturnedQuantityAsync(sale.GoodsLineId));
        Assert.Equal(sale.StockAfterSale, await BatchQuantityAsync(sale.BatchId));
        Assert.Equal(0, await ReturnMovementCountAsync(sale.BatchId));
    }

    /// <summary>
    /// The lock is scoped to the caller's shop: another shop's sale is not found, and is answered at
    /// once rather than after queueing behind whoever holds it.
    /// </summary>
    [Fact]
    public async Task OtherShopsSale_NotFoundWithoutWaitingOnItsLock()
    {
        var owner = await OpenShopAsync();
        var outsider = await OpenShopAsync();
        var sale = await SellServiceAsync(owner);
        using (var recorded = await RecordReturnAsync(owner, sale.SaleId, ServiceLine(sale.ServiceLineId)))
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var returnId = await SingleReturnIdAsync(sale.SaleId);

        await using (await HoldSaleRowAsync(sale.SaleId))
        {
            var elapsed = Stopwatch.StartNew();
            using var record = await RecordReturnAsync(outsider, sale.SaleId, ServiceLine(sale.ServiceLineId));
            using var voided = await VoidReturnAsync(outsider, returnId);
            elapsed.Stop();

            Assert.Equal(HttpStatusCode.NotFound, record.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, voided.StatusCode);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3), $"Cross-shop requests waited {elapsed.Elapsed}.");
        }

        Assert.Equal(1m, await ActiveReturnedQuantityAsync(sale.ServiceLineId));
    }

    // ── Race harness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts <paramref name="first"/> and holds it at save once it has made every decision; starts
    /// <paramref name="second"/>; releases the first once the second has either finished or queued
    /// behind a lock. Returns the first response and the second.
    /// </summary>
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
        await WaitForFinishOrLockWaitAsync(secondTask);
        pause.Release();

        var responses = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));
        return (responses[0], responses[1]);
    }

    private async Task WaitForFinishOrLockWaitAsync(Task competitor)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
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

    /// <summary>Holds the sale row from outside the app, as a stalled transaction would.</summary>
    private async Task<IAsyncDisposable> HoldSaleRowAsync(Guid saleId)
    {
        var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM sales WHERE id = {saleId} FOR UPDATE")
            .ToListAsync();
        Assert.Single(locked);
        return new HeldRow(scope, transaction);
    }

    private sealed class HeldRow(AsyncServiceScope scope, IAsyncDisposable transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await scope.DisposeAsync();
        }
    }

    private static class HoldWhen
    {
        public static readonly Func<ChangeTracker, bool> ReturnRecorded =
            tracker => tracker.Entries<SaleReturn>().Any(e => e.State == EntityState.Added);

        public static readonly Func<ChangeTracker, bool> ReturnVoided =
            tracker => tracker.Entries<SaleReturn>().Any(e => e.State == EntityState.Modified && e.Entity.IsVoided);

        public static readonly Func<ChangeTracker, bool> CreditNoteRedeemed =
            tracker => tracker.Entries<CreditNoteRedemption>().Any(e => e.State == EntityState.Added);
    }

    /// <summary>
    /// One-shot hold at <see cref="IUnitOfWork.SaveChangesAsync"/> for the first save whose pending
    /// changes match; every later save passes straight through.
    /// </summary>
    internal sealed class SaveBarrier
    {
        private readonly Lock _gate = new();
        private Pause? _armed;

        public Pause Hold(Func<ChangeTracker, bool> when)
        {
            lock (_gate)
                return _armed = new Pause(when);
        }

        public Task ArriveAsync(ChangeTracker tracker)
        {
            Pause? pause;
            lock (_gate)
            {
                pause = _armed;
                if (pause is null || !pause.When(tracker))
                    return Task.CompletedTask;
                _armed = null;
            }

            pause.Reached.TrySetResult();
            return pause.WaitAsync();
        }
    }

    internal sealed class Pause(Func<ChangeTracker, bool> when)
    {
        public Func<ChangeTracker, bool> When { get; } = when;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => Released.TrySetResult();

        internal Task WaitAsync() => Released.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class BarrierUnitOfWork(UnitOfWork inner, ApplicationDbContext db, SaveBarrier barrier) : IUnitOfWork
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

    // ── Seeding ─────────────────────────────────────────────────────────────────

    private sealed record Shop(HttpClient Client, string Token) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record SoldSale(Guid SaleId, Guid GoodsLineId, Guid ServiceLineId, Guid BatchId, Guid ItemId, decimal StockAfterSale);

    private sealed record IssuedCreditNote(Guid ReturnId, string Code, Guid ServiceId);

    private async Task<Shop> OpenShopAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var enrolled = await client.EnrollAsync(new
        {
            email = $"returns-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Return",
            lastName = "Owner",
        });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

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
        using var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var shopToken = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return new Shop(client, shopToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(Shop shop, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shop.Token);
        request.Content = JsonContent.Create(body);
        return await shop.Client.SendAsync(request);
    }

    private static async Task<(Guid ItemId, Guid BatchId, string Barcode)> AddStockAsync(Shop shop)
    {
        var barcode = $"RET-{Guid.NewGuid():N}";
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", new
        {
            itemName = "Return Item",
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "pcs",
            batchNumber = "B-001",
            quantity = 10m,
            totalPurchaseCost = 800m,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("itemId").GetGuid(), body.GetProperty("inventoryBatchId").GetGuid(), barcode);
    }

    private static async Task<Guid> AddServiceAsync(Shop shop)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/services", new
        {
            name = $"Service {Guid.NewGuid():N}"[..20],
            description = "Return test service",
            price = 100m,
            hsnCode = "9987",
            taxRatePercent = 18m,
            taxIncluded = false,
            isActive = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("serviceId").GetGuid();
    }

    private static object GoodsSaleLine(string barcode, Guid batchId) => new
    {
        barcode,
        batchNumber = "B-001",
        itemName = "Return Item",
        quantity = 1m,
        costPrice = 80m,
        salesPrice = 100m,
        mrp = 120m,
        taxRatePercent = 18m,
        isPriceIncludingTax = false,
        inventoryBatchId = batchId,
    };

    private static object ServiceSaleLine(Guid serviceId) => new
    {
        barcode = "SVC-RET",
        batchNumber = string.Empty,
        itemName = "Return Service",
        quantity = 1m,
        costPrice = 0m,
        salesPrice = 100m,
        mrp = 0m,
        taxRatePercent = 18m,
        isPriceIncludingTax = false,
        inventoryBatchId = Guid.Empty,
        lineType = "Service",
        serviceId,
    };

    private async Task<SoldSale> SellAsync(Shop shop, object[] lines, decimal total, Guid? customerId = null, Guid? batchId = null, Guid? itemId = null, bool paidInFull = false)
    {
        var onCredit = customerId.HasValue && !paidInFull;
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = customerId.HasValue ? "Due Customer" : "Walk-in Customer",
            customerPhone = customerId.HasValue ? "+919812345678" : "+919876543210",
            paymentMethod = onCredit ? (int)PaymentMethod.Credit : (int)PaymentMethod.Cash,
            paidAmount = onCredit ? 0m : total,
            dueAmount = onCredit ? total : 0m,
            items = lines,
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        var saleId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("saleId").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var items = await db.SaleItems.Where(i => i.SaleId == saleId).ToListAsync();
        var goods = items.SingleOrDefault(i => i.LineType == SaleLineType.Goods);
        var service = items.SingleOrDefault(i => i.LineType == SaleLineType.Service);
        var stock = batchId.HasValue
            ? await db.InventoryBatches.Where(b => b.Id == batchId).Select(b => b.Quantity).SingleAsync()
            : 0m;
        return new SoldSale(saleId, goods?.Id ?? Guid.Empty, service?.Id ?? Guid.Empty, batchId ?? Guid.Empty, itemId ?? Guid.Empty, stock);
    }

    private async Task<SoldSale> SellGoodsAsync(Shop shop)
    {
        var (itemId, batchId, barcode) = await AddStockAsync(shop);
        return await SellAsync(shop, [GoodsSaleLine(barcode, batchId)], 118m, batchId: batchId, itemId: itemId);
    }

    private async Task<SoldSale> SellServiceAsync(Shop shop) =>
        await SellAsync(shop, [ServiceSaleLine(await AddServiceAsync(shop))], 118m);

    private async Task<SoldSale> SellGoodsAndServiceAsync(Shop shop)
    {
        var (itemId, batchId, barcode) = await AddStockAsync(shop);
        var serviceId = await AddServiceAsync(shop);
        return await SellAsync(shop, [GoodsSaleLine(barcode, batchId), ServiceSaleLine(serviceId)], 236m, batchId: batchId, itemId: itemId);
    }

    private static async Task<Guid> AddCustomerAsync(Shop shop)
    {
        using var customer = await SendAsync(shop, HttpMethod.Post, "/api/customers", new
        {
            name = "Due Customer",
            phoneNumber = "+919812345678",
            address = (string?)null,
            isActive = true,
        });
        customer.EnsureSuccessStatusCode();
        return (await customer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();
    }

    private async Task<SoldSale> SellServiceOnCreditAsync(Shop shop) =>
        await SellAsync(shop, [ServiceSaleLine(await AddServiceAsync(shop))], 118m, await AddCustomerAsync(shop));

    private async Task<IssuedCreditNote> IssueCreditNoteAsync(Shop shop)
    {
        var sale = await SellServiceAsync(shop);
        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/sales/{sale.SaleId}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.CreditNote,
            notes = "Store credit",
            creditNoteReason = "Store credit",
            items = new[] { ServiceLine(sale.ServiceLineId) },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);

        var returnId = await SingleReturnIdAsync(sale.SaleId);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var code = await db.CreditNotes.Where(c => c.SaleReturnId == returnId).Select(c => c.Code).SingleAsync();
        return new IssuedCreditNote(returnId, code, await AddServiceAsync(shop));
    }

    // ── Requests ────────────────────────────────────────────────────────────────

    private static object ServiceLine(Guid saleItemId) => new
    {
        saleItemId,
        quantity = 1m,
        lineType = (int)SaleLineType.Service,
        condition = (int?)null,
        approvedRefundAmount = 118m,
        notes = (string?)null,
    };

    private static object GoodsLine(Guid saleItemId, SaleReturnCondition condition) => new
    {
        saleItemId,
        quantity = 1m,
        lineType = (int)SaleLineType.Goods,
        condition = (int)condition,
        approvedRefundAmount = 118m,
        notes = condition == SaleReturnCondition.Wastage ? "Damaged on return" : null,
    };

    private static Task<HttpResponseMessage> RecordReturnAsync(Shop shop, Guid saleId, params object[] lines) =>
        SendAsync(shop, HttpMethod.Post, $"/api/sales/{saleId}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Concurrent return",
            items = lines,
        });

    private static Task<HttpResponseMessage> VoidReturnAsync(Shop shop, Guid returnId) =>
        SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{returnId}/void", new { reason = "Recorded twice" });

    private static Task<HttpResponseMessage> RedeemAsync(Shop shop, IssuedCreditNote note) =>
        SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Credit Customer",
            customerPhone = "+919876543299",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 18m,
            dueAmount = 0m,
            creditNoteAppliedAmount = 100m,
            creditNoteRedemptions = new[] { new { code = note.Code, amount = 100m } },
            items = new[] { ServiceSaleLine(note.ServiceId) },
        });

    // ── Independent reads ───────────────────────────────────────────────────────

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {content}");
        Assert.Contains(code, content, StringComparison.Ordinal);
    }

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<decimal> ActiveReturnedQuantityAsync(Guid saleItemId) =>
        QueryAsync(db => (from line in db.SaleReturnItems
                          join r in db.SaleReturns on line.SaleReturnId equals r.Id
                          where line.SaleItemId == saleItemId && !r.IsVoided
                          select line.Quantity).SumAsync());

    private Task<decimal> ActiveRefundTotalAsync(Guid saleId) =>
        QueryAsync(db => db.SaleReturns.Where(r => r.SaleId == saleId && !r.IsVoided).SumAsync(r => r.TotalRefundAmount));

    private Task<Guid> SingleReturnIdAsync(Guid saleId) =>
        QueryAsync(db => db.SaleReturns.Where(r => r.SaleId == saleId).Select(r => r.Id).SingleAsync());

    private Task<decimal> BatchQuantityAsync(Guid batchId) =>
        QueryAsync(db => db.InventoryBatches.Where(b => b.Id == batchId).Select(b => b.Quantity).SingleAsync());

    private Task<decimal> InventoryQuantityAsync(Guid itemId) =>
        QueryAsync(db => db.Inventory.Where(i => i.ItemId == itemId).Select(i => i.Quantity).SingleAsync());

    private Task<int> ReturnMovementCountAsync(Guid batchId) =>
        QueryAsync(db => db.StockTransactions.CountAsync(t => t.InventoryBatchId == batchId && t.TransactionType == StockTransactionType.Ret));

    private Task<int> LedgerCountAsync(Guid saleId, CustomerLedgerEntryType type) =>
        QueryAsync(db => db.CustomerLedgerEntries.CountAsync(e => e.SaleId == saleId && e.EntryType == type));

    private async Task AssertNoVoidedRedeemedNoteAsync(string code)
    {
        var (isVoided, redemptions) = await QueryAsync(async db =>
        {
            var note = await db.CreditNotes.AsNoTracking().SingleAsync(c => c.Code == code);
            return (note.IsVoided, await db.CreditNoteRedemptions.CountAsync(r => r.CreditNoteId == note.Id));
        });

        Assert.False(isVoided && redemptions > 0, "A credit note was both voided and redeemed.");
        Assert.True(isVoided ^ (redemptions > 0), $"Exactly one of void or redemption must win (voided {isVoided}, redemptions {redemptions}).");
    }
}
