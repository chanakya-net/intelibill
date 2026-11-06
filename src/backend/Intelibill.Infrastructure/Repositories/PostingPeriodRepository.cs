using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PostingPeriodRepository(ApplicationDbContext context) : IPostingPeriodRepository
{
    private const string LockTimeout = "5s";

    public async Task EnsureExistsAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
        {
            // No SaveChanges: it would flush the caller's pending changes. The row is added to the
            // context only; it is written with the caller's own save.
            var tracked = context.PostingPeriods.Local.Any(p => p.ShopId == shopId && p.StartDate == startDate);
            if (!tracked && !await context.PostingPeriods.AnyAsync(p => p.ShopId == shopId && p.StartDate == startDate, cancellationToken))
                context.PostingPeriods.Add(PostingPeriod.Open(shopId, startDate));

            return;
        }

        await InsertIfMissingAsync(shopId, startDate, endDate, cancellationToken);
    }

    private async Task InsertIfMissingAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
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

    public async Task<PostingPeriodLock> LockForTransitionAsync(Guid shopId, DateOnly startDate, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
        {
            await EnsureExistsAsync(shopId, startDate, PostingPeriod.MonthOf(startDate).End, cancellationToken);
            var found = context.PostingPeriods.Local.FirstOrDefault(p => p.ShopId == shopId && p.StartDate == startDate)
                ?? await context.PostingPeriods.FirstOrDefaultAsync(p => p.ShopId == shopId && p.StartDate == startDate, cancellationToken);
            return found is null ? new PostingPeriodLock(RowLockOutcome.NotFound, null) : new PostingPeriodLock(RowLockOutcome.Acquired, found);
        }

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The posting period lock must be taken inside a transaction.");

        await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {LockTimeout}, true)", cancellationToken);

        List<PostingPeriod> rows;
        try
        {
            // Inside the budget too: a first touch another transaction has not committed yet makes this insert wait.
            await InsertIfMissingAsync(shopId, startDate, PostingPeriod.MonthOf(startDate).End, cancellationToken);

            // xmin is not part of SELECT *, and the row version is mapped to it.
            rows = await context.PostingPeriods
                .FromSql($"SELECT *, xmin FROM posting_periods WHERE shop_id = {shopId} AND start_date = {startDate} FOR UPDATE")
                .ToListAsync(cancellationToken);
        }
        // Npgsql counts a lock timeout as transient, so the execution strategy hands it back wrapped.
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // The statement failed, so the transaction is now aborted; the caller rolls it back.
            return new PostingPeriodLock(RowLockOutcome.TimedOut, null);
        }

        await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout TO DEFAULT", cancellationToken);
        return rows.Count == 0 ? new PostingPeriodLock(RowLockOutcome.NotFound, null) : new PostingPeriodLock(RowLockOutcome.Acquired, rows[0]);
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
