using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Exports.Accounting;

/// <summary>
/// Loads every return posting in a period, with the sales they belong to (also those sold outside
/// the period) and the codes of the credit notes they issued, and projects them. The single source
/// of return figures for sales exports and the P&amp;L.
/// </summary>
public sealed class ReturnAdjustmentReader(
    ISaleReturnRepository saleReturnRepository,
    ISaleRepository saleRepository,
    ICreditNoteRepository creditNoteRepository)
{
    public async Task<IReadOnlyList<ReturnAdjustmentEntry>> ReadAsync(
        Guid shopId,
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<Sale> loadedSales,
        CancellationToken cancellationToken)
    {
        var returns = await saleReturnRepository.GetPostedInRangeAsync(shopId, from, to, cancellationToken);
        if (returns.Count == 0)
            return [];

        var salesById = loadedSales
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var missingSaleIds = returns
            .Select(r => r.SaleId)
            .Distinct()
            .Where(id => !salesById.ContainsKey(id))
            .ToArray();
        if (missingSaleIds.Length > 0)
        {
            foreach (var sale in await saleRepository.GetByIdsAsync(shopId, missingSaleIds, cancellationToken))
                salesById[sale.Id] = sale;
        }

        var returnIdsWithStoreCredit = returns
            .Where(r => r.Settlements.Any(s => s.Kind == ReturnSettlementKind.StoreCreditIssued))
            .Select(r => r.Id)
            .ToArray();
        var creditNoteCodes = returnIdsWithStoreCredit.Length == 0
            ? new Dictionary<Guid, string>()
            : (await creditNoteRepository.GetByReturnIdsAsync(shopId, returnIdsWithStoreCredit, cancellationToken))
                .ToDictionary(note => note.Id, note => note.Code);

        return ReturnAdjustmentProjector.Project(returns, salesById, creditNoteCodes, from, to);
    }
}
