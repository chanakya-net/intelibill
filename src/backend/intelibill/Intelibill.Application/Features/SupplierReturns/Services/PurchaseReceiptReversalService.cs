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
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.SupplierReturns.Services;

/// <summary>Reverses only qualified untouched source receipts, with an exact group boundary.</summary>
public sealed class PurchaseReceiptReversalService(
    SupplierReturnAccess access,
    ISupplierReturnRepository returns,
    IPaymentReceiptRepository payments,
    ISupplierReturnMutationLock locks,
    IPostingPeriodPolicy periods,
    IBusinessClock clock,
    IFinancialOperationExecutor executor,
    IShopLicenceFence licence,
    IUnitOfWork uow,
    IMessageBus messageBus,
    PurchaseReceiptLotReverser reverser)
{
    private static Error Invalid(string code,string message) => Error.Validation($"SupplierReturn.{code}",message);
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    private static Error Missing => Error.NotFound("PurchaseReceipt.NotFound","Receipt lot was not found.");
    private static Error VersionConflict => Conflict("VersionConflict","The receipt or stock changed. Refresh the preview.");
    private static ErrorOr<string> Validate(string? key,string? reason)
    {
        if(string.IsNullOrWhiteSpace(key) || key.Length>128)
            return Error.Validation("Operation.InvalidKey","Operation key must be 1 to 128 characters.");
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is <3 or >255)
            return Invalid("ReasonInvalid","Reason must be 3 to 255 characters.");
        return key.Trim();
    }
    public async Task<ErrorOr<PurchaseReceiptReversalResultDto>> ReverseAsync(
        Guid actor,
        Guid shopId,
        Guid targetId,
        bool group,
        string operationKey,
        string reason,
        IReadOnlyList<ReceiptReversalLineInput> lines,
        CancellationToken ct)
    {
        var auth=await access.AuthorizeAsync(actor,shopId,true,ct);
        if(auth.IsError) return auth.Errors;
        var valid=Validate(operationKey,reason);
        if(valid.IsError) return valid.Errors;
        if(lines is null || lines.Count is <1 or >100 || lines.Select(x=>x.ReceiptValueId).Distinct().Count()!=lines.Count)
            return Invalid("LinesInvalid","Select every distinct receipt lot.");
        var hash=FinancialOperationHasher.Compute(
            new{targetId,group,reason=reason.Trim(),lines=lines.OrderBy(x=>x.ReceiptValueId).ToArray()});
        var outcome=await executor.ExecuteAsync<PurchaseReceiptReversalResultDto>(
            new(shopId,FinancialOperationType.PurchaseReceiptReversal,valid.Value) { SuspensionAction = LicenceAction.SourceCorrection },
            actor,
            targetId,
            hash,
            async token=>
            {
                var lease = await licence.EnterAsync(shopId, LicenceAction.SourceCorrection, token);
                if (lease.IsError) return lease.Errors;
                var initial=await LoadReversalTargetAsync(shopId,targetId,group,token);
                if(!MatchesRequestedLots(initial,lines))
                    return Missing;
                var source=ResolveSourceSupplier(initial);
                if(source.IsError) return source.Errors;
                var supplierId=source.Value;
                // Both period fences precede every business row lock.
                var fenced=await FenceReversalPeriodsAsync(shopId,auth.Value,initial,token);
                if(fenced.IsError) return fenced.Errors;
                if(
                    await locks.LockAsync(
                        shopId,
                        supplierId,
                        lines.Select(x=>x.ReceiptValueId).ToArray(),
                        token)!=RowLockOutcome.Acquired)
                    return Conflict("Busy","Receipt lots are busy. Retry this operation.");
                if (lease.Value.CurrentDecision().EnforcedDenial() is { } lockDenial) return lockDenial;
                var rows=await returns.GetReceiptsAsync(shopId,lines.Select(x=>x.ReceiptValueId).ToArray(),token);
                if (rows.Count != lines.Count) return Missing;
                if (rows.Any(x => x.SupplierId != supplierId)) return VersionConflict;
                var allocated=await payments.GetActiveAllocatedByTargetAsync(
                    shopId,
                    PaymentAllocationTargetType.SupplierReceipt,
                    rows.Select(x=>x.Id).ToArray(),
                    token);
                var results=new List<PurchaseReceiptReversalLotDto>();
                var now=clock.UtcNow;
                var context=new PurchaseReceiptLotReverser.ReversalContext(actor,shopId,supplierId,group?targetId:null,reason,auth.Value,now);
                foreach(var receipt in rows.OrderBy(x=>x.Id))
                {
                    var lot=await reverser.LoadReversibleLotAsync(shopId,receipt,lines,allocated,token);
                    if(lot.IsError) return lot.Errors;
                    var reversal=await reverser.ReverseLotAsync(context,receipt,lot.Value,token);
                    if(reversal.IsError) return reversal.Errors;
                    results.Add(MapReversal(reversal.Value));
                }
                if (SupplierReturnPolicy.CheckCompletion(lease.Value, rows, supplierId, clock, now) is { } denied)
                    return denied;
                try {await uow.SaveChangesAsync(token);} catch (Exception ex) when (uow.IsConcurrencyConflict(ex)) {return VersionConflict;}
                return new PurchaseReceiptReversalResultDto(results);
            },ct);
        if(!outcome.IsError)
        {
            var committed=await returns.GetReceiptsAsync(shopId,outcome.Value.Lots.Select(lot=>lot.ReceiptValueId).ToArray(),ct);
            await messageBus.PublishAsync(
                new InventoryStockChangedDomainEvent(shopId,committed.Select(receipt=>receipt.ItemId).Distinct().ToArray()));
        }
        return outcome;
    }
    private async Task<IReadOnlyList<PurchaseReceiptValue>> LoadReversalTargetAsync(
        Guid shopId,
        Guid targetId,
        bool group,
        CancellationToken token) =>
        group
            ?await returns.GetGroupReceiptsAsync(shopId,targetId,token)
            :await returns.GetReceiptsAsync(shopId,[targetId],token);
    private static bool MatchesRequestedLots(
        IReadOnlyList<PurchaseReceiptValue> initial,
        IReadOnlyList<ReceiptReversalLineInput> lines) =>
        initial.Count==lines.Count
        && initial.Select(x=>x.Id).Order().SequenceEqual(lines.Select(x=>x.ReceiptValueId).Order());
    private static ErrorOr<Guid> ResolveSourceSupplier(IReadOnlyList<PurchaseReceiptValue> initial)
    {
        var supplierId=initial[0].SupplierId;
        if(supplierId is null || initial.Any(x=>x.SupplierId!=supplierId))
            return Invalid("SupplierInvalid","All lots must belong to one source supplier.");
        return supplierId.Value;
    }
    private async Task<ErrorOr<Success>> FenceReversalPeriodsAsync(
        Guid shopId,
        Shop shop,
        IReadOnlyList<PurchaseReceiptValue> initial,
        CancellationToken token)
    {
        foreach(
            var date in initial
                .Select(x=>clock.GetBusinessDate(x.ReceivedAt,shop.BusinessTimeZoneId))
                .Append(clock.Today(shop.BusinessTimeZoneId))
                .Distinct()
                .Order())
        {
            var fence=await periods.AuthorizeForWriteAsync(shopId,date,token);
            if(fence.IsError) return fence.Errors;
        }
        return Result.Success;
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
