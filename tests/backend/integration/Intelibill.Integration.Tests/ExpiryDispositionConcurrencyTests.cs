using Intelibill.Domain.Enums;
using System.Net;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryDispositionConcurrencyTests(PostgreSqlTestFixture fixture) : ExpiryDispositionFixture(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDisposals_ConserveRootAndPhysicalStock(bool sameKey)
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var key = Guid.NewGuid().ToString();
        var responses = await Task.WhenAll(DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: key),
            DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: sameKey ? key : Guid.NewGuid().ToString()));
        try
        {
            Assert.Equal(sameKey ? 2 : 1, responses.Count(x => x.IsSuccessStatusCode));
            Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            var batch = await BatchAsync(sale); Assert.Equal((5m,0m),(batch.Quantity,batch.QuarantinedQuantity));
            var changes = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
            Assert.Equal(2, changes.Count); Assert.Equal(0, changes.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity));
            Assert.Equal(1, await QueryAsync(db => db.InventoryAdjustments.CountAsync(x => x.InventoryBatchId == batch.Id)));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ConcurrentVoidAndDisposal_OneConsumesExactSource()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var responses = await Task.WhenAll(DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version),
            SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{held.Return}/void", new { reason = "Wrong return" }));
        try
        {
            Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            var batch = await BatchAsync(sale); Assert.Equal((5m,0m),(batch.Quantity,batch.QuarantinedQuantity));
            var changes = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
            Assert.Equal(2, changes.Count); Assert.Equal(held.Root, changes.Single(x => x.RootChangeId.HasValue).RootChangeId);
            Assert.Equal(0, changes.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Fact]
    public async Task ConcurrentSaleAndDisposal_NeverConsumeHeldUnitsOrLosePhysicalMovement()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        var b = held.Batch;
        var sell = SendAsync(shop, HttpMethod.Post, "/api/sales", new { idempotencyKey = Guid.NewGuid().ToString(), paymentMethod = 1, paidAmount = 590m, dueAmount = 0m,
            items = new[] { new { barcode = b.Item.Barcode, batchNumber = b.BatchNumber, itemName = b.Item.Name, quantity = 5m, costPrice = 80m, salesPrice = 100m,
                mrp = 120m, taxRatePercent = 18m, isPriceIncludingTax = false, inventoryBatchId = b.Id } } });
        var responses = await Task.WhenAll(sell, DisposeAsync(shop, b.Id, held.Root, b.Version));
        try
        {
            Assert.Contains(responses, x => x.IsSuccessStatusCode);
            Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict }));
            var current = await BatchAsync(sale);
            var roots = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == b.Id).ToListAsync());
            var heldBalance = roots.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity);
            Assert.Equal(heldBalance, current.QuarantinedQuantity); Assert.True(current.Quantity >= heldBalance);
            var saleCommitted = responses[0].IsSuccessStatusCode; var disposed = responses[1].IsSuccessStatusCode;
            Assert.Equal(7m - (saleCommitted ? 5m : 0m) - (disposed ? 2m : 0m), current.Quantity);
            Assert.Equal(disposed ? 0m : 2m, current.QuarantinedQuantity);
            Assert.Equal(current.Quantity, await QueryAsync(db => db.Inventory.Where(x => x.ItemId == b.ItemId).Select(x => x.Quantity).SingleAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanRestorationOnPartiallyHeldBatch_PreservesRootsDuringCountOrDisposal(bool countInstead)
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop,quantity:5m); var held = await RecoverAsync(shop,sale);
        await CorrectDateAsync(shop,held.Batch); var b = await BatchAsync(sale);
        var returning = SendAsync(shop,HttpMethod.Post,$"/api/sales/{sale.SaleId}/returns",new { payoutDestination = (int)ReturnPayoutDestination.Refund,
            items = new[] { Line(sale.LineId,quantity:1m) } });
        var competing = countInstead
            ? SendAsync(shop,HttpMethod.Post,$"/api/inventory/batches/{b.Id}/count",new { expectedVersion = b.Version,countedQuantity = 5m,reason = "Count unheld goods",operationKey = Guid.NewGuid().ToString() })
            : DisposeAsync(shop,b.Id,held.Root,b.Version);
        var responses = await Task.WhenAll(returning,competing);
        try
        {
            Assert.Contains(responses,x => x.IsSuccessStatusCode);
            foreach (var response in responses) Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict,await response.Content.ReadAsStringAsync());
            var current = await BatchAsync(sale); var returned = responses[0].IsSuccessStatusCode; var competed = responses[1].IsSuccessStatusCode;
            Assert.Equal(7m + (returned?1m:0m) - (competed?2m:0m),current.Quantity);
            Assert.Equal(!countInstead && competed?0m:2m,current.QuarantinedQuantity);
            var entries = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == b.Id).ToListAsync());
            Assert.Equal(current.QuarantinedQuantity,entries.Sum(x => x.RootChangeId is null?x.Quantity:-x.Quantity));
            Assert.Equal(current.Quantity,await QueryAsync(db => db.Inventory.Where(x => x.ItemId == b.ItemId).Select(x => x.Quantity).SingleAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
