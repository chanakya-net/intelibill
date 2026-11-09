using System.Linq.Expressions;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.DTOs;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Exports;

internal sealed partial class ExportReadSnapshot
{
    private readonly Dictionary<Guid, Sale> _sales = [];

    public async Task<ExportPage> ReadPageAsync(Guid shopId, ExportWindow window, string level, ExportCursor? cursor, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > ExportReadLimits.PageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var stream = cursor?.Stream ?? ExportSource.Sales;
        return stream switch
        {
            ExportSource.Sales => await ReadSalesAsync(shopId, window, level, cursor, pageSize, cancellationToken),
            ExportSource.Returns => await ReadReturnsAsync(shopId, window, cursor, pageSize, cancellationToken),
            ExportSource.Cancellations => await ReadCancellationsAsync(shopId, window, cursor, pageSize, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(cursor))
        };
    }

    private async Task<ExportPage> ReadSalesAsync(Guid shopId, ExportWindow window, string level, ExportCursor? cursor, int size, CancellationToken ct)
    {
        var query = PeriodSales(shopId, window);
        if (cursor?.Timestamp is { } timestamp && cursor.DocumentId is { } id)
            query = query.Where(s => s.SoldAt < timestamp || (s.SoldAt == timestamp && s.Id.CompareTo(id) < 0));
        var sales = await query.OrderByDescending(s => s.SoldAt).ThenByDescending(s => s.Id).Take(size).ToListAsync(ct);
        var ids = sales.Select(s => s.Id).ToArray();
        await PopulateSaleItemsAsync(shopId, sales, ct);
        foreach (var sale in sales) _sales[sale.Id] = sale;
        var cancellationFacts = await db.SaleCancellations.AsNoTracking().Where(c => c.ShopId == shopId && ids.Contains(c.SaleId))
            .Select(c => new { c.SaleId, c.ReferenceNumber, c.BusinessDate }).Take(size).ToListAsync(ct);
        var cancellations = cancellationFacts.ToDictionary(c => c.SaleId);
        var summaryRows = sales.Select(s => SalesExportRowProjector.BuildSummaryRow(s, []) with
        {
            Status = s.Status.ToString(), CancellationReference = cancellations.GetValueOrDefault(s.Id)?.ReferenceNumber,
            CancellationBusinessDate = cancellations.GetValueOrDefault(s.Id)?.BusinessDate
        }).ToList();
        List<SalesExportLineItemRowDto> lines = [];
        if (level.Equals(SalesExportLevel.LineItems, StringComparison.OrdinalIgnoreCase) && ids.Length > 0)
        {
            // Group the quantities in SQL; read only the bounded labels needed for each line,
            // rather than materializing the current physical return entity histories.
            var physicalQuery = from returned in db.SaleReturns.AsNoTracking()
                join item in db.SaleReturnItems.AsNoTracking() on returned.Id equals item.SaleReturnId
                where returned.ShopId == shopId && item.ShopId == shopId && ids.Contains(returned.SaleId) && !returned.IsVoided
                group item by new { returned.Id, returned.ReturnNumber, returned.ProcessedAt, item.SaleItemId } into grouped
                select new { ReturnId = grouped.Key.Id, grouped.Key.SaleItemId, grouped.Key.ReturnNumber, grouped.Key.ProcessedAt, Quantity = grouped.Sum(i => i.Quantity) };
            var physical = await ReadRecordsAsync(physicalQuery, r => r.ReturnId, r => r.SaleItemId, ct);
            var byItem = physical.GroupBy(r => r.SaleItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ProcessedAt).ThenByDescending(r => r.ReturnId).ToList());
            var fallbackIds = sales.SelectMany(s => s.Items).Where(i => string.IsNullOrWhiteSpace(i.LineName) && i.ItemId.HasValue).Select(i => i.ItemId!.Value).Distinct().ToArray();
            var items = await ReadChildrenAsync(db.Items.AsNoTracking().Where(i => i.ShopId == shopId && fallbackIds.Contains(i.Id)), i => i.Id, ct);
            var names = items.ToDictionary(i => i.Id, i => i.Name);
            foreach (var sale in sales)
            {
                ct.ThrowIfCancellationRequested();
                var cancellation = cancellations.GetValueOrDefault(sale.Id);
                foreach (var item in sale.Items)
                {
                    var returned = byItem.GetValueOrDefault(item.Id, []);
                    var numbers = string.Join(", ", returned.Select(r => r.ReturnNumber));
                    lines.Add(SalesExportRowProjector.BuildLineItemRow(sale, item, returned.Sum(r => r.Quantity), numbers, names) with
                    {
                        Status = sale.Status.ToString(), CancellationReference = cancellation?.ReferenceNumber, CancellationBusinessDate = cancellation?.BusinessDate
                    });
                }
            }
        }
        var taxes = sales.SelectMany(s => s.Items).GroupBy(i => i.TaxRatePercent)
            .Select(g => new SalesExportTaxBreakupDto(g.Key, g.Sum(i => i.TaxableAmount), g.Sum(i => i.TaxAmount), 0, 0)).ToList();
        var last = sales.LastOrDefault();
        var next = sales.Count == size ? new ExportCursor(ExportSource.Sales, last!.SoldAt, last.Id) : new ExportCursor(ExportSource.Returns);
        return new(summaryRows, lines, taxes, [], [], next, false);
    }

    private async Task<ExportPage> ReadReturnsAsync(Guid shopId, ExportWindow window, ExportCursor? cursor, int size, CancellationToken ct)
    {
        var query = PeriodReturns(shopId, window);
        if (cursor?.Timestamp is { } timestamp && cursor.DocumentId is { } id)
            query = query.Where(r => r.ProcessedAt > timestamp || (r.ProcessedAt == timestamp && r.Id.CompareTo(id) > 0));
        var returns = await query.OrderBy(r => r.ProcessedAt).ThenBy(r => r.Id).Take(size).ToListAsync(ct);
        var ids = returns.Select(r => r.Id).ToArray();
        await PopulateReturnItemsAsync(shopId, returns, ct);
        var settlements = await ReadChildrenAsync(db.SaleReturnSettlements.AsNoTracking().Where(s => s.ShopId == shopId && ids.Contains(s.SaleReturnId)), s => s.SaleReturnId, ct);
        Attach(returns, settlements, nameof(SaleReturn.Settlements), s => s.SaleReturnId);
        var missing = returns.Select(r => r.SaleId).Distinct().Where(id => !_sales.ContainsKey(id)).ToArray();
        var originalSales = await ReadChildrenAsync(db.Sales.AsNoTracking().Where(s => s.ShopId == shopId && missing.Contains(s.Id)), s => s.Id, ct);
        await PopulateSaleItemsAsync(shopId, originalSales, ct);
        foreach (var sale in originalSales) _sales[sale.Id] = sale;
        var creditIds = settlements.Where(s => s.CreditNoteId.HasValue).Select(s => s.CreditNoteId!.Value).Distinct().ToArray();
        var notes = await ReadChildrenAsync(db.CreditNotes.AsNoTracking().Where(n => n.ShopId == shopId && creditIds.Contains(n.Id)), n => n.Id, ct);
        var postings = ReturnAdjustmentProjector.Project(returns, _sales, notes.ToDictionary(n => n.Id, n => n.Code), window.From, window.To);
        var taxes = postings.SelectMany(p => p.TaxComponents).GroupBy(c => c.TaxRatePercent)
            .Select(g => new SalesExportTaxBreakupDto(g.Key, 0, 0, g.Sum(c => c.TaxableAmount), g.Sum(c => c.TaxAmount))).ToList();
        var last = returns.LastOrDefault();
        var next = returns.Count == size ? new ExportCursor(ExportSource.Returns, last!.ProcessedAt, last.Id) : new ExportCursor(ExportSource.Cancellations);
        return new([], [], taxes, postings, [], next, false);
    }

    private async Task<ExportPage> ReadCancellationsAsync(Guid shopId, ExportWindow window, ExportCursor? cursor, int size, CancellationToken ct)
    {
        var query = PeriodCancellations(shopId, window);
        if (cursor?.Timestamp is { } timestamp && cursor.DocumentId is { } id)
            query = query.Where(c => c.CancelledAt > timestamp || (c.CancelledAt == timestamp && c.Id.CompareTo(id) > 0));
        var facts = await query.OrderBy(c => c.CancelledAt).ThenBy(c => c.Id).Take(size).ToListAsync(ct);
        var ids = facts.Select(c => c.Id).ToArray();
        var lines = await ReadChildrenAsync(db.SaleCancellationLines.AsNoTracking().Where(l => l.ShopId == shopId && ids.Contains(l.SaleCancellationId)), l => l.SaleCancellationId, ct);
        Attach(facts, lines, nameof(SaleCancellation.Lines), l => l.SaleCancellationId);
        var adjustments = SaleCancellationAdjustmentReader.Project(facts);
        List<SalesExportTaxBreakupDto> taxes = adjustments.Count == 0 ? [] : [new(0, 0, 0, 0, 0) { CancellationTaxableAmount = adjustments.Sum(c => c.RevenueAmount) }];
        var last = facts.LastOrDefault();
        var complete = facts.Count < size;
        return new([], [], taxes, [], adjustments, complete ? null : new(ExportSource.Cancellations, last!.CancelledAt, last.Id), complete);
    }

    private async Task PopulateSaleItemsAsync(Guid shopId, List<Sale> sales, CancellationToken ct)
    {
        if (sales.Count == 0) return;
        var ids = sales.Select(s => s.Id).ToArray();
        var items = await ReadChildrenAsync(db.SaleItems.AsNoTracking().Where(i => i.ShopId == shopId && ids.Contains(i.SaleId)), i => i.SaleId, ct);
        Attach(sales, items, nameof(Sale.Items), i => i.SaleId);
    }

    private async Task PopulateReturnItemsAsync(Guid shopId, List<SaleReturn> returns, CancellationToken ct)
    {
        if (returns.Count == 0) return;
        var ids = returns.Select(r => r.Id).ToArray();
        var items = await ReadChildrenAsync(db.SaleReturnItems.AsNoTracking().Where(i => i.ShopId == shopId && ids.Contains(i.SaleReturnId)), i => i.SaleReturnId, ct);
        Attach(returns, items, nameof(SaleReturn.Items), i => i.SaleReturnId);
    }

    // EF's mapped collection accessor fills detached read models without attaching/tracking them,
    // and avoids changing the domain's mutation API just for export hydration.
    private void Attach<TParent, TChild>(IReadOnlyCollection<TParent> parents, IReadOnlyCollection<TChild> children, string navigation, Func<TChild, Guid> parentId)
        where TParent : BaseEntity where TChild : BaseEntity
    {
        var byId = parents.ToDictionary(p => p.Id);
        var accessor = db.Model.FindEntityType(typeof(TParent))!.FindNavigation(navigation)!.GetCollectionAccessor()!;
        foreach (var child in children) accessor.Add(byId[parentId(child)], child, forMaterialization: true);
    }

    private static Task<List<T>> ReadChildrenAsync<T>(IQueryable<T> query, Expression<Func<T, Guid>> parentId, CancellationToken ct) where T : BaseEntity =>
        ReadRecordsAsync(query, parentId, x => x.Id, ct);

    private static async Task<List<T>> ReadRecordsAsync<T>(IQueryable<T> query, Expression<Func<T, Guid>> parentId, Expression<Func<T, Guid>> childId, CancellationToken ct)
    {
        List<T> result = [];
        var readParent = parentId.Compile();
        var readChild = childId.Compile();
        Guid? lastParent = null;
        Guid lastId = default;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pageQuery = query;
            if (lastParent is { } parent)
            {
                var parameter = parentId.Parameters[0];
                var parentCompare = Expression.Call(parentId.Body, nameof(Guid.CompareTo), Type.EmptyTypes, Expression.Constant(parent));
                var idExpression = new ReplaceParameter(childId.Parameters[0], parameter).Visit(childId.Body)!;
                var idCompare = Expression.Call(idExpression, nameof(Guid.CompareTo), Type.EmptyTypes, Expression.Constant(lastId));
                var predicate = Expression.OrElse(Expression.GreaterThan(parentCompare, Expression.Constant(0)),
                    Expression.AndAlso(Expression.Equal(parentId.Body, Expression.Constant(parent)), Expression.GreaterThan(idCompare, Expression.Constant(0))));
                pageQuery = pageQuery.Where(Expression.Lambda<Func<T, bool>>(predicate, parameter));
            }
            var page = await pageQuery.OrderBy(parentId).ThenBy(childId).Take(ExportReadLimits.PageSize).ToListAsync(ct);
            result.AddRange(page);
            if (page.Count < ExportReadLimits.PageSize) break;
            lastParent = readParent(page[^1]);
            lastId = readChild(page[^1]);
        }
        return result;
    }
    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }

}
