using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Fact]
    public async Task TradingSuspensionPermitsDisposalAndRefusesEligibleReleaseOnTheSameRoute()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        await fixture.DrainAsync(source: true);
        await EnrollAsync(fixture, shop);
        await using var db = MerchantDb(database);
        var (batchId, root) = await QuarantineReturnAsync(fixture, shop);
        var before = await db.InventoryBatches.AsNoTracking().SingleAsync(row => row.Id == batchId);
        using var releaseBefore = await PostDispositionAsync(shop, before, root, "Release");
        Assert.True(releaseBefore.StatusCode == HttpStatusCode.OK, await releaseBefore.Content.ReadAsStringAsync());
        var eligible = await db.InventoryBatches.AsNoTracking().SingleAsync(row => row.Id == batchId);
        Assert.Equal((5m, 1m), (eligible.Quantity, eligible.QuarantinedQuantity));

        using var admin = await SignInSecurityAsync(fixture);
        await ApplySuspensionAsync(admin, account, "Shop", shop.ShopId, "Trading");
        await fixture.DrainAsync(source: true);
        using var release = await PostDispositionAsync(shop, eligible, root, "Release");
        Assert.Equal(HttpStatusCode.Forbidden, release.StatusCode);
        Assert.Equal("Licence.TradingRestricted", await P0206Fixture.CodeAsync(release));
        using var dispose = await PostDispositionAsync(shop, eligible, root, "Dispose");
        Assert.True(dispose.StatusCode == HttpStatusCode.OK, await dispose.Content.ReadAsStringAsync());

        var saved = await db.InventoryBatches.AsNoTracking().SingleAsync(row => row.Id == batchId);
        Assert.Equal((4m, 0m), (saved.Quantity, saved.QuarantinedQuantity));
        var dispositions = await db.BatchDispositionChanges.Where(row => row.RootChangeId == root).ToListAsync();
        Assert.Single(dispositions, row => row.Action == "Release");
        Assert.Single(dispositions, row => row is { Action: "Dispose", InventoryAdjustmentId: not null });
    }

    private static async Task<(Guid BatchId, Guid RootId)> QuarantineReturnAsync(P0213Fixture fixture, Merchant shop)
    {
        await P0209Tests.PostStockAsync(shop.Client, "Fixture lot without expiry");
        await using var db = MerchantDb(fixture.Hosts.Database);
        var batch = await db.InventoryBatches.AsNoTracking().Include(row => row.Item).SingleAsync(row => row.ShopId == shop.ShopId);
        var saleId = await SellFixtureGoodsAsync(shop, batch);
        using var policy = await shop.Client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, policy = new { mode = 0, expiryRequired = true, sellThroughExpiryDate = true,
                warningDays = 7, overrideAllowed = false }, reason = "Require original expiry evidence",
        });
        Assert.True(policy.StatusCode == HttpStatusCode.OK, await policy.Content.ReadAsStringAsync());
        var line = await db.SaleItems.Where(row => row.SaleId == saleId).Select(row => row.Id).SingleAsync();
        using var returned = await shop.Client.PostAsJsonAsync($"/api/sales/{saleId:D}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            items = new[] { new { saleItemId = line, quantity = 2, lineType = (int)SaleLineType.Goods,
                condition = (int)SaleReturnCondition.Restockable } },
        });
        Assert.True(returned.StatusCode == HttpStatusCode.OK, await returned.Content.ReadAsStringAsync());
        var root = await db.BatchDispositionChanges.SingleAsync(row => row.InventoryBatchId == batch.Id);
        Assert.Equal("Expiry.RequiredDateMissing", root.ReasonCode);
        var held = await db.InventoryBatches.AsNoTracking().SingleAsync(row => row.Id == batch.Id);
        using var corrected = await shop.Client.PutAsJsonAsync($"/api/inventory/batches/{batch.Id:D}/prices", new
        {
            expectedVersion = held.Version, held.BatchNumber, held.Mrp, held.SalesPrice, held.TaxRatePercent, held.TaxIncluded,
            expiryDate = DateOnly.FromDateTime(fixture.Hosts.Fixture.Clock.Now.UtcDateTime).AddDays(30),
            reason = "Audited original supplier date",
        });
        Assert.True(corrected.StatusCode == HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        return (batch.Id, root.Id);
    }

    private static async Task<Guid> SellFixtureGoodsAsync(Merchant shop, InventoryBatch batch)
    {
        using var sold = await shop.Client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = Guid.NewGuid().ToString("N"), customerName = "Walk-in customer",
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 24, dueAmount = 0,
            items = new[] { new { batch.Item.Barcode, batch.BatchNumber, itemName = batch.Item.Name, quantity = 2,
                batch.CostPrice, batch.SalesPrice, batch.Mrp, batch.TaxRatePercent,
                isPriceIncludingTax = false, inventoryBatchId = batch.Id } },
        });
        Assert.True(sold.StatusCode == HttpStatusCode.Created, await sold.Content.ReadAsStringAsync());
        return (await sold.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid();
    }

    private static Task<HttpResponseMessage> PostDispositionAsync(Merchant shop, InventoryBatch batch, Guid root, string action) =>
        shop.Client.PostAsJsonAsync($"/api/inventory/batches/{batch.Id:D}/dispositions", new
        {
            operationKey = Guid.NewGuid().ToString("N"), expectedVersion = batch.Version, quarantineChangeId = root,
            action, quantity = 1, reason = "Reviewed quarantine",
        });
}
