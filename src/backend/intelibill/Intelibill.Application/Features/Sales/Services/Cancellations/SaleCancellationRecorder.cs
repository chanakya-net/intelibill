using ErrorOr;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Cancellations;

/// <summary>
/// Stages a sale cancellation's records: the customer credit that reverses the sale's due, the cancellation fact,
/// and the sale marked cancelled.
/// </summary>
public sealed class SaleCancellationRecorder(
    ISaleRepository sales,
    ICustomerLedgerEntryRepository ledger,
    ISaleCancellationRepository cancellations)
{
    /// <summary>Adds the due-reversing customer credit (when the sale has a due) and builds the cancellation fact.</summary>
    internal async Task<ErrorOr<SaleCancellation>> StageDueCreditAndRecordAsync(Guid shopId, Sale sale, Guid actorUserId,
        CancelSaleRequest request, BusinessDay day, DateTimeOffset now, IReadOnlyList<SaleCancellationLineInput> lines,
        CancellationToken token)
    {
        var saleId = sale.Id;
        Guid? ledgerId = null;
        if (sale.DueAmount > 0)
        {
            var credit = CustomerLedgerEntry.Create(
                shopId,
                sale.CustomerId!.Value,
                saleId,
                CustomerLedgerEntryType.SaleCancellationCredit,
                sale.DueAmount,
                day.Date,
                request.Reason.Trim(),
                actorUserId);
            if (credit.IsError) return credit.Errors;
            ledgerId = credit.Value.Id;
            await ledger.AddAsync(credit.Value, token);
        }
        var document = sale.InvoiceDocument!;
        var fact = SaleCancellation.Record(
            new SaleCancellationInput(
                shopId,
                saleId,
                actorUserId,
                request.OperationKey,
                request.Reason,
                now,
                day.Date,
                day.TimeZoneId,
                sale.InvoiceNumber,
                sale.BusinessDate,
                sale.TotalAmount,
                sale.TotalAmount,
                0m,
                sale.DueAmount,
                sale.PaidAmount,
                ledgerId,
                document.Seller,
                document.Buyer,
                true,
                request.CashRefundConfirmed,
                lines));
        if (fact.IsError) return fact.Errors;
        return fact.Value;
    }

    /// <summary>Marks the sale cancelled and adds the cancellation fact.</summary>
    internal async Task<ErrorOr<Success>> MarkCancelledAndAddAsync(Sale sale, SaleCancellation fact, CancellationToken token)
    {
        var cancelled = sale.MarkCancelled();
        if (cancelled.IsError) return cancelled.Errors;
        sales.Update(sale);
        await cancellations.AddAsync(fact, token);
        return Result.Success;
    }
}
