using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SaleRepository(ApplicationDbContext context)
    : RepositoryBase<Sale>(context), ISaleRepository
{
    public async Task<Sale?> GetByIdAsync(Guid saleId, Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.ShopId == shopId, cancellationToken);

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
            .Where(s => s.ShopId == shopId && DateOnly.FromDateTime(s.SoldAt.UtcDateTime) == reportingDay)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(s => s.Items)
            .Where(s => s.ShopId == shopId
                && DateOnly.FromDateTime(s.SoldAt.UtcDateTime) >= startDate
                && DateOnly.FromDateTime(s.SoldAt.UtcDateTime) <= endDate)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(cancellationToken);
}
