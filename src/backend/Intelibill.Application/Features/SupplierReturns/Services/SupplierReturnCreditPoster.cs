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

/// <summary>Posts accepted commercial credit once inside the caller's operation transaction.</summary>
public sealed class SupplierReturnCreditPoster(ISupplierReturnRepository returns, ISupplierLedgerEntryRepository ledger, IBusinessClock clock)
{
    private static Error Invalid(string code,string message) => Error.Validation($"SupplierReturn.{code}",message);
    private static Error Conflict(string code,string message) => Error.Conflict($"SupplierReturn.{code}",message);
    public async Task<ErrorOr<Success>> PostAsync(SupplierReturn document,IReadOnlyList<SupplierReturnCreditLineInput> inputs,Guid actor,string reason,string? reference,Shop shop,IReadOnlyList<PurchaseReceiptValue> rows,CancellationToken ct)
    {
        if(inputs.Count!=document.Lines.Count || inputs.Select(x=>x.ReturnLineId).Distinct().Count()!=inputs.Count || inputs.Any(x=>!document.Lines.Any(l=>l.Id==x.ReturnLineId))) return Invalid("CreditLinesInvalid","Include every shipment line exactly once.");
        if(document.Status==SupplierReturnStatus.CreditAccepted) return Conflict("CreditAlreadyAccepted","Credit was already accepted.");
        var history=await returns.GetByReceiptsAsync(shop.Id,rows.Select(x=>x.Id).ToArray(),ct);
        var now=clock.UtcNow; var date=clock.GetBusinessDate(now,shop.BusinessTimeZoneId);
        var created=SupplierReturnCredit.Create(shop.Id,document.Id,actor,now,date,shop.BusinessTimeZoneId,reason,reference); if(created.IsError) return created.Errors;
        var credit=created.Value;
        foreach(var input in inputs)
        {
            var line=document.Lines.Single(x=>x.Id==input.ReturnLineId); var receipt=rows.Single(x=>x.Id==line.ReceiptValueId);
            var prior=history.Where(x=>x.Credit is not null).SelectMany(x=>x.Credit!.Lines).Where(x=>x.ReceiptValueId==receipt.Id).Sum(x=>x.AcceptedAmount);
            if(input.AcceptedAmount<0 || decimal.Round(input.AcceptedAmount,2)!=input.AcceptedAmount || input.AcceptedAmount>receipt.EffectiveTotalCost-prior) return Invalid("CreditLimitExceeded","Accepted credit must not exceed the receipt's remaining effective cost.");
            SupplierLedgerEntry? entry=null;
            if(input.AcceptedAmount>0)
            {
                var result=SupplierLedgerEntry.Create(shop.Id,document.SupplierId,line.InventoryBatchId,SupplierLedgerEntryType.SupplierReturnCredit,-input.AcceptedAmount,date,document.ReturnNumber,actor); if(result.IsError) return result.Errors;
                entry=result.Value; await ledger.AddAsync(entry,ct);
            }
            var fact=SupplierReturnCreditLine.Create(shop.Id,credit.Id,line.Id,receipt.Id,input.AcceptedAmount,entry?.Id); if(fact.IsError) return fact.Errors;
            credit.AddLine(fact.Value);
        }
        var accepted=document.AcceptCredit(credit); if(accepted.IsError) return accepted.Errors;
        await returns.AddCreditAsync(credit,ct);
        return Result.Success;
    }
}
