using Intelibill.Domain.Events;
using Wolverine;
using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierReturns.Services;

/// <summary>Coordinates immutable commercial facts inside the financial operation transaction.</summary>
public sealed class SupplierReturnService(
    SupplierReturnAccess access,
    IPurchaseReceiptValueRepository receipts,
    ISupplierReturnRepository returns,
    ISupplierReturnMutationLock locks,
    IPostingPeriodPolicy periods,
    IBusinessClock clock,
    IFinancialOperationExecutor executor,
    IShopLicenceFence licence,
    IUnitOfWork uow,
    IMessageBus messageBus,
    SupplierReturnCreditPoster creditPoster,
    SupplierReturnShipper shipper,
    PurchaseReceiptDetailReader detailReader)
{
    private static readonly SupplierReturnRequestValidator ShipmentValidator = new();
    private static readonly SupplierReturnCreditRequestValidator CreditValidator = new();
    private static Error Invalid(string code,string message) => Error.Validation($"SupplierReturn.{code}",message);
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    private static Error Missing => Error.NotFound("PurchaseReceipt.NotFound","Receipt lot was not found.");
    private static Error VersionConflict => Conflict("VersionConflict","The receipt, stock or return changed. Refresh the preview.");

    private static ErrorOr<string> Validate(string? key,string? reason)
    {
        if(string.IsNullOrWhiteSpace(key) || key.Length>128)
            return Error.Validation("Operation.InvalidKey","Operation key must be 1 to 128 characters.");
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is <3 or >255)
            return Invalid("ReasonInvalid","Reason must be 3 to 255 characters.");
        return key.Trim();
    }
    private async Task<ErrorOr<Success>> FenceAndLock(Shop shop,Guid supplierId,IReadOnlyCollection<Guid> ids,CancellationToken ct)
    {
        var fence=await periods.AuthorizeForWriteAsync(shop.Id,clock.Today(shop.BusinessTimeZoneId),ct);
        if(fence.IsError) return fence.Errors;
        var outcome=await locks.LockAsync(shop.Id,supplierId,ids,ct);
        if(outcome!=RowLockOutcome.Acquired)
            return Conflict("Busy","Supplier receipts are busy or unavailable. Retry this operation.");
        return Result.Success;
    }
    public async Task<ErrorOr<SupplierReturnDetailDto>> RecordAsync(
        Guid actor,
        Guid shopId,
        SupplierReturnRequest request,
        CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,request.CreditAccepted,ct);
        if(auth.IsError) return auth.Errors;
        var valid=Validate(request.OperationKey,request.Reason);
        if(valid.IsError) return valid.Errors;
        var shape=ValidateShipmentShape(request);
        if(shape.IsError) return shape.Errors;
        var canonical=CanonicalShipment(request);
        var outcome=await executor.ExecuteAsync<SupplierReturnDetailDto>(
            new(shopId,FinancialOperationType.SupplierReturn,valid.Value) { SuspensionAction = LicenceAction.SourceCorrection },
            actor,
            request.SupplierId,
            FinancialOperationHasher.Compute(canonical),
            async token=>
            {
                var lease = await licence.EnterAsync(shopId, LicenceAction.SourceCorrection, token);
                if (lease.IsError) return lease.Errors;
                var locked=await FenceAndLock(auth.Value,request.SupplierId,request.Lines.Select(x=>x.ReceiptValueId).ToArray(),token);
                if(locked.IsError) return locked.Errors;
                if (lease.Value.CurrentDecision().EnforcedDenial() is { } lockDenial) return lockDenial;
                var supplier=await shipper.LoadSourceSupplierAsync(shopId,request.SupplierId,token);
                if(supplier.IsError) return supplier.Errors;
                var rows=await returns.GetReceiptsAsync(shopId,request.Lines.Select(x=>x.ReceiptValueId).ToArray(),token);
                if(rows.Count!=request.Lines.Count) return Missing;
                var now=clock.UtcNow;
                var document=StartShipment(actor,shopId,request,auth.Value,supplier.Value,now);
                var shipped=await shipper.ShipLotsAsync(actor,shopId,request,supplier.Value,rows,document,now,token);
                if(shipped.IsError) return shipped.Errors;
                await returns.AddAsync(document,token);
                if(request.CreditAccepted)
                {
                    var accepted=await AcceptProposedCreditAsync(actor,request,auth.Value,rows,document,token);
                    if(accepted.IsError) return accepted.Errors;
                }
                if (
                    SupplierReturnPolicy.CheckCompletion(
                        lease.Value,
                        rows,
                        request.SupplierId,
                        clock,
                        document.ShippedAt,
                        document) is { } denied)
                    return denied;
                try { await uow.SaveChangesAsync(token); } catch (Exception ex) when (uow.IsConcurrencyConflict(ex)) { return VersionConflict; }
                return Map(document);
            },ct);
        if(!outcome.IsError)
        {
            await messageBus.PublishAsync(
                new InventoryStockChangedDomainEvent(shopId,outcome.Value.Lines.Select(line=>line.ItemId).Distinct().ToArray()));
        }
        return outcome;
    }
    private static ErrorOr<Success> ValidateShipmentShape(SupplierReturnRequest request)
    {
        var validation=ShipmentValidator.Validate(request);
        if(!validation.IsValid)
            return validation.Errors.Select(x=>Error.Validation("SupplierReturn.InvalidRequest",x.ErrorMessage)).ToList();
        if(request.Lines is null
            || request.Lines.Count is <1 or >100
            || request.Lines.Select(x=>x.ReceiptValueId).Distinct().Count()!=request.Lines.Count)
            return Invalid("LinesInvalid","Select 1 to 100 distinct receipt lots.");
        if(request.SupplierReference?.Length>255)
            return Invalid("ReferenceInvalid","Supplier reference cannot exceed 255 characters.");
        return Result.Success;
    }
    private static SupplierReturnRequest CanonicalShipment(SupplierReturnRequest request) => request with {
        OperationKey="",
        Reason=request.Reason.Trim(),
        SupplierReference=request.SupplierReference?.Trim(),
        Lines=request.Lines
            .OrderBy(x=>x.ReceiptValueId)
            .Select(
                x=>x with {
                    Quantity=FinancialOperationHasher.NormalizeAmount(x.Quantity),
                    ProposedCreditAmount=FinancialOperationHasher.NormalizeAmount(x.ProposedCreditAmount)
                })
            .ToArray()
    };
    private SupplierReturn StartShipment(
        Guid actor,
        Guid shopId,
        SupplierReturnRequest request,
        Shop shop,
        Supplier supplier,
        DateTimeOffset now)
    {
        var date=clock.GetBusinessDate(now,shop.BusinessTimeZoneId);
        return SupplierReturn.Create(
            shopId,
            request.SupplierId,
            $"SR-{Guid.NewGuid():N}",
            request.Reason,
            actor,
            now,
            date,
            shop.BusinessTimeZoneId,
            shop.Name,
            supplier.Name).Value;
    }
    private Task<ErrorOr<Success>> AcceptProposedCreditAsync(
        Guid actor,
        SupplierReturnRequest request,
        Shop shop,
        IReadOnlyList<PurchaseReceiptValue> rows,
        SupplierReturn document,
        CancellationToken token) =>
        creditPoster.PostAsync(
            document,
            document.Lines.Select(x=>new SupplierReturnCreditLineInput(x.Id,x.ProposedCreditAmount)).ToArray(),
            actor,
            request.Reason,
            request.SupplierReference,
            shop,
            rows,
            token);
    public async Task<ErrorOr<SupplierReturnDetailDto>> AcceptAsync(
        Guid actor,
        Guid shopId,
        Guid id,
        SupplierReturnCreditRequest request,
        CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,true,ct);
        if(auth.IsError) return auth.Errors;
        var valid=Validate(request.OperationKey,request.Reason);
        if(valid.IsError) return valid.Errors;
        var validation=CreditValidator.Validate(request);
        if(!validation.IsValid)
            return validation.Errors.Select(x=>Error.Validation("SupplierReturn.InvalidRequest",x.ErrorMessage)).ToList();
        if(request.Lines is null || request.Lines.Count is <1 or >100)
            return Invalid("LinesInvalid","Every return line requires an agreed amount.");
        var canonical=request with {
            OperationKey="",
            Reason=request.Reason.Trim(),
            SupplierReference=request.SupplierReference?.Trim(),
            Lines=request.Lines
                .OrderBy(x=>x.ReturnLineId)
                .Select(x=>x with{AcceptedAmount=FinancialOperationHasher.NormalizeAmount(x.AcceptedAmount)})
                .ToArray()
        };
        return await executor.ExecuteAsync<SupplierReturnDetailDto>(
            new(shopId,FinancialOperationType.SupplierReturnCredit,valid.Value) { SuspensionAction = LicenceAction.SourceCorrection },
            actor,
            id,
            FinancialOperationHasher.Compute(new{id,request=canonical}),
            async token=>
            {
                var lease = await licence.EnterAsync(shopId, LicenceAction.SourceCorrection, token);
                if (lease.IsError) return lease.Errors;
                if (lease.Value.CurrentDecision().EnforcedDenial() is { } admissionDenial) return admissionDenial;
                var source = await returns.GetDetailAsync(shopId, id, token);
                if (lease.Value.CurrentDecision().EnforcedDenial() is { } sourceDenial) return sourceDenial;
                if (source is null) return Error.NotFound("SupplierReturn.NotFound", "Supplier return was not found.");
                var ids=source.Lines.Select(x=>x.ReceiptValueId).ToArray();
                var locked=await FenceAndLock(auth.Value,source.SupplierId,ids,token);
                if(locked.IsError) return locked.Errors;
                if(await locks.LockReturnAsync(shopId,id,token)!=RowLockOutcome.Acquired)
                    return Conflict("Busy","Supplier return is busy. Retry this operation.");
                if (lease.Value.CurrentDecision().EnforcedDenial() is { } lockDenial) return lockDenial;
                var document=await returns.GetDetailAsync(shopId,id,token);
                if(document is null) return Missing;
                if(document.Version!=request.ExpectedVersion || request.ExpectedVersion==0) return VersionConflict;
                var rows=await returns.GetReceiptsAsync(shopId,ids,token);
                if (rows.Count != ids.Distinct().Count()) return Missing;
                var posted=await creditPoster.PostAsync(
                    document,
                    request.Lines,
                    actor,
                    request.Reason,
                    request.SupplierReference,
                    auth.Value,
                    rows,
                    token);
                if(posted.IsError) return posted.Errors;
                returns.Update(document);
                foreach(var receipt in rows) receipts.Update(receipt);
                if (
                    SupplierReturnPolicy.CheckCompletion(
                        lease.Value,
                        rows,
                        document.SupplierId,
                        clock,
                        document.ShippedAt,
                        document) is { } denied)
                    return denied;
                try { await uow.SaveChangesAsync(token); } catch (Exception ex) when (uow.IsConcurrencyConflict(ex)) {return VersionConflict;}
                return Map(document);
            },ct);
    }
    private static SupplierReturnDetailDto Map(SupplierReturn document) => PurchaseReceiptDetailReader.Map(document);
    public async Task<ErrorOr<SupplierReturnDetailDto>> GetAsync(Guid actor,Guid shopId,Guid id,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct);
        if(auth.IsError) return auth.Errors;
        var document=await returns.GetDetailAsync(shopId,id,ct);
        return document is null?Error.NotFound("SupplierReturn.NotFound","Supplier return was not found."):Map(document);
    }
    public async Task<ErrorOr<IReadOnlyList<SupplierReturnDetailDto>>> ListAsync(
        Guid actor,
        Guid shopId,
        Guid? supplierId,
        Guid? receiptId,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct);
        if(auth.IsError) return auth.Errors;
        if(page<1 || pageSize is <1 or >100 || (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<SupplierReturnStatus>(status,out _)))
            return Invalid("PagingInvalid","Page and status are invalid.");
        return (await returns.ListAsync(shopId,supplierId,receiptId,status,page,pageSize,ct)).Select(Map).ToArray();
    }
    public async Task<ErrorOr<PurchaseReceiptDetailDto>> GetReceiptAsync(Guid actor,Guid shopId,Guid id,CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct);
        if(auth.IsError) return auth.Errors;
        var receipt=await receipts.GetByIdAsync(id,ct);
        if(receipt is null || receipt.ShopId!=shopId) return Missing;
        return await detailReader.ReceiptDetail(receipt,ct);
    }
    public async Task<ErrorOr<SupplierReturnPreviewDto>> PreviewAsync(
        Guid actor,
        Guid shopId,
        SupplierReturnPreviewRequest request,
        CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,false,ct);
        if(auth.IsError) return auth.Errors;
        if(request.Lines is null
            || request.Lines.Count is <1 or >100
            || request.Lines.Select(x=>x.ReceiptValueId).Distinct().Count()!=request.Lines.Count)
            return Invalid("LinesInvalid","Select distinct receipt lots.");
        var result=new List<PurchaseReceiptDetailDto>();
        foreach(var input in request.Lines)
        {
            var receipt=await receipts.GetByIdAsync(input.ReceiptValueId,ct);
            if(receipt is null || receipt.ShopId!=shopId || receipt.SupplierId!=request.SupplierId) return Missing;
            var detail=await detailReader.ReceiptDetail(receipt,ct);
            result.Add(
                detail with {
                    SuggestedCreditAmount=Math.Min(
                        detail.NetPayableAmount,
                        decimal.Round(
                            receipt.EffectiveTotalCost*Math.Max(0,input.Quantity)/receipt.ReceivedQuantity,
                            2,
                            MidpointRounding.AwayFromZero))
                });
        }
        return new SupplierReturnPreviewDto(result);
    }
}
