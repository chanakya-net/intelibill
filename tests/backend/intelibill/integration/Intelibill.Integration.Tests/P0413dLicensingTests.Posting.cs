using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;
using Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Fact]
    public async Task Restricted_correction_posts_only_the_signed_delta()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var saleDate = BusinessCalendar.DateOf(_clock.Now);
        await ExpireAsync(client, source);
        var before = await FactsAsync(source);

        foreach (var (revised, delta) in new[] { (1100m, 100m), (1050m, -50m) })
        {
            var version = await ReceiptVersionAsync(source.Receipt);
            using var response = await CorrectAsync(client, source.Receipt, version, revised, NewKey());
            var result = await ReadSuccessAsync(response);
            Assert.Equal(delta, result.GetProperty("delta").GetDecimal());
            Assert.NotEqual(version, result.GetProperty("receiptVersion").GetUInt32());
        }

        var after = await FactsAsync(source);
        Assert.Equal(before with { Effective = 1050, Adjustments = 2, Ledger = before.Ledger + 2, Operations = before.Operations + 2 }, after);
        Assert.Equal(6m, after.Quantity);
        Assert.Equal(100m, after.Cost);
        await AssertPostedDeltasAsync(source);

        using var report = await client.GetAsync($"/api/sales/profit-loss?from={saleDate:yyyy-MM-dd}&to={saleDate:yyyy-MM-dd}", Ct);
        Assert.Equal(400m, (await ReadSuccessAsync(report)).GetProperty("summary").GetProperty("economics").GetProperty("cogs").GetDecimal());
        _output.WriteLine("Committed receipt 1000 → 1100 → 1050, linked ledger +100/-50; P&L COGS 400. Before {0}; after {1}.", before, after);
    }

    private async Task AssertPostedDeltasAsync(Source source)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1000m, await db.PurchaseReceiptValues.Where(x => x.Id == source.Receipt).Select(x => x.OriginalTotalCost).SingleAsync(Ct));
        var line = await db.SaleItems.SingleAsync(x => x.ShopId == source.Shop, Ct);
        Assert.Equal(4m, line.Quantity);
        Assert.Equal(100m, line.CostPrice);
        var owner = await db.ShopMemberships.Where(x => x.ShopId == source.Shop).Select(x => x.UserId).SingleAsync(Ct);
        var rows = await db.PurchaseReceiptCostAdjustments.Where(x => x.ShopId == source.Shop).OrderBy(x => x.PriorTotalCost).ToListAsync(Ct);
        Assert.Collection(rows,
            row => Assert.Equal((1000m, 1100m, 100m), (row.PriorTotalCost, row.RevisedTotalCost, row.Delta)),
            row => Assert.Equal((1100m, 1050m, -50m), (row.PriorTotalCost, row.RevisedTotalCost, row.Delta)));
        foreach (var row in rows)
        {
            Assert.Equal(_clock.Now, row.PerformedAt);
            Assert.Equal(owner, row.PerformedBy);
            Assert.Equal("Supplier cost corrected", row.Reason);
            Assert.Equal(source.Receipt, row.PurchaseReceiptValueId);
            Assert.Equal(source.Batch, row.InventoryBatchId);
            var entry = await db.SupplierLedgerEntries.SingleAsync(x => x.Id == row.SupplierLedgerEntryId, Ct);
            Assert.Equal(row.Delta, entry.Amount);
            Assert.Equal(SupplierLedgerEntryType.CostAdjusted, entry.EntryType);
            Assert.Equal(BusinessCalendar.DateOf(_clock.Now), entry.EntryDate);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Correction_observes_winning_restriction_while_waiting(bool accountScope)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        var version = await ReceiptVersionAsync(source.Receipt);
        var before = await FactsAsync(source);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "entitlement_snapshot_inbox", source.Shop);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await delivery.Database.OpenConnectionAsync(Ct);
        var projection = ApplyAsync(source.Binding, end, 2,
            restriction: accountScope ? "Security" : "Unrestricted", connection: delivery,
            securityEffectiveAt: accountScope ? null : _clock.Now);
        var blocker = await barrier.WaitForArrivalAsync();
        var request = CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        var waiter = await ObserveWaitAsync(request, blocker, barrier, projection);
        Assert.NotEqual(0, waiter);
        await AssertProblemAsync(await request, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(source));
        _output.WriteLine("PostgreSQL waiter PID {0}, blocker PID {1}; HTTP 403 Licence.SecuritySuspended ({2}); unchanged {3}.",
            waiter, blocker, accountScope ? "account" : "shop", before);
    }

    private async Task<int> ObserveWaitAsync(Task request, int blocker, TillPostgresBarrier barrier, Task projection)
    {
        try
        {
            return await WaitForBlockedRequestAsync(request, blocker);
        }
        finally
        {
            await barrier.ReleaseAsync();
            await projection;
        }
    }

    [Fact]
    public async Task Restricted_general_stock_cannot_be_posted_as_a_receipt_correction()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var countRoute = $"/api/inventory/batches/{source.Batch}/count";
        var adjustRoute = $"/api/inventory/batches/{source.Batch}/adjust";
        var version = await BatchVersionAsync(source.Batch);
        var count = new { expectedVersion = version, countedQuantity = 7, reason = "Receipt cost correction", operationKey = NewKey() };
        var increase = new { direction = "Increase", reason = "ReturnRestockCorrection", quantity = 1, notes = "Receipt cost correction" };
        var decrease = new { direction = "Decrease", reason = "Damaged", quantity = 1, notes = "Damaged stock" };

        await ExpireAsync(client, source);
        var before = await FactsAsync(source);
        foreach (var (route, payload) in new (string, object)[] { (countRoute, count), (adjustRoute, increase), (adjustRoute, decrease) })
            await AssertProblemAsync(await client.PostAsJsonAsync(route, payload, Ct), HttpStatusCode.Forbidden, "Licence.Expired");

        await AssertDirectStockDeniedAsync(host, client, source, version);
        Assert.Equal(before, await FactsAsync(source));
        await ApplyAsync(source.Binding, _clock.Now.AddDays(1), 2);
        var counted = await PostAsync(client, countRoute, count);
        Assert.Equal(7m, (await FactsAsync(source)).Quantity);
        await ApplyAsync(source.Binding, _clock.Now.AddDays(1), 3, restriction: "Trading");
        Assert.True(System.Text.Json.JsonElement.DeepEquals(counted, await PostAsync(client, countRoute, count)));
        await AssertProblemAsync(await client.PostAsJsonAsync(countRoute, count with { operationKey = NewKey() }, Ct),
            HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertProblemAsync(await client.PostAsJsonAsync(adjustRoute, decrease, Ct),
            HttpStatusCode.Forbidden, "Licence.TradingRestricted");
    }

    private static async Task AssertDirectStockDeniedAsync(
        ApiWebApplicationFactory host, HttpClient client, Source source, uint version)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var actor = Guid.Parse(jwt.Subject);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Intelibill.Infrastructure.Data.ApplicationDbContext>();
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.current_user_id', {actor.ToString("D")}, false),
                   set_config('app.active_shop_id', {source.Shop.ToString("D")}, false)
            """, Ct);
        var count = ActivatorUtilities.CreateInstance<CorrectStockCountCommandHandler>(scope.ServiceProvider);
        var result = await count.HandleAsync(new(source.Batch, actor, source.Shop, version, 7, "Receipt labelled count", null), Ct);
        Assert.Equal("Licence.Expired", result.FirstError.Code);

        var adjustment = ActivatorUtilities.CreateInstance<CreateAdjustmentCommandHandler>(scope.ServiceProvider);
        var adjusted = await adjustment.HandleAsync(new(source.Batch, actor, source.Shop, InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.ReturnRestockCorrection, 1, null, "Receipt labelled adjustment"), Ct);
        Assert.Equal("Licence.Expired", adjusted.FirstError.Code);
    }

    private async Task<uint> BatchVersionAsync(Guid batch)
    {
        await using var db = Open(Migrator);
        return await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Version).SingleAsync(Ct);
    }
}
