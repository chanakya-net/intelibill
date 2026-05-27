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
            query = query.Where(s =>
                EF.Functions.ILike(s.InvoiceNumber, $"%{term}%")
                || EF.Functions.ILike(s.CustomerName!, $"%{term}%")
                || EF.Functions.ILike(s.CustomerPhone!, $"%{term}%"));
        }

        var status = NormalizeStatus(filter.Status);
        if (status is SaleHistoryStatus.Returned)
        {
            query = query.Where(s =>
                _context.SaleReturns.Any(r => r.ShopId == filter.ShopId && r.SaleId == s.Id && !r.IsVoided));
        }
        else if (status is SaleHistoryStatus.NotReturned)
        {
            query = query.Where(s =>
                !_context.SaleReturns.Any(r => r.ShopId == filter.ShopId && r.SaleId == s.Id && !r.IsVoided));
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
        var returnNumbersBySaleId = await _context.SaleReturns
            .AsNoTracking()
            .Where(r => r.ShopId == filter.ShopId && saleIds.Contains(r.SaleId) && !r.IsVoided)
            .Select(r => new { r.SaleId, r.ReturnNumber })
            .ToListAsync(cancellationToken);

        var lookup = returnNumbersBySaleId
            .GroupBy(x => x.SaleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.ReturnNumber).ToList());

        var items = page
            .Select(s => new SaleHistoryReadModel(
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
                lookup.GetValueOrDefault(s.Id, [])))
            .ToList();

        return (items, totalCount);
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
        Returned,
        NotReturned,
    }

    private static SaleHistoryStatus? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return status.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "returned" => SaleHistoryStatus.Returned,
            "refunded" => SaleHistoryStatus.Returned,
            "not-returned" => SaleHistoryStatus.NotReturned,
            "notrefunded" => SaleHistoryStatus.NotReturned,
            "not-refunded" => SaleHistoryStatus.NotReturned,
            _ => null,
        };
    }
}
