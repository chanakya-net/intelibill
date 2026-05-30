using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using SaleHistoryFilter = Intelibill.Domain.Interfaces.Repositories.SaleHistoryFilter;
using SaleHistoryReadModel = Intelibill.Domain.Interfaces.Repositories.SaleHistoryReadModel;
using SalesHistorySummaryReadModel = Intelibill.Domain.Interfaces.Repositories.SalesHistorySummaryReadModel;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SaleRepository : RepositoryBase<Sale>, ISaleRepository
{
    private readonly ApplicationDbContext _context;

    public SaleRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    private static DateTime ToUtcStart(DateOnly utcDate) =>
        DateTime.SpecifyKind(utcDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    public async Task<Sale?> GetByIdAsync(Guid saleId, Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.ShopId == shopId, cancellationToken);

    public async Task<Sale?> GetByIdempotencyKeyAsync(Guid shopId, Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s =>
                s.ShopId == shopId
                && s.ActorUserId == actorUserId
                && s.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<Sale?> GetByClientSaleIdAsync(
        Guid shopId,
        string deviceId,
        string clientSaleId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s =>
                s.ShopId == shopId
                && s.DeviceId == deviceId
                && s.ClientSaleId == clientSaleId,
                cancellationToken);

    public async Task<Sale?> GetByInvoiceNumberAsync(Guid shopId, string invoiceNumber, CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.ShopId == shopId && s.InvoiceNumber == invoiceNumber, cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .Where(s => s.ShopId == shopId)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByIdsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> saleIds,
        CancellationToken cancellationToken = default) =>
        saleIds.Count == 0
            ? []
            : await DbSet
                .Include(s => s.Items)
                .Where(s => s.ShopId == shopId && saleIds.Contains(s.Id))
                .OrderByDescending(s => s.SoldAt)
                .ThenByDescending(s => s.Id)
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByCustomerAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .Where(s => s.ShopId == shopId && s.CustomerId == customerId)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByShopAndDateAsync(Guid shopId, DateOnly reportingDay, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .Where(s => s.ShopId == shopId
                && s.SoldAt >= ToUtcStart(reportingDay)
                && s.SoldAt < ToUtcStart(reportingDay.AddDays(1)))
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .Where(s => s.ShopId == shopId
                && s.SoldAt >= ToUtcStart(startDate)
                && s.SoldAt < ToUtcStart(endDate.AddDays(1)))
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<SaleHistoryReadModel> Items, int TotalCount)> GetHistoryAsync(
        SaleHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var start = ToUtcStart(filter.StartDate);
        var exclusiveEnd = ToUtcStart(filter.EndDate.AddDays(1));

        var query = _context.Sales
            .AsNoTracking()
            .Where(s =>
                s.ShopId == filter.ShopId
                && s.SoldAt >= start
                && s.SoldAt < exclusiveEnd);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var amountMatch = decimal.TryParse(term, out var parsedAmount);
            query = query.Where(s =>
                EF.Functions.ILike(s.InvoiceNumber, $"%{term}%")
                || EF.Functions.ILike(s.CustomerName!, $"%{term}%")
                || EF.Functions.ILike(s.CustomerPhone!, $"%{term}%")
                || _context.SaleReturns.Any(r =>
                    r.ShopId == filter.ShopId
                    && r.SaleId == s.Id
                    && !r.IsVoided
                    && EF.Functions.ILike(r.ReturnNumber, $"%{term}%"))
                || (amountMatch && s.TotalAmount == parsedAmount));
        }

        var status = NormalizeStatus(filter.Status);
        if (status is SaleHistoryStatus.Refunded)
        {
            query = query.Where(s =>
                _context.SaleReturns.Any(r => r.ShopId == filter.ShopId && r.SaleId == s.Id && !r.IsVoided));
        }
        else if (status is SaleHistoryStatus.Paid)
        {
            query = query.Where(s =>
                !_context.SaleReturns.Any(r => r.ShopId == filter.ShopId && r.SaleId == s.Id && !r.IsVoided)
                && s.DueAmount == 0m);
        }
        else if (status is SaleHistoryStatus.PartiallyPaid)
        {
            query = query.Where(s =>
                !_context.SaleReturns.Any(r => r.ShopId == filter.ShopId && r.SaleId == s.Id && !r.IsVoided)
                && s.DueAmount > 0m);
        }
        else if (status is SaleHistoryStatus.Unknown)
        {
            query = query.Where(s =>
                !_context.SaleReturns.Any(r => r.ShopId == filter.ShopId && r.SaleId == s.Id && !r.IsVoided)
                && s.DueAmount < 0m);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = await query
            .OrderByDescending(s => s.SoldAt)
            .ThenByDescending(s => s.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.CustomerId,
                s.PaymentMethod,
                s.SoldAt,
                s.PaidAmount,
                s.DueAmount,
                s.TotalBeforeDiscount,
                s.TotalDiscountAmount,
                s.TotalAmount,
                s.TotalTaxAmount,
                s.CustomerName,
                s.CustomerPhone,
                ItemCount = _context.SaleItems.Count(i => i.SaleId == s.Id),
            })
            .ToListAsync(cancellationToken);

        var saleIds = page.Select(x => x.Id).ToList();
        var returnsBySaleId = await _context.SaleReturns
            .AsNoTracking()
            .Where(r => r.ShopId == filter.ShopId && saleIds.Contains(r.SaleId) && !r.IsVoided)
            .Select(r => new { r.SaleId, r.ReturnNumber, r.TotalRefundAmount, r.DueReductionAmount })
            .ToListAsync(cancellationToken);

        var lookup = returnsBySaleId
            .GroupBy(x => x.SaleId)
            .ToDictionary(
                g => g.Key,
                g => (
                    ReturnNumbers: (IReadOnlyList<string>)g.Select(x => x.ReturnNumber).ToList(),
                    RefundAmount: g.Sum(x => x.TotalRefundAmount),
                    DueReductionAmount: g.Sum(x => x.DueReductionAmount)));

        var items = page
            .Select(s =>
            {
                var hasActiveReturn = lookup.TryGetValue(s.Id, out var returnData);
                return new SaleHistoryReadModel(
                    s.Id,
                    s.InvoiceNumber,
                    s.CustomerId,
                    s.PaymentMethod,
                    s.SoldAt,
                    s.PaidAmount,
                    s.DueAmount,
                    s.TotalBeforeDiscount,
                    s.TotalDiscountAmount,
                    s.TotalAmount,
                    s.TotalTaxAmount,
                    s.CustomerName,
                    s.CustomerPhone,
                    s.ItemCount,
                    hasActiveReturn ? returnData.ReturnNumbers : [],
                    DeriveStatus(s.DueAmount, hasActiveReturn),
                    hasActiveReturn ? returnData.RefundAmount : 0m,
                    hasActiveReturn ? returnData.DueReductionAmount : 0m);
            })
            .ToList();

        return (items, totalCount);
    }

    private static string DeriveStatus(decimal dueAmount, bool hasActiveReturn)
    {
        if (hasActiveReturn)
            return "refunded";
        if (dueAmount > 0m)
            return "partiallyPaid";
        if (dueAmount == 0m)
            return "paid";
        return "unknown";
    }

    public async Task<SalesHistorySummaryReadModel> GetHistorySummaryAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var start = ToUtcStart(startDate);
        var exclusiveEnd = ToUtcStart(endDate.AddDays(1));

        var salesInRange = _context.Sales
            .AsNoTracking()
            .Where(s =>
                s.ShopId == shopId
                && s.SoldAt >= start
                && s.SoldAt < exclusiveEnd);

        var invoiceCount = await salesInRange.CountAsync(cancellationToken);
        var grossSales = await salesInRange.SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;
        var saleIdsInRange = salesInRange.Select(s => s.Id);

        var refundAmount = await _context.SaleReturns
            .AsNoTracking()
            .Where(r =>
                r.ShopId == shopId
                && !r.IsVoided
                && saleIdsInRange.Contains(r.SaleId))
            .SumAsync(r => (decimal?)r.TotalRefundAmount, cancellationToken) ?? 0m;

        return new SalesHistorySummaryReadModel(
            PeriodSales: grossSales - refundAmount,
            InvoiceCount: invoiceCount,
            RefundAmount: refundAmount);
    }

    private enum SaleHistoryStatus
    {
        Refunded,
        Paid,
        PartiallyPaid,
        Unknown,
    }

    private static SaleHistoryStatus? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        return status.Trim().ToLowerInvariant() switch
        {
            "refunded" or "returned" => SaleHistoryStatus.Refunded,
            "paid" => SaleHistoryStatus.Paid,
            "partiallypaid" => SaleHistoryStatus.PartiallyPaid,
            "unknown" => SaleHistoryStatus.Unknown,
            _ => null,
        };
    }
}
