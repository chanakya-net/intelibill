using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>Stages an offline sale's stock deductions, the sale itself, its review cases and its due entry, in that order.</summary>
public sealed class OfflineSaleStockAndDuePoster(
    SaleStockOutPoster stockOutPoster,
    ISaleRepository saleRepository,
    IOfflineSaleVarianceAnalyzer varianceAnalyzer,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    internal async Task StageSaleStockAndDueAsync(
        OfflineSalePostingRequest request,
        IReadOnlyList<ValidatedSaleLine> vLines,
        List<OfflineStockConsumption> consumption,
        string invoiceNumber,
        Sale saleEntity,
        List<ReconciliationIssue> pendingIssues,
        CustomerLedgerEntry? dueEntry,
        CancellationToken cancellationToken)
    {
        await DeductStockAsync(request, vLines, consumption, invoiceNumber, cancellationToken);

        await saleRepository.AddAsync(saleEntity, cancellationToken);

        await PersistReviewIssuesAsync(request, saleEntity, invoiceNumber, pendingIssues, cancellationToken);

        if (dueEntry is not null)
            await customerLedgerEntryRepository.AddAsync(dueEntry, cancellationToken);
    }

    private async Task DeductStockAsync(
        OfflineSalePostingRequest request,
        IReadOnlyList<ValidatedSaleLine> vLines,
        List<OfflineStockConsumption> consumption,
        string invoiceNumber,
        CancellationToken cancellationToken)
    {
        var recordedBy = request.RecordedByUserId;
        for (var i = 0; i < vLines.Count; i++)
        {
            var consumed = consumption[i].ConsumedQuantity;
            if (consumed <= 0) continue;
            if (vLines[i].LineType == SaleLineType.Service) continue;
            var stockOut = await stockOutPoster.PostStockOutAsync(
                request.ShopId,
                vLines[i].Item!.Id,
                vLines[i].Batch!,
                vLines[i].Inventory!,
                consumed,
                invoiceNumber,
                request.Sale.SoldAt,
                request.AttributedUserId,
                recordedBy,
                cancellationToken);
            if (stockOut.IsError) throw new InvalidOperationException(stockOut.FirstError.Description);
        }
    }

    private async Task PersistReviewIssuesAsync(
        OfflineSalePostingRequest request,
        Sale saleEntity,
        string invoiceNumber,
        List<ReconciliationIssue> pendingIssues,
        CancellationToken cancellationToken)
    {
        foreach (var issue in pendingIssues)
            issue.AttachEvidence(invoiceNumber, request.OfflineSubmissionId);
        await varianceAnalyzer.PersistReviewIssuesAsync(saleEntity.Id, pendingIssues, cancellationToken);
    }
}
