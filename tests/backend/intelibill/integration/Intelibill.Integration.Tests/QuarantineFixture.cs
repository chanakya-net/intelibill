using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

/// <summary>Retained recovery facts for read-path fixtures which predate the disposition journal.
/// Represents an original sale followed by a zero-refund physical recovery. The pair's net
/// physical movement is zero; the original fixture's physical quantity and policy stay intact.</summary>
internal static class QuarantineFixture
{
    public static async Task SeedAsync(ApplicationDbContext db, Guid batchId, decimal quantity)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var batch = await db.InventoryBatches.SingleAsync(x => x.Id == batchId);
        var effective = await new Intelibill.Infrastructure.Repositories.ExpiryPolicyRepository(db).GetEffectiveAsync(batch.ShopId,batch.ItemId,null,default);
        if (!effective.HistoryKnown || effective.Policy is null || (!batch.ExpiryDate.HasValue && !effective.Policy.ExpiryRequired))
            throw new InvalidOperationException("A fixture recovery needs retained policy and dated expired or genuinely required undated goods.");
        var date = batch.ExpiryDate?.AddDays(1) ?? BusinessDates.Today;
        var returnedAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var soldAt = returnedAt.AddDays(-2);
        var line = new SaleLineInput(batch.ShopId, SaleLineType.Goods, batch.ItemId, batch.Id, null, "Recovered fixture goods", "FIXTURE",
            quantity, batch.CostPrice, batch.SalesPrice, batch.Mrp, 0, false, false);
        var sale = Sale.Record(batch.ShopId, batch.CreatedBy, Guid.NewGuid().ToString(), "fixture", $"F-{Guid.NewGuid():N}", [line], null, "Fixture buyer", null,
            PaymentMethod.Cash, quantity * batch.SalesPrice, 0, soldAt, BusinessDay.InDefaultZone(soldAt)).Value;
        var recovered = SaleReturn.Record(batch.ShopId, sale.Id, $"FR-{Guid.NewGuid():N}", returnedAt, BusinessDay.InDefaultZone(returnedAt), batch.CreatedBy,
            "Previously recovered stock, no financial refund", 0, 0, 0, null, 0, 0, null, null,
            [new(batch.ShopId, sale.Items.Single().Id, quantity, SaleReturnCondition.Restockable, batch.CostPrice, batch.SalesPrice, 0, false,
                quantity * batch.SalesPrice, 0, quantity * batch.SalesPrice, 0, "Recovered without refund")]).Value;
        var movement = StockTransaction.Create(batch.ShopId, batch.ItemId, batch.Id, StockTransactionType.Ret, quantity, recovered.ReturnNumber,
            "Retained fixture recovery", returnedAt, batch.CreatedBy, batch.CreatedBy).Value;
        var outMovement = StockTransaction.Create(batch.ShopId, batch.ItemId, batch.Id, StockTransactionType.Out, -quantity, sale.InvoiceNumber,
            "Original fixture sale", soldAt, batch.CreatedBy, batch.CreatedBy).Value;
        var returnedLine = recovered.Items.Single();
        var reason = batch.ExpiryDate.HasValue ? "Expiry.Expired" : "Expiry.RequiredDateMissing";
        var root = BatchDispositionChange.Quarantine(batch, "SaleReturn", recovered.Id, returnedLine.Id, movement.Id, quantity, reason,
            effective.Policy, date, batch.CreatedBy, returnedAt);
        returnedLine.FreezeDisposition(root.Id, quantity, reason);
        db.AddRange(sale, recovered, movement, outMovement, root);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE inventory_batches SET quarantined_quantity = {quantity} WHERE id = {batchId}");
        await tx.CommitAsync();
        db.ChangeTracker.Clear();
    }
}
