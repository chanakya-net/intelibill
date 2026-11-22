using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData("batch-void")]
    [InlineData("purchase-order-cancel")]
    [InlineData("supplier-delete")]
    [InlineData("discount-disable")]
    public async Task Existing_source_at_or_after_restriction_cutoff_is_refused(string action)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var cutoff = _clock.Now;
        var end = cutoff.AddMinutes(10);
        var binding = await OpenEnrolledShopAsync(client, end);
        var write = await SeedCutoffSourceAsync(client, action);
        var sourceAt = await CutoffSourceTimeAsync(binding.Shop, action);
        Assert.True(sourceAt >= cutoff, $"Source {sourceAt:O}, cutoff {cutoff:O}");
        var before = await CutoffFactsAsync(binding.Shop);

        _clock.Now = cutoff.AddMinutes(1);
        await ApplyAsync(binding, end, revision: 2, restriction: "Trading", suspensionEffectiveAt: cutoff);
        var observed = await ObserveAsync(await write());
        var after = await CutoffFactsAsync(binding.Shop);
        _output.WriteLine("{0}: source {1:O}, restriction cutoff {2:O}, delivered {3:O}, response {4}, facts unchanged {5}",
            action, sourceAt, cutoff, _clock.Now, observed, before == after);

        Assert.Equal(new Observed(HttpStatusCode.Forbidden, "Licence.TradingRestricted"), observed);
        Assert.Equal(before, after);
    }

    private async Task<Func<Task<HttpResponseMessage>>> SeedCutoffSourceAsync(HttpClient client, string action)
    {
        if (action == "discount-disable")
            return (await PrepareDefinitionAsync(client, action)).Item2;

        if (action == "purchase-order-cancel")
        {
            var supplier = await AddSupplierAsync(client);
            var batch = await SeedGoodsAsync(client);
            return (await PreparePurchaseOrderAsync(client, action, supplier, batch)).Item2;
        }

        return await PrepareCorrectionAsync(client, action);
    }

    private async Task<DateTimeOffset> CutoffSourceTimeAsync(Guid shop, string action)
    {
        await using var db = Open(Migrator);
        return action switch
        {
            "batch-void" => await db.PurchaseReceiptValues.Where(x => x.ShopId == shop).Select(x => x.ReceivedAt).SingleAsync(Ct),
            "purchase-order-cancel" => await db.PurchaseOrders.Where(x => x.ShopId == shop).Select(x => x.CreatedAt).SingleAsync(Ct),
            "supplier-delete" => await db.Suppliers.Where(x => x.ShopId == shop && !x.IsSystem).Select(x => x.CreatedAt).SingleAsync(Ct),
            _ => await db.DiscountRules.Where(x => x.ShopId == shop).Select(x => x.CreatedAt).SingleAsync(Ct),
        };
    }

    private async Task<string> CutoffFactsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
                'batches', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM inventory_batches t WHERE shop_id = {shop}),
                'receipts', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM purchase_receipt_values t WHERE shop_id = {shop}),
                'movements', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM stock_transactions t WHERE shop_id = {shop}),
                'orders', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM purchase_orders t WHERE shop_id = {shop}),
                'suppliers', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM suppliers t WHERE shop_id = {shop}),
                'discounts', (SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM discount_rules t WHERE shop_id = {shop})
            )::text AS "Value"
            """).SingleAsync(Ct);
    }
}
