using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Exports;

internal sealed partial class ExportReadSnapshot
{
    private IQueryable<Sale> PeriodSales(Guid shopId, ExportWindow window) => db.Sales.AsNoTracking()
        .Where(s => s.ShopId == shopId && s.BusinessDate >= window.From && s.BusinessDate <= window.To);

    private IQueryable<SaleReturn> PeriodReturns(Guid shopId, ExportWindow window) => db.SaleReturns.AsNoTracking()
        .Where(r => r.ShopId == shopId && ((r.BusinessDate >= window.From && r.BusinessDate <= window.To)
            || (r.IsVoided && r.VoidedAt != null && r.VoidedBusinessDate >= window.From && r.VoidedBusinessDate <= window.To)));

    private IQueryable<SaleCancellation> PeriodCancellations(Guid shopId, ExportWindow window) => db.SaleCancellations.AsNoTracking()
        .Where(c => c.ShopId == shopId && c.BusinessDate >= window.From && c.BusinessDate <= window.To);

    public async Task<ExportCounts> CountAsync(Guid shopId, ExportWindow window, string level, string format, CancellationToken cancellationToken)
    {
        var sales = PeriodSales(shopId, window);
        var returns = PeriodReturns(shopId, window);
        var cancellations = PeriodCancellations(shopId, window);
        var saleIds = sales.Select(s => s.Id);
        var returnIds = returns.Select(r => r.Id);
        var cancelIds = cancellations.Select(c => c.Id);
        var lines = db.SaleItems.Where(i => i.ShopId == shopId && saleIds.Contains(i.SaleId));
        var isLines = level.Equals(SalesExportLevel.LineItems, StringComparison.OrdinalIgnoreCase);
        var isXml = format.Equals(SalesExportFormat.TallyXml, StringComparison.OrdinalIgnoreCase);
        var saleCount = await sales.LongCountAsync(cancellationToken);
        var lineCount = await lines.LongCountAsync(cancellationToken);
        var postings = checked(await returns.LongCountAsync(r => r.BusinessDate >= window.From && r.BusinessDate <= window.To, cancellationToken)
            + await returns.LongCountAsync(r => r.IsVoided && r.VoidedAt != null && r.VoidedBusinessDate >= window.From && r.VoidedBusinessDate <= window.To, cancellationToken));
        var cancellationCount = await cancellations.LongCountAsync(cancellationToken);
        var cancellationLines = await db.SaleCancellationLines.LongCountAsync(l => l.ShopId == shopId && cancelIds.Contains(l.SaleCancellationId), cancellationToken);
        long output;
        checked
        {
            output = (isLines ? lineCount : saleCount) + postings + cancellationCount + cancellationLines;
            if (isXml)
            {
                var emptySales = await sales.LongCountAsync(s => !db.SaleItems.Any(i => i.ShopId == shopId && i.SaleId == s.Id), cancellationToken);
                var returnLines = await returns.Select(r => (long)r.Items.Count(i => i.ShopId == shopId) *
                    ((r.BusinessDate >= window.From && r.BusinessDate <= window.To ? 1 : 0)
                    + (r.IsVoided && r.VoidedAt != null && r.VoidedBusinessDate >= window.From && r.VoidedBusinessDate <= window.To ? 1 : 0)))
                    .SumAsync(cancellationToken);
                output = lineCount + emptySales + postings + returnLines + cancellationCount + cancellationLines;
            }
            else if (format.Equals(SalesExportFormat.Xlsx, StringComparison.OrdinalIgnoreCase) && !isLines)
            {
                // The summary worksheet adds one aggregate for returns on earlier invoices and one row per cancellation.
                var earlier = await returns.AnyAsync(r => !saleIds.Contains(r.SaleId), cancellationToken);
                output += cancellationCount + (earlier ? 1 : 0);
            }
        }

        // SQL subqueries deduplicate overlapping dependencies without loading IDs or any detail graph.
        var neededSales = db.Sales.Where(s => s.ShopId == shopId && (saleIds.Contains(s.Id) || returns.Select(r => r.SaleId).Contains(s.Id)));
        var neededSaleIds = neededSales.Select(s => s.Id);
        var neededReturns = db.SaleReturns.Where(r => r.ShopId == shopId && (returnIds.Contains(r.Id) || (isLines && !r.IsVoided && saleIds.Contains(r.SaleId))));
        var neededReturnIds = neededReturns.Select(r => r.Id);
        var neededCancellations = db.SaleCancellations.Where(c => c.ShopId == shopId && (cancelIds.Contains(c.Id) || saleIds.Contains(c.SaleId)));
        var fallbackItemIds = db.SaleItems.Where(i => i.ShopId == shopId && saleIds.Contains(i.SaleId) && i.ItemId != null && (i.LineName == null || i.LineName.Trim() == "")).Select(i => i.ItemId);
        long source;
        checked
        {
            source = await neededSales.LongCountAsync(cancellationToken)
                + await db.SaleItems.LongCountAsync(i => i.ShopId == shopId && neededSaleIds.Contains(i.SaleId), cancellationToken)
                + await neededReturns.LongCountAsync(cancellationToken)
                + await db.SaleReturnItems.LongCountAsync(i => i.ShopId == shopId && neededReturnIds.Contains(i.SaleReturnId), cancellationToken)
                + await db.SaleReturnSettlements.LongCountAsync(s => s.ShopId == shopId && returnIds.Contains(s.SaleReturnId), cancellationToken)
                + await neededCancellations.LongCountAsync(cancellationToken)
                + cancellationLines
                + await db.CreditNotes.LongCountAsync(n => n.ShopId == shopId && db.SaleReturnSettlements.Any(s => s.ShopId == shopId && returnIds.Contains(s.SaleReturnId) && s.CreditNoteId == n.Id), cancellationToken)
                + (isLines ? await db.Items.LongCountAsync(i => i.ShopId == shopId && fallbackItemIds.Contains(i.Id), cancellationToken) : 0);
        }
        return new(output, source);
    }
}
