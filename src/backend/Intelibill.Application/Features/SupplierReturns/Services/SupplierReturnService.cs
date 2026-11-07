using Intelibill.Domain.Events;
using Wolverine;
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

/// <summary>Coordinates immutable commercial facts inside the financial operation transaction.</summary>
public sealed class SupplierReturnService(
    SupplierReturnAccess access, ISupplierRepository suppliers,
    IPurchaseReceiptValueRepository receipts, IInventoryBatchRepository batches, IInventoryRepository inventory,
    IItemRepository items, IStockTransactionRepository movements,
    IPaymentReceiptRepository payments, ISupplierReturnRepository returns, IPurchaseReceiptReversalRepository reversals,
    ISupplierReturnMutationLock locks, IPostingPeriodPolicy periods, IBusinessClock clock,
    IFinancialOperationExecutor executor, IUnitOfWork uow, IMessageBus messageBus, SupplierReturnCreditPoster creditPoster, PurchaseReceiptReversalPolicy reversalPolicy)
{
    private static readonly SupplierReturnRequestValidator ShipmentValidator = new();
    private static readonly SupplierReturnCreditRequestValidator CreditValidator = new();
    private static Error Invalid(string code,string message) => Error.Validation($"SupplierReturn.{code}",message);
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    private static Error Missing => Error.NotFound("PurchaseReceipt.NotFound","Receipt lot was not found.");
    private static Error VersionConflict => Conflict("VersionConflict","The receipt, stock or return changed. Refresh the preview.");

    private static ErrorOr<string> Validate(string? key,string? reason)
    {
        if(string.IsNullOrWhiteSpace(key) || key.Length>128) return Error.Validation("Operation.InvalidKey","Operation key must be 1 to 128 characters.");
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is <3 or >255) return Invalid("ReasonInvalid","Reason must be 3 to 255 characters.");
        return key.Trim();
    }
    private async Task<ErrorOr<Success>> FenceAndLock(Shop shop,Guid supplierId,IReadOnlyCollection<Guid> ids,CancellationToken ct)
    {
        var fence=await periods.AuthorizeForWriteAsync(shop.Id,clock.Today(shop.BusinessTimeZoneId),ct);
        if(fence.IsError) return fence.Errors;
        var outcome=await locks.LockAsync(shop.Id,supplierId,ids,ct);
        if(outcome!=RowLockOutcome.Acquired) return Conflict("Busy","Supplier receipts are busy or unavailable. Retry this operation.");
        return Result.Success;
    }
    public async Task<ErrorOr<SupplierReturnDetailDto>> RecordAsync(Guid actor,Guid shopId,SupplierReturnRequest request,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,request.CreditAccepted,ct); if(auth.IsError) return auth.Errors;
        var valid=Validate(request.OperationKey,request.Reason); if(valid.IsError) return valid.Errors;
        var validation=ShipmentValidator.Validate(request);
        if(!validation.IsValid) return validation.Errors.Select(x=>Error.Validation("SupplierReturn.InvalidRequest",x.ErrorMessage)).ToList();
        if(request.Lines is null || request.Lines.Count is <1 or >100 || request.Lines.Select(x=>x.ReceiptValueId).Distinct().Count()!=request.Lines.Count) return Invalid("LinesInvalid","Select 1 to 100 distinct receipt lots.");
        if(request.SupplierReference?.Length>255) return Invalid("ReferenceInvalid","Supplier reference cannot exceed 255 characters.");
        var canonical=request with { OperationKey="", Reason=request.Reason.Trim(), SupplierReference=request.SupplierReference?.Trim(), Lines=request.Lines.OrderBy(x=>x.ReceiptValueId).Select(x=>x with {Quantity=FinancialOperationHasher.NormalizeAmount(x.Quantity),ProposedCreditAmount=FinancialOperationHasher.NormalizeAmount(x.ProposedCreditAmount)}).ToArray() };
        var outcome=await executor.ExecuteAsync<SupplierReturnDetailDto>(new(shopId,FinancialOperationType.SupplierReturn,valid.Value),actor,request.SupplierId,FinancialOperationHasher.Compute(canonical),async token=>
        {
            var locked=await FenceAndLock(auth.Value,request.SupplierId,request.Lines.Select(x=>x.ReceiptValueId).ToArray(),token); if(locked.IsError) return locked.Errors;
            var supplier=await suppliers.GetByIdAsync(request.SupplierId,token);
            if(supplier is null || supplier.ShopId!=shopId || supplier.IsSystem) return Invalid("SupplierInvalid","A real source supplier is required.");
            var rows=await returns.GetReceiptsAsync(shopId,request.Lines.Select(x=>x.ReceiptValueId).ToArray(),token);
            if(rows.Count!=request.Lines.Count) return Missing;
            var now=clock.UtcNow; var date=clock.GetBusinessDate(now,auth.Value.BusinessTimeZoneId);
            var document=SupplierReturn.Create(shopId,request.SupplierId,$"SR-{Guid.NewGuid():N}",request.Reason,actor,now,date,auth.Value.BusinessTimeZoneId,auth.Value.Name,supplier.Name).Value;
            var history=await returns.GetByReceiptsAsync(shopId,rows.Select(x=>x.Id).ToArray(),token);
            foreach(var input in request.Lines.OrderBy(x=>x.ReceiptValueId))
            {
                var receipt=rows.Single(x=>x.Id==input.ReceiptValueId);
                if(!receipt.IsResolved || receipt.EffectiveTotalCost is null || receipt.SupplierId!=supplier.Id) return Invalid("ProvenanceInvalid","Each lot must have resolved source evidence from this supplier.");
                var batch=await batches.GetByIdAsync(receipt.InventoryBatchId,token);
                if(batch is null || batch.ShopId!=shopId || batch.IsVoided) return Missing;
                if(receipt.Version!=input.ExpectedReceiptVersion || batch.Version!=input.ExpectedBatchVersion || input.ExpectedBatchVersion==0 || input.ExpectedReceiptVersion==0) return VersionConflict;
                var item=(await items.GetByIdsAsync(shopId,[receipt.ItemId],token)).SingleOrDefault();
                if(item is null) return Missing;
                var shipped=history.SelectMany(x=>x.Lines).Where(x=>x.ReceiptValueId==receipt.Id).Sum(x=>x.Quantity);
                if(input.Quantity<=0 || !QuantityPolicy.FitsScale(input.Quantity,item.QuantityScale) || input.Quantity>SupplierReturnPolicy.ReturnableQuantity(batch.Quantity,receipt.ReceivedQuantity,shipped)) return Invalid("QuantityInvalid","Quantity exceeds available receipt goods or item precision.");
                var stock=await inventory.GetByItemAsync(shopId,receipt.ItemId,token); if(stock is null) return Missing;
                var movement=StockTransaction.Create(shopId,receipt.ItemId,batch.Id,StockTransactionType.SupplierReturn,-input.Quantity,document.ReturnNumber,request.Reason,now,actor,actor).Value;
                var line=SupplierReturnLine.Create(shopId,document.Id,receipt.Id,batch.Id,receipt.ItemId,receipt.PurchaseOrderReceiptLineId,input.Quantity,input.ProposedCreditAmount,receipt.ReceivedQuantity,receipt.EffectiveTotalCost.Value,item.Name,item.DocumentUnit,receipt.SourceReference,movement.Id,batch.BatchNumber);
                if(line.IsError) return line.Errors;
                document.AddLine(line.Value);
                var reduced=batch.SubtractQuantity(input.Quantity,actor); if(reduced.IsError) return reduced.Errors;
                reduced=stock.SubtractQuantity(input.Quantity,actor); if(reduced.IsError) return reduced.Errors;
                receipt.MarkSupplierReturn(); receipts.Update(receipt); batches.Update(batch); inventory.Update(stock);
                await movements.AddAsync(movement,token);
            }
            await returns.AddAsync(document,token);
            if(request.CreditAccepted)
            {
                var accepted=await creditPoster.PostAsync(document,document.Lines.Select(x=>new SupplierReturnCreditLineInput(x.Id,x.ProposedCreditAmount)).ToArray(),actor,request.Reason,request.SupplierReference,auth.Value,rows,token);
                if(accepted.IsError) return accepted.Errors;
            }
            try { await uow.SaveChangesAsync(token); } catch(DbUpdateConcurrencyException) { return VersionConflict; }
            return Map(document);
        },ct);
        if(!outcome.IsError)
        {
            await messageBus.PublishAsync(new InventoryStockChangedDomainEvent(shopId,outcome.Value.Lines.Select(line=>line.ItemId).Distinct().ToArray()));
        }
        return outcome;
    }
    public async Task<ErrorOr<SupplierReturnDetailDto>> AcceptAsync(Guid actor,Guid shopId,Guid id,SupplierReturnCreditRequest request,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,true,ct); if(auth.IsError) return auth.Errors;
        var valid=Validate(request.OperationKey,request.Reason); if(valid.IsError) return valid.Errors;
        var validation=CreditValidator.Validate(request);
        if(!validation.IsValid) return validation.Errors.Select(x=>Error.Validation("SupplierReturn.InvalidRequest",x.ErrorMessage)).ToList();
        if(request.Lines is null || request.Lines.Count is <1 or >100) return Invalid("LinesInvalid","Every return line requires an agreed amount.");
        var source=await returns.GetDetailAsync(shopId,id,ct); if(source is null) return Error.NotFound("SupplierReturn.NotFound","Supplier return was not found.");
        var canonical=request with {OperationKey="",Reason=request.Reason.Trim(),SupplierReference=request.SupplierReference?.Trim(),Lines=request.Lines.OrderBy(x=>x.ReturnLineId).Select(x=>x with{AcceptedAmount=FinancialOperationHasher.NormalizeAmount(x.AcceptedAmount)}).ToArray()};
        return await executor.ExecuteAsync<SupplierReturnDetailDto>(new(shopId,FinancialOperationType.SupplierReturnCredit,valid.Value),actor,id,FinancialOperationHasher.Compute(new{id,request=canonical}),async token=>
        {
            var ids=source.Lines.Select(x=>x.ReceiptValueId).ToArray();
            var locked=await FenceAndLock(auth.Value,source.SupplierId,ids,token); if(locked.IsError) return locked.Errors;
            if(await locks.LockReturnAsync(shopId,id,token)!=RowLockOutcome.Acquired) return Conflict("Busy","Supplier return is busy. Retry this operation.");
            var document=await returns.GetDetailAsync(shopId,id,token); if(document is null) return Missing;
            if(document.Version!=request.ExpectedVersion || request.ExpectedVersion==0) return VersionConflict;
            var rows=await returns.GetReceiptsAsync(shopId,ids,token);
            var posted=await creditPoster.PostAsync(document,request.Lines,actor,request.Reason,request.SupplierReference,auth.Value,rows,token); if(posted.IsError) return posted.Errors;
            returns.Update(document); foreach(var receipt in rows) receipts.Update(receipt);
            try { await uow.SaveChangesAsync(token); } catch(DbUpdateConcurrencyException) {return VersionConflict;}
            return Map(document);
        },ct);
    }
    private SupplierReturnDetailDto Map(SupplierReturn document)
    {
        var credit=document.Credit;
        return new(document.Id,document.SupplierId,document.ReturnNumber,document.Status.ToString(),document.Version,document.Reason,document.ShippedAt,document.BusinessDate,document.BusinessTimeZoneId,document.ShopName,document.SupplierName,document.Lines.Select(x=>
        {
            var accepted=credit?.Lines.SingleOrDefault(l=>l.ReturnLineId==x.Id);
            return new SupplierReturnLineDto(x.Id,x.ReceiptValueId,x.InventoryBatchId,x.ItemId,x.PurchaseOrderReceiptLineId,x.Quantity,x.ProposedCreditAmount,x.ReceivedQuantity,x.EffectiveTotalCost,x.ItemName,x.Uqc,x.SourceReference,x.StockTransactionId,accepted?.AcceptedAmount,accepted?.SupplierLedgerEntryId,x.BatchNumberSnapshot);
        }).ToArray(),credit is null?null:new(credit.Id,credit.AcceptedAt,credit.BusinessDate,credit.CreatedBy,credit.Reason,credit.SupplierReference));
    }
    public async Task<ErrorOr<SupplierReturnDetailDto>> GetAsync(Guid actor,Guid shopId,Guid id,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct); if(auth.IsError) return auth.Errors;
        var document=await returns.GetDetailAsync(shopId,id,ct); return document is null?Error.NotFound("SupplierReturn.NotFound","Supplier return was not found."):Map(document);
    }
    public async Task<ErrorOr<IReadOnlyList<SupplierReturnDetailDto>>> ListAsync(Guid actor,Guid shopId,Guid? supplierId,Guid? receiptId,string? status,int page,int pageSize,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct); if(auth.IsError) return auth.Errors;
        if(page<1 || pageSize is <1 or >100 || (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<SupplierReturnStatus>(status,out _))) return Invalid("PagingInvalid","Page and status are invalid.");
        return (await returns.ListAsync(shopId,supplierId,receiptId,status,page,pageSize,ct)).Select(Map).ToArray();
    }
    public async Task<ErrorOr<PurchaseReceiptDetailDto>> GetReceiptAsync(Guid actor,Guid shopId,Guid id,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct); if(auth.IsError) return auth.Errors;
        var receipt=await receipts.GetByIdAsync(id,ct); if(receipt is null || receipt.ShopId!=shopId) return Missing;
        return await ReceiptDetail(receipt,ct);
    }
    private async Task<PurchaseReceiptDetailDto> ReceiptDetail(PurchaseReceiptValue receipt,CancellationToken ct)
    {
        var batch=(await batches.GetByIdAsync(receipt.InventoryBatchId,ct))!;
        var item=(await items.GetByIdsAsync(receipt.ShopId,[receipt.ItemId],ct)).Single();
        var history=await returns.GetByReceiptsAsync(receipt.ShopId,[receipt.Id],ct);
        var reversed=await reversals.GetByReceiptsAsync(receipt.ShopId,[receipt.Id],ct);
        var shipped=history.SelectMany(x=>x.Lines).Where(x=>x.ReceiptValueId==receipt.Id).Sum(x=>x.Quantity);
        var credited=history.Where(x=>x.Credit is not null).SelectMany(x=>x.Credit!.Lines).Where(x=>x.ReceiptValueId==receipt.Id).Sum(x=>x.AcceptedAmount);
        var allocated=(await payments.GetActiveAllocatedByTargetAsync(receipt.ShopId,PaymentAllocationTargetType.SupplierReceipt,[receipt.Id],ct)).GetValueOrDefault(receipt.Id);
        var net=batch.IsVoided || reversed.Count>0 ? 0m : Math.Max(0,(receipt.EffectiveTotalCost??0)-credited); var ceiling=SupplierReturnPolicy.ReturnableQuantity(batch.Quantity,receipt.ReceivedQuantity,shipped);
        var reasons=await reversalPolicy.GetReasonsAsync(receipt,batch,allocated,ct);
        var supplier=receipt.SupplierId.HasValue?await suppliers.GetByIdAsync(receipt.SupplierId.Value,ct):null;
        if(supplier is null || supplier.IsSystem) { reasons.Add("PurchaseReceipt.SupplierMissing"); ceiling=0; }
        if(!receipt.IsResolved || batch.IsVoided) ceiling=0;
        return new(receipt.Id,batch.Id,item.Id,receipt.SupplierId,receipt.PurchaseOrderReceiptLineId,batch.BatchNumber,item.Name,item.DocumentUnit,item.QuantityScale,receipt.Version,batch.Version,receipt.ReceivedQuantity,batch.Quantity,shipped,reversed.Sum(x=>x.Quantity),receipt.ReceivedQuantity-shipped-reversed.Sum(x=>x.Quantity),ceiling,receipt.EffectiveTotalCost,credited,allocated,net,Math.Max(0,net-allocated),Math.Max(0,allocated-net),Math.Min(net,decimal.Round((receipt.EffectiveTotalCost??0)*ceiling/receipt.ReceivedQuantity,2,MidpointRounding.AwayFromZero)),batch.IsVoided,reasons,history.Select(Map).ToArray(),reversed.Select(MapReversal).ToArray());
    }
    public async Task<ErrorOr<SupplierReturnPreviewDto>> PreviewAsync(Guid actor,Guid shopId,SupplierReturnPreviewRequest request,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct); if(auth.IsError) return auth.Errors;
        if(request.Lines is null || request.Lines.Count is <1 or >100 || request.Lines.Select(x=>x.ReceiptValueId).Distinct().Count()!=request.Lines.Count) return Invalid("LinesInvalid","Select distinct receipt lots.");
        var result=new List<PurchaseReceiptDetailDto>();
        foreach(var input in request.Lines)
        {
            var receipt=await receipts.GetByIdAsync(input.ReceiptValueId,ct); if(receipt is null || receipt.ShopId!=shopId || receipt.SupplierId!=request.SupplierId) return Missing;
            var detail=await ReceiptDetail(receipt,ct);
            result.Add(detail with {SuggestedCreditAmount=Math.Min(detail.NetPayableAmount,decimal.Round((receipt.EffectiveTotalCost??0)*Math.Max(0,input.Quantity)/receipt.ReceivedQuantity,2,MidpointRounding.AwayFromZero))});
        }
        return new SupplierReturnPreviewDto(result);
    }
    private static PurchaseReceiptReversalLotDto MapReversal(PurchaseReceiptReversal x) => new(x.Id,x.ReceiptValueId,x.Quantity,x.EffectiveAmount,x.StockTransactionId,x.SupplierLedgerEntryId,x.ReversedAt,x.Reason);
}
