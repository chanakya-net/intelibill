using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SaleReturnRepository(ApplicationDbContext context)
    : RepositoryBase<SaleReturn>(context), ISaleReturnRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<SaleReturn?> GetByIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet.FirstOrDefaultAsync(r => r.ShopId == shopId && r.Id == saleReturnId, cancellationToken);

    public async Task<Guid?> GetSaleIdAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(r => r.ShopId == shopId && r.Id == saleReturnId)
            .Select(r => (Guid?)r.SaleId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SaleReturn?> GetByIdWithItemsAsync(Guid shopId, Guid saleReturnId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.ShopId == shopId && r.Id == saleReturnId, cancellationToken);

    public async Task<SaleReturn?> GetByReturnNumberAsync(Guid shopId, string returnNumber, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.ShopId == shopId && r.ReturnNumber == returnNumber, cancellationToken);

    public async Task<IReadOnlyList<SaleReturn>> GetByShopAndDateRangeAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(r => r.Items)
            .Where(r => r.ShopId == shopId
                && r.BusinessDate >= startDate
                && r.BusinessDate <= endDate)
            .OrderByDescending(r => r.ProcessedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SaleReturn>> GetPostedInRangeAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Items)
            .Include(r => r.Settlements)
            .Where(r => r.ShopId == shopId
                && ((r.BusinessDate >= startDate && r.BusinessDate <= endDate)
                    || (r.VoidedBusinessDate >= startDate && r.VoidedBusinessDate <= endDate)))
            .OrderBy(r => r.ProcessedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SaleReturn>> GetBySaleAsync(Guid shopId, Guid saleId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(r => r.Items)
            .Where(r => r.ShopId == shopId && r.SaleId == saleId)
            .OrderByDescending(r => r.ProcessedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleReturnItem>> GetLinesBySaleItemAsync(Guid shopId, Guid saleItemId, CancellationToken cancellationToken = default) =>
        await _context.SaleReturnItems
            .Where(i => i.ShopId == shopId && i.SaleItemId == saleItemId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
}
