using ErrorOr;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.SupplierReturns.Services;

/// <summary>Checks the source supplier and ships each requested receipt lot out of batch and inventory stock.</summary>
public sealed class SupplierReturnShipper(
    ISupplierRepository suppliers,
    IPurchaseReceiptValueRepository receipts,
    IInventoryBatchRepository batches,
    IInventoryRepository inventory,
    IItemRepository items,
    IStockTransactionRepository movements,
    ISupplierReturnRepository returns)
{
    private static Error Invalid(string code,string message) => Error.Validation($"SupplierReturn.{code}",message);
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    private static Error Missing => Error.NotFound("PurchaseReceipt.NotFound","Receipt lot was not found.");
    private static Error VersionConflict => Conflict("VersionConflict","The receipt, stock or return changed. Refresh the preview.");

    internal async Task<ErrorOr<Supplier>> LoadSourceSupplierAsync(Guid shopId,Guid supplierId,CancellationToken token)
    {
        var supplier=await suppliers.GetByIdAsync(supplierId,token);
        if(supplier is null || supplier.ShopId!=shopId || supplier.IsSystem)
            return Invalid("SupplierInvalid","A real source supplier is required.");
        return supplier;
    }
    internal async Task<ErrorOr<Success>> ShipLotsAsync(
        Guid actor,
        Guid shopId,
        SupplierReturnRequest request,
        Supplier supplier,
        IReadOnlyList<PurchaseReceiptValue> rows,
        SupplierReturn document,
        DateTimeOffset now,
        CancellationToken token)
    {
        var history=await returns.GetByReceiptsAsync(shopId,rows.Select(x=>x.Id).ToArray(),token);
        foreach(var input in request.Lines.OrderBy(x=>x.ReceiptValueId))
        {
            var lot=await LoadShippableLotAsync(shopId,supplier,rows,history,input,token);
            if(lot.IsError) return lot.Errors;
            var posted=await ShipLotAsync(actor,shopId,request.Reason,document,input,lot.Value,now,token);
            if(posted.IsError) return posted.Errors;
        }
        return Result.Success;
    }
    private async Task<ErrorOr<ShippableLot>> LoadShippableLotAsync(
        Guid shopId,
        Supplier supplier,
        IReadOnlyList<PurchaseReceiptValue> rows,
        IReadOnlyList<SupplierReturn> history,
        SupplierReturnLineInput input,
        CancellationToken token)
    {
        var receipt=rows.Single(x=>x.Id==input.ReceiptValueId);
        if(receipt.SupplierId!=supplier.Id)
            return Invalid("ProvenanceInvalid","Each lot must have resolved source evidence from this supplier.");
        var batch=await batches.GetByIdAsync(receipt.InventoryBatchId,token);
        if(batch is null || batch.ShopId!=shopId || batch.IsVoided) return Missing;
        if(receipt.Version!=input.ExpectedReceiptVersion
            || batch.Version!=input.ExpectedBatchVersion
            || input.ExpectedBatchVersion==0
            || input.ExpectedReceiptVersion==0)
            return VersionConflict;
        var item=(await items.GetByIdsAsync(shopId,[receipt.ItemId],token)).SingleOrDefault();
        if(item is null) return Missing;
        var shipped=history.SelectMany(x=>x.Lines).Where(x=>x.ReceiptValueId==receipt.Id).Sum(x=>x.Quantity);
        if(input.Quantity<=0
            || !QuantityPolicy.FitsScale(input.Quantity,item.QuantityScale)
            || input.Quantity>SupplierReturnPolicy.ReturnableQuantity(
                batch.UnquarantinedQuantity,
                receipt.ReceivedQuantity,
                shipped))
            return Invalid("QuantityInvalid","Quantity exceeds available receipt goods or item precision.");
        var stock=await inventory.GetByItemAsync(shopId,receipt.ItemId,token);
        if(stock is null) return Missing;
        return new ShippableLot(receipt,batch,item,stock);
    }
    private async Task<ErrorOr<Success>> ShipLotAsync(
        Guid actor,
        Guid shopId,
        string reason,
        SupplierReturn document,
        SupplierReturnLineInput input,
        ShippableLot lot,
        DateTimeOffset now,
        CancellationToken token)
    {
        var (receipt,batch,item,stock)=lot;
        var movement=StockTransaction.Create(
            shopId,
            receipt.ItemId,
            batch.Id,
            StockTransactionType.SupplierReturn,
            -input.Quantity,
            document.ReturnNumber,
            reason,
            now,
            actor,
            actor).Value;
        var line=SupplierReturnLine.Create(
            shopId,
            document.Id,
            receipt.Id,
            batch.Id,
            receipt.ItemId,
            receipt.PurchaseOrderReceiptLineId,
            input.Quantity,
            input.ProposedCreditAmount,
            receipt.ReceivedQuantity,
            receipt.EffectiveTotalCost,
            item.Name,
            item.DocumentUnit,
            receipt.SourceReference,
            movement.Id,
            batch.BatchNumber);
        if(line.IsError) return line.Errors;
        document.AddLine(line.Value);
        var reduced=batch.SubtractQuantity(input.Quantity,actor);
        if(reduced.IsError) return reduced.Errors;
        reduced=stock.SubtractQuantity(input.Quantity,actor);
        if(reduced.IsError) return reduced.Errors;
        receipt.MarkSupplierReturn();
        receipts.Update(receipt);
        batches.Update(batch);
        inventory.Update(stock);
        await movements.AddAsync(movement,token);
        return Result.Success;
    }
    private sealed record ShippableLot(
        PurchaseReceiptValue Receipt,
        InventoryBatch Batch,
        Item Item,
        Intelibill.Domain.Entities.Inventory Stock);
}
