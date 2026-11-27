using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

/// <summary>M12: existing lease recovery remains available after online trading ends.</summary>
public sealed partial class P0418LicensingTests
{
    [Fact]
    public async Task Previously_issued_offline_invoice_replays_after_expiry()
    {
        using var host = CreateHost(runtimeIdentityOutsideRequests: false);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var batch = await SeedGoodsAsync(client);
        await LegacyOfflineExpiryHistory.SeedAsync(host, client.DefaultRequestHeaders.Authorization!.Parameter!);
        const string device = "p0418-device";
        var lease = await ReadSuccessAsync(await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve",
            new { deviceId = device, blockSize = 25 }, Ct));
        var invoice = lease.GetProperty("prefix").GetString() +
            lease.GetProperty("rangeStart").GetInt32().ToString("D6", CultureInfo.InvariantCulture);
        var barcode = await BatchBarcodeAsync(batch);
        _clock.Now = end.AddMinutes(5);

        var payload = OfflinePayload(device, invoice, barcode, batch, soldAt: end.AddMicroseconds(-1), OfflineIssuer.Of(client.DefaultRequestHeaders.Authorization!.Parameter!));
        using var sync = await client.PostAsJsonAsync("/api/sales/offline-sync", payload, Ct);
        var status = (await ReadSuccessAsync(sync)).GetProperty("results").EnumerateArray().Single()
            .GetProperty("status").GetString();
        await using var db = Open(Migrator);
        var posted = await db.Sales.CountAsync(x => x.ShopId == binding.Shop, Ct);
        var held = await db.OfflineSubmissions.CountAsync(x => x.ShopId == binding.Shop && x.SaleId == null, Ct);
        _output.WriteLine("Offline invoice {0} sold before the trading end, synced after it: status {1}; PostgreSQL sales={2}, " +
            "held envelopes={3}", invoice, status, posted, held);

        Assert.Equal("created", status);
        Assert.Equal(1, posted);
        Assert.Equal(0, held);

        var hash = await db.OfflineSubmissions.Where(row => row.ShopId == binding.Shop)
            .Select(row => row.PayloadHash).SingleAsync(Ct);
        using var replay = await client.PostAsJsonAsync("/api/sales/offline-sync", payload, Ct);
        var duplicate = (await ReadSuccessAsync(replay)).GetProperty("results").EnumerateArray().Single();
        Assert.Equal("duplicate", duplicate.GetProperty("status").GetString());

        await ApplyAsync(binding, end.AddHours(1), revision: 2, restriction: "Security");
        var denied = await ObserveAsync(await client.PostAsJsonAsync("/api/sales/offline-sync", payload, Ct));
        Assert.Equal(new Observed(HttpStatusCode.Forbidden, "Licence.SecuritySuspended"), denied);
        Assert.Equal(1, await db.Sales.CountAsync(row => row.ShopId == binding.Shop, Ct));
        Assert.Equal(hash, await db.OfflineSubmissions.Where(row => row.ShopId == binding.Shop)
            .Select(row => row.PayloadHash).SingleAsync(Ct));
    }

    private async Task<string> BatchBarcodeAsync(Guid batch)
    {
        await using var db = Open(Migrator);
        return await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Item.Barcode).SingleAsync(Ct);
    }

    private static object OfflinePayload(string device, string invoice, string barcode, Guid batch, DateTimeOffset soldAt, Guid issuer) => new
    {
        deviceId = device,
        sales = new[] { new
        {
            clientSaleId = $"offline-{Guid.NewGuid():N}", invoiceNumber = invoice, soldAt,
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 2m, dueAmount = 0m,
            subtotalBeforeDiscount = 2m, totalBeforeDiscount = 2m, totalDiscountAmount = 0m,
            totalTaxAmount = 0m, totalAmount = 2m, saleDiscountOverrideType = 0, saleDiscountOverrideValue = 0m,
            calculationVersion = 1, issuedByUserId = issuer,
            items = new[] { new
            {
                barcode, batchNumber = "LOT", itemName = "Fixture stock", quantity = 1m, costPrice = 1m, salesPrice = 2m,
                mrp = 2m, taxRatePercent = 0m, isPriceIncludingTax = false, inventoryBatchId = batch,
                preTaxAmountBeforeDiscount = 2m, itemDiscountAmount = 0m, saleDiscountAmount = 0m, taxableAmount = 2m,
                taxAmount = 0m, totalAmount = 2m, itemDiscountOverrideType = 0, itemDiscountOverrideValue = 0m,
            } },
        } },
    };
}
