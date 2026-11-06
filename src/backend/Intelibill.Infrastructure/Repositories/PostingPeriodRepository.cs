using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PostingPeriodRepository(ApplicationDbContext context) : IPostingPeriodRepository
{
    public async Task EnsureExistsAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
        {
            if (!await context.PostingPeriods.AnyAsync(p => p.ShopId == shopId && p.StartDate == startDate, cancellationToken))
            {
                context.PostingPeriods.Add(PostingPeriod.Open(shopId, startDate));
                await context.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        // ON CONFLICT DO NOTHING: two first touches of a month never raise a unique violation, so
        // neither aborts its transaction. A concurrent uncommitted insert makes this wait, then skip.
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO posting_periods (id, shop_id, start_date, end_date, state, created_at)
            VALUES ({Guid.NewGuid()}, {shopId}, {startDate}, {endDate}, {(int)PostingPeriodState.Open}, {DateTimeOffset.UtcNow})
            ON CONFLICT (shop_id, start_date) DO NOTHING
            """,
            cancellationToken);
    }

    public async Task<PostingPeriod?> LockForTransitionAsync(Guid shopId, DateOnly startDate, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return await context.PostingPeriods.FirstOrDefaultAsync(p => p.ShopId == shopId && p.StartDate == startDate, cancellationToken);

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The posting period lock must be taken inside a transaction.");

        // xmin is not part of SELECT *, and the row version is mapped to it.
        var rows = await context.PostingPeriods
            .FromSql($"SELECT *, xmin FROM posting_periods WHERE shop_id = {shopId} AND start_date = {startDate} FOR UPDATE")
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<PostingPeriod>> GetRangeAsync(Guid shopId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) =>
        await context.PostingPeriods
            .AsNoTracking()
            .Where(p => p.ShopId == shopId && p.StartDate >= fromDate && p.StartDate <= toDate)
            .OrderBy(p => p.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PostingPeriodEvent>> GetEventsAsync(Guid shopId, IReadOnlyCollection<Guid> periodIds, CancellationToken cancellationToken = default) =>
        await context.PostingPeriodEvents
            .AsNoTracking()
            .Where(e => e.ShopId == shopId && periodIds.Contains(e.PostingPeriodId))
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

    public async Task AddEventAsync(PostingPeriodEvent periodEvent, CancellationToken cancellationToken = default) =>
        await context.PostingPeriodEvents.AddAsync(periodEvent, cancellationToken);
}
