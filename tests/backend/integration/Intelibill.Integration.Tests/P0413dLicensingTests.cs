using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Fact]
    public async Task Expired_correction_requires_a_receipt_effective_by_its_posting_instant()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        await ExpireAsync(client, source);
        var version = await SetReceiptInstantAsync(source.Receipt, _clock.Now.AddSeconds(1));
        var before = await FactsAsync(source);
        _output.WriteLine("Frozen posting instant {0:O}, receipt effective one second later.", _clock.Now);

        foreach (var key in new[] { NewKey(), null })
        {
            await AssertProblemAsync(await CorrectAsync(client, source.Receipt, version, 1100, key),
                HttpStatusCode.Conflict, "PurchaseReceipt.SourceIneligible");
            Assert.Equal(before, await FactsAsync(source));
        }

        version = await SetReceiptInstantAsync(source.Receipt, _clock.Now);
        using var response = await CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        var result = await ReadSuccessAsync(response);
        Assert.Equal(100m, result.GetProperty("delta").GetDecimal());
        _output.WriteLine("Future receipt: 409 for keyed and unkeyed requests, unchanged {0}. Equal instant: 200, delta 100.", before);
    }

    private async Task<Source> SeedAsync(HttpClient client)
    {
        var shop = await CreateShopAsync(client);
        var supplier = (await PostAsync(client, "/api/suppliers", new
        {
            name = "Source supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
            pin = "560001", isActive = true, isPreferred = false,
        })).GetProperty("supplierId").GetGuid();
        var barcode = $"RC-{Guid.NewGuid():N}";
        const string lot = "LOT";
        var batch = (await PostAsync(client, "/api/inventory/inbound", new
        {
            itemName = "Source goods", barcode, uom = "pcs", batchNumber = lot, quantity = 10,
            totalPurchaseCost = 1000, mrp = 150, salesPrice = 120, supplierId = supplier,
        })).GetProperty("inventoryBatchId").GetGuid();
        await PostAsync(client, "/api/sales", new
        {
            idempotencyKey = NewKey(), paymentMethod = (int)PaymentMethod.Cash, paidAmount = 480, dueAmount = 0,
            items = new[] { new { barcode, batchNumber = lot, itemName = "Source goods", quantity = 4,
                costPrice = 100, salesPrice = 120, mrp = 150, taxRatePercent = 0, isPriceIncludingTax = false,
                inventoryBatchId = batch } },
        });
        await using var db = Open(Migrator);
        var receipt = await db.PurchaseReceiptValues.SingleAsync(x => x.InventoryBatchId == batch, Ct);
        return new(shop, batch, receipt.Id, receipt.ItemId, supplier, await BindAsync(shop));
    }

    private async Task ExpireAsync(HttpClient client, Source source)
    {
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        _clock.Now = end;
        await AssertProblemAsync(await client.GetAsync($"/api/licensing/shops/{source.Shop}/trading-authority", Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
    }

    private async Task<uint> SetReceiptInstantAsync(Guid receipt, DateTimeOffset instant)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE public.purchase_receipt_values SET received_at = {instant} WHERE id = {receipt}
            """, Ct));
        return await ReceiptVersionAsync(receipt);
    }

    private async Task<uint> ReceiptVersionAsync(Guid receipt)
    {
        await using var db = Open(Migrator);
        return await db.PurchaseReceiptValues.Where(x => x.Id == receipt).Select(x => x.Version).SingleAsync(Ct);
    }

    private async Task<SourceFacts> FactsAsync(Source source)
    {
        await using var db = Open(Migrator);
        var receipt = await db.PurchaseReceiptValues.SingleAsync(x => x.Id == source.Receipt, Ct);
        var batch = await db.InventoryBatches.SingleAsync(x => x.Id == source.Batch, Ct);
        return new(receipt.OriginalTotalCost, receipt.EffectiveTotalCost, batch.Quantity, batch.CostPrice,
            await db.Inventory.Where(x => x.ShopId == source.Shop && x.ItemId == source.Item).Select(x => x.Quantity).SingleAsync(Ct),
            await db.StockCounts.CountAsync(x => x.ShopId == source.Shop, Ct),
            await db.InventoryAdjustments.CountAsync(x => x.ShopId == source.Shop, Ct),
            await db.PaymentAllocations.Where(x => x.ShopId == source.Shop).SumAsync(x => x.Amount, Ct),
            await db.PurchaseReceiptCostAdjustments.CountAsync(x => x.ShopId == source.Shop, Ct),
            await db.SupplierLedgerEntries.CountAsync(x => x.ShopId == source.Shop, Ct),
            await db.StockTransactions.CountAsync(x => x.ShopId == source.Shop, Ct),
            await db.OperationReceipts.CountAsync(x => x.ShopId == source.Shop, Ct));
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static string CorrectionRoute(Guid receipt) => $"/api/inventory/receipts/{receipt}/cost-corrections";

    private static Task<HttpResponseMessage> CorrectAsync(HttpClient client, Guid receipt, uint version, decimal revised, string? key) =>
        client.PostAsJsonAsync(CorrectionRoute(receipt),
            new { expectedVersion = version, revisedTotalCost = revised, reason = "Supplier cost corrected", operationKey = key }, Ct);

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request, Ct);
        return await ReadSuccessAsync(response);
    }

    private sealed record Source(Guid Shop, Guid Batch, Guid Receipt, Guid Item, Guid Supplier, Binding Binding);
    private sealed record SourceFacts(decimal? Original, decimal? Effective, decimal Quantity, decimal Cost,
        decimal Aggregate, int Counts, int InventoryAdjustments, decimal Allocated, int Adjustments, int Ledger, int Movements, int Operations);
}
