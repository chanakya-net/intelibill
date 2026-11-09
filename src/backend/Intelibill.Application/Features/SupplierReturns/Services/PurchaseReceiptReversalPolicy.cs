using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.SupplierReturns.Services;

public sealed class PurchaseReceiptReversalPolicy(ISupplierReturnRepository returns,IStockTransactionRepository movements, IBatchDispositionRepository dispositions)
{
    public async Task<List<string>> GetReasonsAsync(PurchaseReceiptValue receipt,InventoryBatch batch,decimal allocated,CancellationToken ct)
    {
        var result=new List<string>();
        if(batch.QuarantinedQuantity > 0 || await dispositions.HasHistoryAsync(receipt.ShopId,batch.Id,ct)) result.Add("PurchaseReceipt.QuarantineHistory");
        if(!receipt.IsResolved) result.Add("PurchaseReceipt.EvidenceMissing");
        if(batch.IsVoided) result.Add("PurchaseReceipt.BatchVoided");
        if(receipt.HasSupplierReturns) result.Add("PurchaseReceipt.HasSupplierReturns");
        if(batch.Quantity!=receipt.ReceivedQuantity) result.Add("Inventory.BatchConsumed");
        if(allocated>0) result.Add("Payment.DocumentHasAllocations");
        if(await returns.HasCountHistoryAsync(receipt.ShopId,batch.Id,ct)) result.Add("PurchaseReceipt.CountHistory");
        var history=await movements.GetByBatchAsync(receipt.ShopId,batch.Id,ct);
        if(history.Any(x=>x.TransactionType!=StockTransactionType.In) || history.Count(x=>x.TransactionType==StockTransactionType.In)!=1) result.Add("Inventory.BatchConsumed");
        return result;
    }
}
