using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408cLicensingTests
{
    [Fact]
    public async Task Receiving_inputs_cannot_create_supplier_capacity()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var foreign = CreateClient(host);
        var binding = await OpenTradingAsync(owner);
        var foreignShop = await CreateShopAsync(foreign);
        var localSupplier = await AddSupplierIdAsync(owner, "Local supply");
        var foreignSupplier = await AddSupplierIdAsync(foreign, "Foreign supply");

        var localBefore = await SupplierRowsAsync(binding.Shop);
        var foreignBefore = await SupplierRowsAsync(foreignShop);
        var system = Assert.Single(localBefore, x => x.IsSystem).Id;
        var barcode = Guid.NewGuid().ToString("N");
        var inbound = InboundInput("System control", null, existingItem: ("System control", barcode));
        using var posted = await owner.PostAsJsonAsync("/api/inventory/inbound", inbound, Ct);
        await AssertBatchSupplierAsync((await ReadSuccessAsync(posted)).GetProperty("inventoryBatchId").GetGuid(), system);
        await AssertProblemAsync(await owner.PostAsJsonAsync("/api/inventory/inbound", InboundInput("Foreign", foreignSupplier), Ct),
            HttpStatusCode.NotFound, "Supplier.SupplierNotFound");
        await AssertBulkSupplierResolutionAsync(owner, foreignSupplier, system);
        await AssertPurchaseOrderSupplierResolutionAsync(owner, localSupplier, foreignSupplier);

        await RestrictAsync(binding, "PendingActivation");
        await ProbeRestrictedReceivingAsync(owner, foreignSupplier, barcode);
        _clock.Now = _clock.Now.AddDays(1);
        await ApplyAsync(binding, _clock.Now.AddDays(1), 3, state: "Expired");
        await ProbeRestrictedReceivingAsync(owner, foreignSupplier, barcode);

        Assert.Equal(localBefore, await SupplierRowsAsync(binding.Shop));
        Assert.Equal(foreignBefore, await SupplierRowsAsync(foreignShop));
        _output.WriteLine("Single/bulk intake and PO receiving retained both shops' original supplier ID sets.");
    }

    private async Task AssertBulkSupplierResolutionAsync(HttpClient client, Guid foreignSupplier, Guid system)
    {
        using var posted = await client.PostAsJsonAsync("/api/inventory/inbound/batch", new
        {
            items = new[] { InboundInput("Bulk system", null), InboundInput("Bulk foreign", foreignSupplier) },
        }, Ct);
        Assert.Equal(HttpStatusCode.MultiStatus, posted.StatusCode);
        var result = await ReadSuccessAsync(posted);
        var succeeded = Assert.Single(result.GetProperty("succeeded").EnumerateArray());
        Assert.Equal("Bulk system", succeeded.GetProperty("clientRowId").GetString());
        await AssertBatchSupplierAsync(succeeded.GetProperty("result").GetProperty("inventoryBatchId").GetGuid(), system);
        var failed = Assert.Single(result.GetProperty("failed").EnumerateArray());
        Assert.Equal("Bulk foreign", failed.GetProperty("clientRowId").GetString());
        Assert.Equal("Supplier.SupplierNotFound", failed.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    private async Task AssertPurchaseOrderSupplierResolutionAsync(HttpClient client, Guid localSupplier, Guid foreignSupplier)
    {
        var barcode = Guid.NewGuid().ToString("N");
        using var item = await client.PostAsJsonAsync("/api/items", new { name = "PO item", barcode, uom = "pcs", isActive = true }, Ct);
        var itemId = (await ReadSuccessAsync(item)).GetProperty("id").GetGuid();
        var textDraft = await CreateDraftAsync(client, itemId, null);
        var draftPath = $"/api/purchase-orders/{textDraft.GetProperty("purchaseOrderId").GetGuid()}";
        await AssertProblemAsync(await client.PostAsync(draftPath + "/place", null, Ct),
            HttpStatusCode.BadRequest, "PurchaseOrder.SupplierRequired");

        var identifiedDraft = await CreateDraftAsync(client, itemId, localSupplier);
        var path = $"/api/purchase-orders/{identifiedDraft.GetProperty("purchaseOrderId").GetGuid()}";
        using var placed = await client.PostAsync(path + "/place", null, Ct);
        await ReadSuccessAsync(placed);
        var line = identifiedDraft.GetProperty("lines")[0].GetProperty("lineId").GetGuid();
        var batchNumber = $"PO-{Guid.NewGuid():N}";
        using var received = await client.PostAsJsonAsync(path + "/receipts", new
        {
            supplierId = foreignSupplier, supplier = new { name = "Inline receiving supplier" },
            lines = new[] { new
            {
                purchaseOrderLineId = line, barcode, batchNumber, quantity = 1,
                totalPurchaseCost = 10m, mrp = 20m, salesPrice = 15m,
            } },
        }, Ct);
        await ReadSuccessAsync(received);
        await using var db = Open(Migrator);
        Assert.Equal(localSupplier, await db.InventoryBatches.Where(x => x.BatchNumber == batchNumber)
            .Select(x => x.SupplierId).SingleAsync(Ct));
    }

    private static async Task<JsonElement> CreateDraftAsync(HttpClient client, Guid itemId, Guid? supplierId)
    {
        using var created = await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId, supplierName = "Free-text supplier",
            lines = new[] { new { itemId, description = "PO item", expectedQuantity = 1, unitCost = 10m } },
        }, Ct);
        return await ReadSuccessAsync(created);
    }

    private async Task ProbeRestrictedReceivingAsync(HttpClient client, Guid foreignSupplier, string barcode)
    {
        // Intake/receiving restrictions are owned by separate slices. Here assert supplier identity
        // invariants in PostgreSQL regardless of the intake policy's decision.
        var item = ("System control", barcode);
        using var single = await client.PostAsJsonAsync("/api/inventory/inbound",
            InboundInput("Restricted single", null, existingItem: item), Ct);
        using var bulk = await client.PostAsJsonAsync("/api/inventory/inbound/batch", new
        {
            items = new[]
            {
                InboundInput("Restricted bulk", null, existingItem: item),
                InboundInput("Restricted foreign", foreignSupplier, existingItem: item),
            },
        }, Ct);
        _output.WriteLine("Restricted intake probes: single HTTP {0}; bulk HTTP {1}.",
            (int)single.StatusCode, (int)bulk.StatusCode);
    }

    private async Task AssertBatchSupplierAsync(Guid batchId, Guid supplier)
    {
        await using var db = Open(Migrator);
        Assert.Equal(supplier, await db.InventoryBatches.Where(x => x.Id == batchId).Select(x => x.SupplierId).SingleAsync(Ct));
    }

    private async Task<SupplierRow[]> SupplierRowsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return await db.Suppliers.Where(x => x.ShopId == shop).OrderBy(x => x.Id)
            .Select(x => new SupplierRow(x.Id, x.IsSystem, x.IsActive)).ToArrayAsync(Ct);
    }

    private static object InboundInput(string name, Guid? supplierId, decimal totalPurchaseCost = 800m,
        (string Name, string Barcode)? existingItem = null) => new
    {
        clientRowId = name, itemName = existingItem?.Name ?? name, barcode = existingItem?.Barcode ?? Guid.NewGuid().ToString("N"), uom = "pcs",
        batchNumber = $"B-{Guid.NewGuid():N}", quantity = 10m, totalPurchaseCost, mrp = 150m, salesPrice = 120m,
        supplierId, supplierName = "Inline name", supplier = new { name = "Inline supplier", isActive = true },
    };

    private sealed record SupplierRow(Guid Id, bool IsSystem, bool IsActive);
}
