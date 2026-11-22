using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Source paths that carry a blocked-when-restricted classification but have no slice evidence.
/// The assertion is the parent matrix expectation; the observed response is written first so a
/// failure records the exact divergence.
/// </summary>
public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData("discount-create")]
    [InlineData("discount-replace")]
    [InlineData("item-reactivate")]
    [InlineData("barcode-generate")]
    [InlineData("batch-prices")]
    [InlineData("legacy-batch-prices")]
    public async Task Sources_operational_capacity_writes_refuse_after_expiry(string route)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var operatingWrite = await PrepareCapacityWriteAsync(client, route);
        var write = await PrepareCapacityWriteAsync(client, route);
        _clock.Now = end.AddMicroseconds(-1);
        using var operating = await operatingWrite();
        Assert.True(operating.IsSuccessStatusCode, await operating.Content.ReadAsStringAsync(Ct));

        var before = await CountCapacityRowsAsync(binding.Shop);
        _clock.Now = end;
        var observed = await ObserveAsync(await write());
        var changes = await CountCapacityRowsAsync(binding.Shop);
        _output.WriteLine("{0} on enrolled shop at trading deadline: {1}; PostgreSQL {2}", route, observed, changes);

        Assert.Equal(new Observed(HttpStatusCode.Forbidden, "Licence.Expired"), observed);
        Assert.Equal(before, changes);
    }

    [Theory]
    [InlineData("batch-void")]
    [InlineData("adjustment-void")]
    [InlineData("posting-period-close")]
    [InlineData("discount-create")]
    [InlineData("customer-create")]
    [InlineData("supplier-delete")]
    public async Task Sources_corrections_decide_inside_the_licence_fence(string route)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var write = await PrepareCorrectionAsync(client, route);

        if (route is "batch-void" or "adjustment-void" or "posting-period-close" or "supplier-delete")
            _clock.Now = end;

        var before = await CountSourceFactsAsync(binding.Shop);
        await using var holder = await HoldFenceAsync(binding);
        var request = write();
        var waited = await WaitsBehindAsync(request, Pid(holder));
        await Task.Delay(100, Ct);
        var whileHeld = await CountSourceFactsAsync(binding.Shop);
        await holder.Database.RollbackTransactionAsync(Ct);
        var observed = await ObserveAsync(await request);
        var after = await CountSourceFactsAsync(binding.Shop);
        _output.WriteLine("{0} with the shop licence fence held exclusively: waited={1}; PostgreSQL facts before [{2}], " +
            "while held [{3}], after release [{4}]; response {5}", route, waited, before, whileHeld, after, observed);

        // The response guard also waits for the fence, so a wait alone does not show a source decision.
        // A source that decides inside the fence cannot have committed its facts while the fence is held.
        Assert.True(waited);
        Assert.InRange((int)observed.Status, 200, 299);
        Assert.Equal(before, whileHeld);
        Assert.NotEqual(before, after);
    }

    private async Task<string> CountSourceFactsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var movements = await db.StockTransactions.CountAsync(x => x.ShopId == shop, Ct);
        var periodEvents = await db.PostingPeriodEvents.CountAsync(x => x.ShopId == shop, Ct);
        var discounts = await db.DiscountRules.CountAsync(x => x.ShopId == shop, Ct);
        var receipts = await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct);
        var customers = await db.Customers.CountAsync(x => x.ShopId == shop, Ct);
        var suppliers = await db.Suppliers.CountAsync(x => x.ShopId == shop, Ct);
        return $"suppliers={suppliers}, stock_transactions={movements}, posting_period_events={periodEvents}, discount_rules={discounts}, " +
            $"operation_receipts={receipts}, customers={customers}";
    }

    private async Task<Func<Task<HttpResponseMessage>>> PrepareCapacityWriteAsync(HttpClient client, string route)
    {
        switch (route)
        {
            case "discount-create":
                return () => client.PostAsJsonAsync("/api/discounts", DiscountInput("After expiry"), Ct);
            case "discount-replace":
            {
                var id = (await ReadSuccessAsync(await client.PostAsJsonAsync("/api/discounts", DiscountInput("Before"), Ct)))
                    .GetProperty("id").GetGuid();
                return () => client.PutAsJsonAsync($"/api/discounts/{id}", DiscountInput("Replaced after expiry"), Ct);
            }
            case "item-reactivate":
            {
                var barcode = Guid.NewGuid().ToString("N");
                var item = await ReadSuccessAsync(await client.PostAsJsonAsync("/api/items", new
                {
                    name = "Dormant " + barcode, barcode, uom = "pcs", isActive = false, defaultTaxRatePercent = 0m,
                }, Ct));
                var id = item.GetProperty("id").GetGuid();
                return () => client.PatchAsJsonAsync($"/api/items/{id}", new
                {
                    name = "Dormant " + barcode, barcode, uom = "pcs", defaultTaxRatePercent = 0m, isActive = true,
                }, Ct);
            }
            case "barcode-generate":
                return () => client.PostAsync("/api/items/barcodes/generate", null, Ct);
            case "batch-prices":
            {
                var batch = await SeedGoodsAsync(client, $"Fixture stock {Guid.NewGuid():N}");
                var version = await BatchVersionAsync(batch);
                return () => client.PutAsJsonAsync($"/api/inventory/batches/{batch}/prices", new
                {
                    expectedVersion = version, mrp = 5m, salesPrice = 5m, taxRatePercent = 0m, taxIncluded = false,
                    reason = "Price change after expiry",
                }, Ct);
            }
            case "legacy-batch-prices":
            {
                var batch = await SeedGoodsAsync(client, $"Fixture stock {Guid.NewGuid():N}");
                return () => client.PutAsJsonAsync($"/api/inventory/batches/{batch}", new
                {
                    quantity = 10m, costPrice = 1m, mrp = 5m, salesPrice = 5m, taxRatePercent = 0m,
                    taxIncluded = false, notes = "New retail terms",
                }, Ct);
            }
            case "supplier-delete":
            {
                var supplier = await ReadSuccessAsync(await client.PostAsJsonAsync("/api/suppliers", new
                {
                    name = "Fixture supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
                    pin = "560001", isActive = true,
                }, Ct));
                var id = supplier.GetProperty("supplierId").GetGuid();
                return () => client.DeleteAsync($"/api/suppliers/{id}", Ct);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, null);
        }
    }

    private async Task<Func<Task<HttpResponseMessage>>> PrepareCorrectionAsync(HttpClient client, string route)
    {
        switch (route)
        {
            case "batch-void":
            {
                var batch = await SeedGoodsAsync(client);
                return () => client.PostAsync($"/api/inventory/batches/{batch}/void", null, Ct);
            }
            case "adjustment-void":
            {
                var batch = await SeedGoodsAsync(client);
                var adjustment = (await ReadSuccessAsync(await client.PostAsJsonAsync($"/api/inventory/batches/{batch}/adjust",
                    new { direction = 2, reason = 1, quantity = 2m, notes = "Damaged in store" }, Ct)))
                    .GetProperty("adjustmentId").GetGuid();
                return () => client.PostAsJsonAsync($"/api/inventory/adjustments/{adjustment}/void",
                    new { reason = "Write-off entered in error" }, Ct);
            }
            case "posting-period-close":
            {
                var month = _clock.Now.ToOffset(TimeSpan.FromHours(5.5)).AddMonths(-1);
                return () => client.PostAsJsonAsync($"/api/posting-periods/{month:yyyy-MM}/close",
                    new { reason = "Prior books complete", operationKey = $"close-{Guid.NewGuid():N}" }, Ct);
            }
            case "discount-create":
                return () => client.PostAsJsonAsync("/api/discounts", DiscountInput("Fence control"), Ct);
            case "supplier-delete":
                return await PrepareCapacityWriteAsync(client, route);
            case "customer-create":
                return () => client.PostAsJsonAsync("/api/customers", new
                {
                    name = "Fenced customer", phoneNumber = "+919111111111", isActive = true, creditLimit = 0m,
                }, Ct);
            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, null);
        }
    }

    private static object DiscountInput(string name) => new
    {
        ruleType = 2, name, percentage = 5m, belowCostConfirmed = false,
    };

    private static async Task<Guid> SeedGoodsAsync(HttpClient client, string itemName = "Fixture stock")
    {
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName, barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "LOT",
            quantity = 10m, totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m,
            taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        return (await ReadSuccessAsync(response)).GetProperty("inventoryBatchId").GetGuid();
    }

    private async Task<uint> BatchVersionAsync(Guid batch)
    {
        await using var db = Open(Migrator);
        return await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Version).SingleAsync(Ct);
    }

    private async Task<string> CountCapacityRowsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var discounts = await db.Database.SqlQuery<int>(
            $"""SELECT count(*)::int AS "Value" FROM discount_rules WHERE shop_id = {shop}""").SingleAsync(Ct);
        var activeItems = await db.Items.CountAsync(x => x.ShopId == shop && x.IsActive, Ct);
        var suppliers = await db.Suppliers.CountAsync(x => x.ShopId == shop, Ct);
        var prices = await db.InventoryBatches.Where(x => x.ShopId == shop).OrderBy(x => x.Id).Select(x => x.SalesPrice).ToArrayAsync(Ct);
        var priceRevisions = await db.BatchPriceRevisions.CountAsync(x => x.ShopId == shop, Ct);
        var sequence = await db.Database.SqlQuery<int>(
            $"""SELECT next_number AS "Value" FROM item_barcode_sequences WHERE shop_id = {shop}""").SingleOrDefaultAsync(Ct);
        return $"discount_rules={discounts}, active items={activeItems}, suppliers={suppliers}, barcode next={sequence}, " +
            $"price revisions={priceRevisions}, batch sales prices=[{string.Join(',', prices)}]";
    }

}
