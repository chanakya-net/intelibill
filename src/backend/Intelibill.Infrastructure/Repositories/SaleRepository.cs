using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DashboardLatestSaleReadModel = Intelibill.Domain.Interfaces.Repositories.DashboardLatestSaleReadModel;
using SaleHistoryFilter = Intelibill.Domain.Interfaces.Repositories.SaleHistoryFilter;
using SaleHistoryReadModel = Intelibill.Domain.Interfaces.Repositories.SaleHistoryReadModel;
using SalesHistorySummaryReadModel = Intelibill.Domain.Interfaces.Repositories.SalesHistorySummaryReadModel;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SaleRepository : RepositoryBase<Sale>, ISaleRepository
{
    private const string ReturnMutationLockTimeout = "5s";

    private readonly ApplicationDbContext _context;

    public SaleRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Sale?> GetByIdAsync(Guid saleId, Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.ShopId == shopId, cancellationToken);

    public async Task<RowLockOutcome> LockForReturnMutationAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsNpgsql())
        {
            return await DbSet.AnyAsync(s => s.Id == saleId && s.ShopId == shopId, cancellationToken)
                ? RowLockOutcome.Acquired
                : RowLockOutcome.NotFound;
        }

        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The sale return lock must be taken inside a transaction.");

        // The budget covers every lock the transaction takes from here on: the customer and sale
        // below, the credit notes and the batch rows written at save. A lock not granted in time
        // fails its statement with 55P03, which the caller answers as a retryable conflict.
        await _context.Database.ExecuteSqlAsync(
            $"SELECT set_config('lock_timeout', {ReturnMutationLockTimeout}, true)",
            cancellationToken);

        // The sale's customer never changes, so it is read before any lock. A due reduction is
        // capped by the customer's balance across all of their sales, so returns on two sales of
        // one customer must also queue: the customer row is locked first, then the sale.
        var customerIds = await _context.Database
            .SqlQuery<Guid?>($"SELECT customer_id AS \"Value\" FROM sales WHERE id = {saleId} AND shop_id = {shopId}")
            .ToListAsync(cancellationToken);
        if (customerIds.Count == 0)
            return RowLockOutcome.NotFound;

        List<Guid> locked;
        try
        {
            if (customerIds[0] is { } customerId)
            {
                _ = await _context.Database
                    .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM customers WHERE id = {customerId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                    .ToListAsync(cancellationToken);
            }

            // NO KEY UPDATE, not UPDATE: every return, void and credit-note decision on the sale takes
            // this same lock, so they exclude one another, while inserts that merely reference the
            // sale or customer (their key-share check) are not made to queue behind it.
            locked = await _context.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM sales WHERE id = {saleId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                .ToListAsync(cancellationToken);
        }
        // Npgsql counts a lock timeout as transient, so the execution strategy hands it back wrapped.
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // The statement failed, so the transaction is now aborted; the caller rolls it back.
            return RowLockOutcome.TimedOut;
        }

        return locked.Count == 0 ? RowLockOutcome.NotFound : RowLockOutcome.Acquired;
    }

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
                && s.BusinessDate == reportingDay)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .Where(s => s.ShopId == shopId
                && s.BusinessDate >= startDate
                && s.BusinessDate <= endDate)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DashboardLatestSaleReadModel>> GetLatestDashboardSalesAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(s => s.ShopId == shopId)
            .OrderByDescending(s => s.SoldAt)
            .ThenByDescending(s => s.Id)
            .Take(5)
            .Select(s => new DashboardLatestSaleReadModel(
                s.Id,
                s.InvoiceNumber,
                string.IsNullOrWhiteSpace(s.CustomerName) ? "Walk-in Customer" : s.CustomerName,
                s.SoldAt,
                s.TotalAmount))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTrendBucketReadModel>> GetDailySalesTrendAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        var salesByDay = await DbSet
            .AsNoTracking()
            .Where(s =>
                s.ShopId == shopId
                && s.BusinessDate >= startDate
                && s.BusinessDate <= endDate)
            .Select(s => new { s.BusinessDate, s.TotalAmount })
            .ToListAsync(cancellationToken);

        // A return offsets sales on its own business date even once voided; the void takes it back
        // on the date it happened (the same reading the exports and the P&L use).
        var returnRows = await _context.SaleReturns
            .AsNoTracking()
            .Where(r =>
                r.ShopId == shopId
                && ((r.BusinessDate >= startDate && r.BusinessDate <= endDate)
                    || (r.IsVoided
                        && r.VoidedBusinessDate >= startDate
                        && r.VoidedBusinessDate <= endDate)))
            .Select(r => new { r.BusinessDate, r.IsVoided, r.VoidedBusinessDate, r.TotalRefundAmount })
            .ToListAsync(cancellationToken);

        var grossSalesByDay = salesByDay
            .GroupBy(s => s.BusinessDate)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalAmount));

        var returnOffsetsByDay = new Dictionary<DateOnly, decimal>();
        foreach (var row in returnRows)
        {
            if (row.BusinessDate >= startDate && row.BusinessDate <= endDate)
            {
                returnOffsetsByDay[row.BusinessDate] = returnOffsetsByDay.GetValueOrDefault(row.BusinessDate) + row.TotalRefundAmount;
            }

            if (row.IsVoided
                && row.VoidedBusinessDate is { } voidedOn
                && voidedOn >= startDate
                && voidedOn <= endDate)
            {
                returnOffsetsByDay[voidedOn] = returnOffsetsByDay.GetValueOrDefault(voidedOn) - row.TotalRefundAmount;
            }
        }

        return grossSalesByDay.Keys
            .Concat(returnOffsetsByDay.Keys)
            .Distinct()
            .OrderBy(date => date)
            .Select(date => new SalesTrendBucketReadModel(
                date,
                grossSalesByDay.GetValueOrDefault(date),
                returnOffsetsByDay.GetValueOrDefault(date)))
            .ToList();
    }

    public async Task<(IReadOnlyList<SaleHistoryReadModel> Items, int TotalCount)> GetHistoryAsync(
        SaleHistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Sales
            .AsNoTracking()
            .Where(s =>
                s.ShopId == filter.ShopId
                && s.BusinessDate >= filter.StartDate
                && s.BusinessDate <= filter.EndDate);

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
                s.CreditNoteAppliedAmount,
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
                    hasActiveReturn ? returnData.DueReductionAmount : 0m,
                    s.CreditNoteAppliedAmount);
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
        var salesInRange = _context.Sales
            .AsNoTracking()
            .Where(s =>
                s.ShopId == shopId
                && s.BusinessDate >= startDate
                && s.BusinessDate <= endDate);

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

    public async Task<IReadOnlyDictionary<Guid, CustomerSalesMetricsReadModel>> GetCustomerSalesMetricsAsync(
        Guid shopId,
        IReadOnlyCollection<Guid> customerIds,
        UtcDateWindow month,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, CustomerSalesMetricsReadModel>();
        }

        var startOffset = month.StartInclusive;
        var endOffset = month.EndExclusive;

        var metricsList = await DbSet
            .AsNoTracking()
            .Where(s => s.ShopId == shopId
                && s.CustomerId != null
                && customerIds.Contains(s.CustomerId.Value))
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new CustomerSalesMetricsReadModel(
                g.Key,
                g.Count(),
                g.Sum(s => s.TotalAmount),
                g.Sum(s => s.SoldAt >= startOffset && s.SoldAt < endOffset ? s.TotalAmount : 0m)
            ))
            .ToListAsync(cancellationToken);

        return metricsList.ToDictionary(m => m.CustomerId);
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
