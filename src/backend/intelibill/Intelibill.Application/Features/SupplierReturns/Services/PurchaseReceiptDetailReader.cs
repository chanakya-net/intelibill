using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierReturns.Services;

/// <summary>Reads a receipt lot's shipped, reversed, credited, allocated and returnable position into its detail view.</summary>
public sealed class PurchaseReceiptDetailReader(
    ISupplierRepository suppliers,
    IInventoryBatchRepository batches,
    IItemRepository items,
    IPaymentReceiptRepository payments,
    ISupplierReturnRepository returns,
    IPurchaseReceiptReversalRepository reversals,
    PurchaseReceiptReversalPolicy reversalPolicy)
{
    internal async Task<PurchaseReceiptDetailDto> ReceiptDetail(PurchaseReceiptValue receipt,CancellationToken ct)
    {
        var batch=(await batches.GetByIdAsync(receipt.InventoryBatchId,ct))!;
        var item=(await items.GetByIdsAsync(receipt.ShopId,[receipt.ItemId],ct)).Single();
        var history=await returns.GetByReceiptsAsync(receipt.ShopId,[receipt.Id],ct);
        var reversed=await reversals.GetByReceiptsAsync(receipt.ShopId,[receipt.Id],ct);
        var shipped=history.SelectMany(x=>x.Lines).Where(x=>x.ReceiptValueId==receipt.Id).Sum(x=>x.Quantity);
        var credited=history
            .Where(x=>x.Credit is not null)
            .SelectMany(x=>x.Credit!.Lines)
            .Where(x=>x.ReceiptValueId==receipt.Id)
            .Sum(x=>x.AcceptedAmount);
        var allocated=(
            await payments.GetActiveAllocatedByTargetAsync(
                receipt.ShopId,
                PaymentAllocationTargetType.SupplierReceipt,
                [receipt.Id],
                ct)).GetValueOrDefault(receipt.Id);
        var net=batch.IsVoided || reversed.Count>0 ? 0m : Math.Max(0,receipt.EffectiveTotalCost-credited);
        var ceiling=SupplierReturnPolicy.ReturnableQuantity(batch.UnquarantinedQuantity,receipt.ReceivedQuantity,shipped);
        var reasons=await reversalPolicy.GetReasonsAsync(receipt,batch,allocated,ct);
        var supplier=receipt.SupplierId.HasValue?await suppliers.GetByIdAsync(receipt.SupplierId.Value,ct):null;
        if (supplier is null) reasons.Add("PurchaseReceipt.SupplierMissing");
        if (supplier is null || supplier.IsSystem) ceiling = 0;
        if(batch.IsVoided) ceiling=0;
        return new(
            receipt.Id,
            batch.Id,
            item.Id,
            receipt.SupplierId,
            receipt.PurchaseOrderReceiptLineId,
            batch.BatchNumber,
            item.Name,
            item.DocumentUnit,
            item.QuantityScale,
            receipt.Version,
            batch.Version,
            receipt.ReceivedQuantity,
            batch.Quantity,
            shipped,
            reversed.Sum(x=>x.Quantity),
            receipt.ReceivedQuantity-shipped-reversed.Sum(x=>x.Quantity),
            ceiling,
            receipt.EffectiveTotalCost,
            credited,
            allocated,
            net,
            Math.Max(0,net-allocated),
            Math.Max(0,allocated-net),
            Math.Min(net,decimal.Round(receipt.EffectiveTotalCost*ceiling/receipt.ReceivedQuantity,2,MidpointRounding.AwayFromZero)),
            batch.IsVoided,
            reasons,
            history.Select(Map).ToArray(),
            reversed.Select(MapReversal).ToArray());
    }
    internal static SupplierReturnDetailDto Map(SupplierReturn document)
    {
        var credit=document.Credit;
        return new(
            document.Id,
            document.SupplierId,
            document.ReturnNumber,
            document.Status.ToString(),
            document.Version,
            document.Reason,
            document.ShippedAt,
            document.BusinessDate,
            document.BusinessTimeZoneId,
            document.ShopName,
            document.SupplierName,
            document.Lines.Select(x=>
            {
                var accepted=credit?.Lines.SingleOrDefault(l=>l.ReturnLineId==x.Id);
                return new SupplierReturnLineDto(
                    x.Id,
                    x.ReceiptValueId,
                    x.InventoryBatchId,
                    x.ItemId,
                    x.PurchaseOrderReceiptLineId,
                    x.Quantity,
                    x.ProposedCreditAmount,
                    x.ReceivedQuantity,
                    x.EffectiveTotalCost,
                    x.ItemName,
                    x.Uqc,
                    x.SourceReference,
                    x.StockTransactionId,
                    accepted?.AcceptedAmount,
                    accepted?.SupplierLedgerEntryId,
                    x.BatchNumberSnapshot);
            }).ToArray(),
            credit is null
                ?null
                :new(
                    credit.Id,
                    credit.AcceptedAt,
                    credit.BusinessDate,
                    credit.CreatedBy,
                    credit.Reason,
                    credit.SupplierReference));
    }
    private static PurchaseReceiptReversalLotDto MapReversal(PurchaseReceiptReversal x) => new(
        x.Id,
        x.ReceiptValueId,
        x.Quantity,
        x.EffectiveAmount,
        x.StockTransactionId,
        x.SupplierLedgerEntryId,
        x.ReversedAt,
        x.Reason);
}
