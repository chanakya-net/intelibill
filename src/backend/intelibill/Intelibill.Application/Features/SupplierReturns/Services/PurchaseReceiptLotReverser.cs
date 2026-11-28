using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierReturns.Services;

/// <summary>Decides whether a locked receipt lot is fully reversible and posts its stock, batch and supplier ledger reversal.</summary>
public sealed class PurchaseReceiptLotReverser(
    IInventoryBatchRepository batches,
    IInventoryRepository inventory,
    IPurchaseReceiptValueRepository receipts,
    IStockTransactionRepository movements,
    ISupplierLedgerEntryRepository ledger,
    IPurchaseReceiptReversalRepository reversals,
    IBusinessClock clock,
    PurchaseReceiptReversalPolicy policy)
{
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    private static Error Missing => Error.NotFound("PurchaseReceipt.NotFound","Receipt lot was not found.");
    private static Error VersionConflict => Conflict("VersionConflict","The receipt or stock changed. Refresh the preview.");
    internal async Task<ErrorOr<ReversibleLot>> LoadReversibleLotAsync(
        Guid shopId,
        PurchaseReceiptValue receipt,
        IReadOnlyList<ReceiptReversalLineInput> lines,
        IReadOnlyDictionary<Guid,decimal> allocated,
        CancellationToken token)
    {
        var input=lines.Single(x=>x.ReceiptValueId==receipt.Id);
        var batch=await batches.GetByIdAsync(receipt.InventoryBatchId,token);
        if(batch is null) return Missing;
        if(input.ExpectedReceiptVersion==0
            || input.ExpectedBatchVersion==0
            || input.ExpectedReceiptVersion!=receipt.Version
            || input.ExpectedBatchVersion!=batch.Version)
            return VersionConflict;
        var reasons=await policy.GetReasonsAsync(receipt,batch,allocated.GetValueOrDefault(receipt.Id),token);
        if(reasons.Count>0)
            return Error.Conflict(reasons[0],"Receipt is ineligible for full reversal; review its stock and payment history.");
        var stock=await inventory.GetByItemAsync(shopId,receipt.ItemId,token);
        if(stock is null) return Missing;
        return new ReversibleLot(batch,stock);
    }
    internal async Task<ErrorOr<PurchaseReceiptReversal>> ReverseLotAsync(
        ReversalContext context,
        PurchaseReceiptValue receipt,
        ReversibleLot lot,
        CancellationToken token)
    {
        var (batch,stock)=lot;
        var subtracted=stock.SubtractQuantity(receipt.ReceivedQuantity,context.Actor);
        if(subtracted.IsError) return subtracted.Errors;
        var reference=$"RR-{Guid.NewGuid():N}";
        var movement=StockTransaction.Create(
            context.ShopId,
            receipt.ItemId,
            batch.Id,
            StockTransactionType.Reversal,
            -receipt.ReceivedQuantity,
            reference,
            context.Reason,
            context.Now,
            context.Actor,
            context.Actor).Value;
        var entry=await PostReversalLedgerEntryAsync(context,receipt,batch,reference,token);
        var reversal=PurchaseReceiptReversal.Create(
            context.ShopId,
            receipt.Id,
            context.GroupId,
            context.Actor,
            context.Reason,
            context.Now,
            clock.Today(context.Shop.BusinessTimeZoneId),
            context.Shop.BusinessTimeZoneId,
            receipt.ReceivedQuantity,
            receipt.EffectiveTotalCost,
            movement.Id,
            entry?.Id,
            reference).Value;
        await movements.AddAsync(movement,token);
        await reversals.AddAsync(reversal,token);
        batch.Void(context.Actor);
        batches.Update(batch);
        inventory.Update(stock);
        receipts.Update(receipt);
        return reversal;
    }
    private async Task<SupplierLedgerEntry?> PostReversalLedgerEntryAsync(
        ReversalContext context,
        PurchaseReceiptValue receipt,
        InventoryBatch batch,
        string reference,
        CancellationToken token)
    {
        SupplierLedgerEntry? entry=null;
        if(receipt.EffectiveTotalCost>0)
        {
            entry=SupplierLedgerEntry.Create(
                context.ShopId,
                context.SupplierId,
                batch.Id,
                SupplierLedgerEntryType.Reversal,
                -receipt.EffectiveTotalCost,
                clock.Today(context.Shop.BusinessTimeZoneId),
                reference,
                context.Actor).Value;
            await ledger.AddAsync(entry,token);
        }
        return entry;
    }
    internal sealed record ReversalContext(
        Guid Actor,
        Guid ShopId,
        Guid SupplierId,
        Guid? GroupId,
        string Reason,
        Shop Shop,
        DateTimeOffset Now);
    internal sealed record ReversibleLot(InventoryBatch Batch,Intelibill.Domain.Entities.Inventory Stock);
}
