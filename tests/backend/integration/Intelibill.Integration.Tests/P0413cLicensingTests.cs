using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413cLicensingTests
{
    [Theory]
    [InlineData("shipment")]
    [InlineData("credit")]
    [InlineData("receipt")]
    [InlineData("group")]
    public async Task Source_completion_observes_winning_security_projection(string operation)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        var (route, body) = await PrepareAsync(client, source, operation);
        var before = await FactsAsync(source.Shop);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "entitlement_snapshot_inbox", source.Shop);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await delivery.Database.OpenConnectionAsync(Ct);
        var projection = ApplyAsync(source.Binding, end, 2, connection: delivery, securityEffectiveAt: _clock.Now);
        var blocker = await barrier.WaitForArrivalAsync();
        Assert.Equal(((NpgsqlConnection)delivery.Database.GetDbConnection()).ProcessID, blocker);

        var request = client.PostAsJsonAsync(route, body, Ct);
        var waiter = await ObserveWaitAsync(request, blocker, barrier, projection);
        await AssertProblemAsync(await request, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.NotEqual(0, waiter);
        Assert.Equal(before, await FactsAsync(source.Shop));
        _output.WriteLine("{0}: HTTP 403 Licence.SecuritySuspended, unchanged {1}", operation, before);
    }

    private async Task<int> ObserveWaitAsync(Task request, int blocker, TillPostgresBarrier barrier, Task projection)
    {
        try
        {
            await using var observer = Open(Migrator);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!request.IsCompleted)
            {
                var waiter = await observer.Database.SqlQuery<int>($"""
                    SELECT pid AS "Value" FROM pg_stat_activity
                    WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid)) LIMIT 1
                    """).SingleOrDefaultAsync(deadline.Token);
                if (waiter != 0)
                {
                    _output.WriteLine("PostgreSQL pg_blocking_pids: request PID {0} waited on blocker PID {1}.", waiter, blocker);
                    return waiter;
                }
                await Task.Delay(10, deadline.Token);
            }
            _output.WriteLine("Request completed while PostgreSQL blocker PID {0} was still held.", blocker);
            return 0;
        }
        finally
        {
            await barrier.ReleaseAsync();
            await projection;
        }
    }

    private async Task<Source> SeedAsync(HttpClient client)
    {
        var shop = await CreateShopAsync(client);
        var supplier = (await PostAsync(client, "/api/suppliers", new
        {
            name = "Source supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
            pin = "560001", isActive = true, isPreferred = false,
        })).GetProperty("supplierId").GetGuid();
        var barcode = $"SR-{Guid.NewGuid():N}";
        var batch = (await PostAsync(client, "/api/inventory/inbound", Inbound(supplier, barcode)))
            .GetProperty("inventoryBatchId").GetGuid();
        await using var db = Open(Migrator);
        var receipt = await db.PurchaseReceiptValues.SingleAsync(x => x.InventoryBatchId == batch, Ct);
        return new(shop, supplier, receipt.Id, receipt.ItemId, barcode, await BindAsync(shop));
    }

    private static object Inbound(Guid supplier, string barcode) => new
    {
        itemName = "Source goods", barcode, uom = "pcs", batchNumber = $"LOT-{Guid.NewGuid():N}",
        quantity = 10, totalPurchaseCost = 1000, mrp = 150, salesPrice = 120, supplierId = supplier,
    };

    private static async Task<(string Route, object Body)> PrepareAsync(HttpClient client, Source source, string operation)
    {
        if (operation == "group")
        {
            var group = await ReceiveGroupAsync(client, source);
            var lines = await Task.WhenAll(group.Lots.Select(async id => ReversalLine(await DetailAsync(client, id))));
            return ($"/api/purchase-order-receipts/{group.Id}/reversal",
                new { operationKey = NewKey(), reason = "Reverse incorrect delivery", lines });
        }

        var detail = await DetailAsync(client, source.Receipt);
        var versions = ReversalLine(detail);
        if (operation == "receipt")
            return ($"/api/purchase-receipts/{source.Receipt}/reversal", new
            {
                operationKey = NewKey(), reason = "Reverse incorrect receipt",
                versions.ExpectedReceiptVersion, versions.ExpectedBatchVersion,
            });

        var shipment = Shipment(source, detail, NewKey());
        if (operation == "shipment") return ("/api/supplier-returns", shipment);
        var pending = await PostAsync(client, "/api/supplier-returns", shipment);
        return ($"/api/supplier-returns/{pending.GetProperty("returnId").GetGuid()}/credit", Credit(pending, NewKey()));
    }

    private static async Task<Group> ReceiveGroupAsync(HttpClient client, Source source)
    {
        var order = await PostAsync(client, "/api/purchase-orders", new
        {
            supplierId = source.Supplier, lines = new[] { new { itemId = source.Item, description = "Goods", expectedQuantity = 12, unitCost = 100 } },
        });
        var po = order.GetProperty("purchaseOrderId").GetGuid();
        var line = order.GetProperty("lines")[0].GetProperty("lineId").GetGuid();
        await PostAsync(client, $"/api/purchase-orders/{po}/place", new { });
        await PostAsync(client, $"/api/purchase-orders/{po}/receipts", Receiving(source, line, 4, 6));
        var current = await GetAsync(client, $"/api/purchase-orders/{po}");
        var receipt = current.GetProperty("receipts")[0];
        return new(po, line, receipt.GetProperty("receiptId").GetGuid(),
            receipt.GetProperty("lines").EnumerateArray().Select(x => x.GetProperty("receiptValueId").GetGuid()).ToArray());
    }

    private static object Receiving(Source source, Guid line, params int[] quantities) => new
    {
        operationId = Guid.NewGuid(),
        lines = quantities.Select(quantity => new
        {
            clientLineId = Guid.NewGuid(), purchaseOrderLineId = line, source.Barcode,
            batchNumber = $"PO-{Guid.NewGuid():N}", quantity, totalPurchaseCost = quantity * 100,
            mrp = 150, salesPrice = 120, taxRatePercent = 0, taxIncluded = false, purchaseTaxIncluded = false,
        }).ToArray(),
    };

    private static object Shipment(Source source, JsonElement detail, string key, decimal quantity = 3, bool accepted = false) => new
    {
        operationKey = key, supplierId = source.Supplier, reason = "Damaged goods", creditAccepted = accepted,
        lines = new[] { new
        {
            receiptValueId = source.Receipt, expectedReceiptVersion = detail.GetProperty("receiptVersion").GetUInt32(),
            expectedBatchVersion = detail.GetProperty("batchVersion").GetUInt32(), quantity, proposedCreditAmount = 300,
        } },
    };

    private static object Credit(JsonElement pending, string key) => new
    {
        operationKey = key, expectedVersion = pending.GetProperty("version").GetUInt32(), reason = "Supplier agreed",
        lines = new[] { new { returnLineId = pending.GetProperty("lines")[0].GetProperty("returnLineId").GetGuid(), acceptedAmount = 300 } },
    };

    private static ReversalVersions ReversalLine(JsonElement detail) => new(
        detail.GetProperty("receiptValueId").GetGuid(), detail.GetProperty("receiptVersion").GetUInt32(),
        detail.GetProperty("batchVersion").GetUInt32());

    private async Task<SourceFacts> FactsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return new(
            await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct),
            await db.SupplierReturns.CountAsync(x => x.ShopId == shop, Ct),
            await db.SupplierReturnCredits.CountAsync(x => x.ShopId == shop, Ct),
            await db.PurchaseReceiptReversals.CountAsync(x => x.ShopId == shop, Ct),
            await db.StockTransactions.CountAsync(x => x.ShopId == shop, Ct),
            await db.SupplierLedgerEntries.CountAsync(x => x.ShopId == shop, Ct),
            await db.PurchaseReceiptValues.CountAsync(x => x.ShopId == shop, Ct),
            await db.InventoryBatches.Where(x => x.ShopId == shop).SumAsync(x => x.Quantity, Ct));
    }

    private static string NewKey() => Guid.NewGuid().ToString();
    private static Task<JsonElement> DetailAsync(HttpClient client, Guid id) => GetAsync(client, $"/api/purchase-receipts/{id}");
    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        return await ReadSuccessAsync(response);
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, Ct);
        return await ReadSuccessAsync(response);
    }

    private sealed record Source(Guid Shop, Guid Supplier, Guid Receipt, Guid Item, string Barcode, Binding Binding);
    private sealed record Group(Guid Order, Guid Line, Guid Id, Guid[] Lots);
    private sealed record ReversalVersions(Guid ReceiptValueId, uint ExpectedReceiptVersion, uint ExpectedBatchVersion);
    private sealed record SourceFacts(int Operations, int Shipments, int Credits, int Reversals, int Movements, int Ledger, int Receipts, decimal Stock);
}
