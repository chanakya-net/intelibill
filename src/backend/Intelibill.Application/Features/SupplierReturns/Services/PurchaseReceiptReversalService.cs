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

/// <summary>Reverses only qualified untouched source receipts, with an exact group boundary.</summary>
public sealed class PurchaseReceiptReversalService(
    SupplierReturnAccess access, ISupplierReturnRepository returns, IInventoryBatchRepository batches,
    IInventoryRepository inventory, IPurchaseReceiptValueRepository receipts, IPaymentReceiptRepository payments,
    IStockTransactionRepository movements, ISupplierLedgerEntryRepository ledger, IPurchaseReceiptReversalRepository reversals,
    ISupplierReturnMutationLock locks, IPostingPeriodPolicy periods, IBusinessClock clock,
    IFinancialOperationExecutor executor, IUnitOfWork uow, PurchaseReceiptReversalPolicy policy)
{
    private static Error Invalid(string code,string message) => Error.Validation($"SupplierReturn.{code}",message);
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    private static Error Missing => Error.NotFound("PurchaseReceipt.NotFound","Receipt lot was not found.");
    private static Error VersionConflict => Conflict("VersionConflict","The receipt or stock changed. Refresh the preview.");
    private static ErrorOr<string> Validate(string? key,string? reason)
    {
        if(string.IsNullOrWhiteSpace(key) || key.Length>128) return Error.Validation("Operation.InvalidKey","Operation key must be 1 to 128 characters.");
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is <3 or >255) return Invalid("ReasonInvalid","Reason must be 3 to 255 characters.");
        return key.Trim();
    }
    public async Task<ErrorOr<PurchaseReceiptReversalResultDto>> ReverseAsync(Guid actor,Guid shopId,Guid targetId,bool group,string operationKey,string reason,IReadOnlyList<ReceiptReversalLineInput> lines,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,true,ct); if(auth.IsError) return auth.Errors;
        var valid=Validate(operationKey,reason); if(valid.IsError) return valid.Errors;
        if(lines is null || lines.Count is <1 or >100 || lines.Select(x=>x.ReceiptValueId).Distinct().Count()!=lines.Count) return Invalid("LinesInvalid","Select every distinct receipt lot.");
        var hash=FinancialOperationHasher.Compute(new{targetId,group,reason=reason.Trim(),lines=lines.OrderBy(x=>x.ReceiptValueId).ToArray()});
        return await executor.ExecuteAsync<PurchaseReceiptReversalResultDto>(new(shopId,FinancialOperationType.PurchaseReceiptReversal,valid.Value),actor,targetId,hash,async token=>
        {
            var initial=group?await returns.GetGroupReceiptsAsync(shopId,targetId,token):await returns.GetReceiptsAsync(shopId,[targetId],token);
            if(initial.Count!=lines.Count || !initial.Select(x=>x.Id).Order().SequenceEqual(lines.Select(x=>x.ReceiptValueId).Order())) return Missing;
            var supplierId=initial[0].SupplierId; if(supplierId is null || initial.Any(x=>x.SupplierId!=supplierId)) return Invalid("SupplierInvalid","All lots must belong to one source supplier.");
            // Both period fences precede every business row lock.
            foreach(var date in initial.Select(x=>clock.GetBusinessDate(x.ReceivedAt,auth.Value.BusinessTimeZoneId)).Append(clock.Today(auth.Value.BusinessTimeZoneId)).Distinct().Order())
            { var fence=await periods.AuthorizeForWriteAsync(shopId,date,token); if(fence.IsError) return fence.Errors; }
            if(await locks.LockAsync(shopId,supplierId.Value,lines.Select(x=>x.ReceiptValueId).ToArray(),token)!=RowLockOutcome.Acquired) return Conflict("Busy","Receipt lots are busy. Retry this operation.");
            var rows=await returns.GetReceiptsAsync(shopId,lines.Select(x=>x.ReceiptValueId).ToArray(),token);
            var allocated=await payments.GetActiveAllocatedByTargetAsync(shopId,PaymentAllocationTargetType.SupplierReceipt,rows.Select(x=>x.Id).ToArray(),token);
            var results=new List<PurchaseReceiptReversalLotDto>(); var now=clock.UtcNow;
            foreach(var receipt in rows.OrderBy(x=>x.Id))
            {
                var input=lines.Single(x=>x.ReceiptValueId==receipt.Id); var batch=await batches.GetByIdAsync(receipt.InventoryBatchId,token); if(batch is null) return Missing;
                if(input.ExpectedReceiptVersion==0 || input.ExpectedBatchVersion==0 || input.ExpectedReceiptVersion!=receipt.Version || input.ExpectedBatchVersion!=batch.Version) return VersionConflict;
                var reasons=await policy.GetReasonsAsync(receipt,batch,allocated.GetValueOrDefault(receipt.Id),token); if(reasons.Count>0) return Error.Conflict(reasons[0],"Receipt is ineligible for full reversal; review its stock and payment history.");
                var stock=await inventory.GetByItemAsync(shopId,receipt.ItemId,token); if(stock is null) return Missing;
                var subtracted=stock.SubtractQuantity(receipt.ReceivedQuantity,actor); if(subtracted.IsError) return subtracted.Errors;
                var reference=$"RR-{Guid.NewGuid():N}";
                var movement=StockTransaction.Create(shopId,receipt.ItemId,batch.Id,StockTransactionType.Reversal,-receipt.ReceivedQuantity,reference,reason,now,actor,actor).Value;
                SupplierLedgerEntry? entry=null;
                if(receipt.EffectiveTotalCost>0) {entry=SupplierLedgerEntry.Create(shopId,supplierId.Value,batch.Id,SupplierLedgerEntryType.Reversal,-receipt.EffectiveTotalCost.Value,clock.Today(auth.Value.BusinessTimeZoneId),reference,actor).Value; await ledger.AddAsync(entry,token);}
                var reversal=PurchaseReceiptReversal.Create(shopId,receipt.Id,group?targetId:null,actor,reason,now,clock.Today(auth.Value.BusinessTimeZoneId),auth.Value.BusinessTimeZoneId,receipt.ReceivedQuantity,receipt.EffectiveTotalCost!.Value,movement.Id,entry?.Id,reference).Value;
                await movements.AddAsync(movement,token); await reversals.AddAsync(reversal,token); batch.Void(actor); batches.Update(batch); inventory.Update(stock); receipts.Update(receipt); results.Add(MapReversal(reversal));
            }
            try {await uow.SaveChangesAsync(token);} catch(DbUpdateConcurrencyException) {return VersionConflict;}
            return new PurchaseReceiptReversalResultDto(results);
        },ct);
    }
    private static PurchaseReceiptReversalLotDto MapReversal(PurchaseReceiptReversal x) => new(x.Id,x.ReceiptValueId,x.Quantity,x.EffectiveAmount,x.StockTransactionId,x.SupplierLedgerEntryId,x.ReversedAt,x.Reason);
}
