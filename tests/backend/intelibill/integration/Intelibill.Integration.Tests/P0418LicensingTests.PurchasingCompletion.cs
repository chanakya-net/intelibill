using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData("purchase-draft-delete")]
    [InlineData("purchase-order-cancel")]
    [InlineData("purchase-order-close")]
    [InlineData("receipt-reassign")]
    public async Task Existing_purchase_completion_waits_to_write_after_expiry(string action)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var supplier = await AddSupplierAsync(client);
        var batch = await SeedGoodsAsync(client);
        (Guid id, Func<Task<HttpResponseMessage>> write) = action == "receipt-reassign"
            ? (batch, () => client.PostAsJsonAsync($"/api/inventory/batches/{batch:D}/reassign-supplier",
                new { newSupplierId = supplier }, Ct))
            : await PreparePurchaseOrderAsync(client, action, supplier, batch);
        _clock.Now = end;
        var before = await PurchaseFactsAsync(action, id);

        await using var holder = await HoldFenceAsync(binding);
        var request = write();
        var waited = await WaitsBehindAsync(request, Pid(holder));
        var whileHeld = await PurchaseFactsAsync(action, id);
        await holder.Database.RollbackTransactionAsync(Ct);
        using var result = await request;
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync(Ct));
        Assert.True(waited);
        Assert.Equal(before, whileHeld);
        Assert.NotEqual(before, await PurchaseFactsAsync(action, id));
    }

    private static async Task<Guid> AddSupplierAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/suppliers", new
        {
            name = "Purchase supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
            pin = "560001", isActive = true,
        }, Ct);
        return (await ReadSuccessAsync(response)).GetProperty("supplierId").GetGuid();
    }

    private async Task<(Guid, Func<Task<HttpResponseMessage>>)> PreparePurchaseOrderAsync(
        HttpClient client, string action, Guid supplier, Guid batch)
    {
        await using var db = Open(Migrator);
        var item = await db.InventoryBatches.Where(row => row.Id == batch)
            .Select(row => new { row.ItemId, row.Item.Barcode }).SingleAsync(Ct);
        using var created = await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = supplier, notes = "Existing order",
            lines = new[] { new { item.ItemId, description = "Existing item", expectedQuantity = 10, unitCost = 1m } },
        }, Ct);
        var draft = await ReadSuccessAsync(created);
        var id = draft.GetProperty("purchaseOrderId").GetGuid();
        if (action == "purchase-draft-delete")
            return (id, () => client.DeleteAsync($"/api/purchase-orders/{id:D}", Ct));

        using var placed = await client.PostAsync($"/api/purchase-orders/{id:D}/place", null, Ct);
        await ReadSuccessAsync(placed);
        if (action == "purchase-order-cancel")
            return (id, () => client.PostAsJsonAsync($"/api/purchase-orders/{id:D}/cancel", new { reason = "Order withdrawn" }, Ct));

        using var receipt = await client.PostAsJsonAsync($"/api/purchase-orders/{id:D}/receipts", new
        {
            operationId = Guid.NewGuid(), receivedAt = _clock.Now,
            lines = new[] { new
            {
                purchaseOrderLineId = draft.GetProperty("lines")[0].GetProperty("lineId").GetGuid(),
                batchNumber = "PARTIAL", item.Barcode, quantity = 1m, totalPurchaseCost = 1m, mrp = 2m, salesPrice = 2m,
                taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
            } },
        }, Ct);
        await ReadSuccessAsync(receipt);
        return (id, () => client.PostAsJsonAsync($"/api/purchase-orders/{id:D}/close",
            new { reason = "Remaining goods declined" }, Ct));
    }

    private async Task<string?> PurchaseFactsAsync(string action, Guid id)
    {
        await using var db = Open(Migrator);
        return action == "receipt-reassign"
            ? await db.Database.SqlQuery<string>($"""SELECT to_jsonb(t)::text AS "Value" FROM inventory_batches t WHERE id = {id}""")
                .SingleOrDefaultAsync(Ct)
            : await db.Database.SqlQuery<string>($"""SELECT to_jsonb(t)::text AS "Value" FROM purchase_orders t WHERE id = {id}""")
                .SingleOrDefaultAsync(Ct);
    }
}
