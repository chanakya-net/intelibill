using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class SaleRepository(ApplicationDbContext context)
    : RepositoryBase<Sale>(context), ISaleRepository
{
    private static DateTime ToUtcStart(DateOnly localDate)
    {
        var localMidnight = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, TimeZoneInfo.Local);
    }

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
}
