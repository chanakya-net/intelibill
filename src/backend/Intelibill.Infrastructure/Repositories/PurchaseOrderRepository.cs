using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PurchaseOrderRepository(ApplicationDbContext context)
    : RepositoryBase<PurchaseOrder>(context), IPurchaseOrderRepository
{
    public async Task<PurchaseOrder?> GetByShopAndIdAsync(
        Guid shopId,
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(po => po.Lines)
            .FirstOrDefaultAsync(po => po.ShopId == shopId && po.Id == purchaseOrderId, cancellationToken);

    public async Task<PurchaseOrder?> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(po => po.Lines)
            .FirstOrDefaultAsync(po => po.Id == purchaseOrderId, cancellationToken);

    public async Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetByShopAsync(
        PurchaseOrderListFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(po => po.Lines)
            .Where(po => po.ShopId == filter.ShopId)
            .AsQueryable();

        if (filter.Status is not null)
            query = query.Where(po => po.Status == filter.Status);

        if (filter.OrderDateFrom is not null)
        {
            var start = ToUtcStart(filter.OrderDateFrom.Value);
            query = query.Where(po => po.CreatedAt >= start);
        }

        if (filter.OrderDateTo is not null)
        {
            var endExclusive = ToUtcStart(filter.OrderDateTo.Value.AddDays(1));
            query = query.Where(po => po.CreatedAt < endExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(po =>
                EF.Functions.ILike(po.PurchaseOrderNumber, $"%{term}%")
                || (po.SupplierName != null && EF.Functions.ILike(po.SupplierName, $"%{term}%"))
                || (po.SupplierReference != null && EF.Functions.ILike(po.SupplierReference, $"%{term}%"))
                || po.Lines.Any(line => EF.Functions.ILike(line.Description, $"%{term}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(po => po.Status == PurchaseOrderStatus.Draft ? 0 : 99)
            .ThenByDescending(po => po.CreatedAt)
            .ThenByDescending(po => po.Id)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    private static DateTimeOffset ToUtcStart(DateOnly date) =>
        new(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
}
