using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Services.Posting;

/// <inheritdoc cref="IPostingPeriodPolicy"/>
internal sealed class PostingPeriodPolicy(ApplicationDbContext context, IPostingPeriodRepository periods) : IPostingPeriodPolicy
{
    private const string LockTimeout = "5s";

    private sealed class PeriodRow
    {
        public Guid Id { get; set; }

        public int State { get; set; }
    }

    public async Task<ErrorOr<IReadOnlyList<PostingPeriodLease>>> AuthorizeForWriteAsync(
        Guid shopId,
        IReadOnlyCollection<DateOnly> businessDates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(businessDates);

        // One date per month, lowest month first: the same global order for every transaction.
        var ordered = businessDates
            .Select(date => PostingPeriod.MonthOf(date).Start)
            .Distinct()
            .Order()
            .ToList();

        var leases = new List<PostingPeriodLease>(ordered.Count);
        foreach (var monthStart in ordered)
        {
            var lease = await AuthorizeForWriteAsync(shopId, monthStart, cancellationToken);
            if (lease.IsError)
                return lease.Errors;

            leases.Add(lease.Value);
        }

        return leases;
    }

    public async Task<ErrorOr<PostingPeriodLease>> AuthorizeForWriteAsync(
        Guid shopId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var npgsql = context.Database.IsNpgsql();
        if (npgsql && context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "The posting period fence requires an open transaction. The shared lock it takes on the " +
                "period is released when the transaction ends; without one it would be released at once " +
                "and a close could commit between the check and the posting.");
        }

        var (start, end) = PostingPeriod.MonthOf(businessDate);

        PeriodRow? row;
        string previousTimeout;
        if (npgsql)
        {
            // The caller may already hold a budget of its own; put back exactly what was in force.
            previousTimeout = (await context.Database
                .SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"")
                .ToListAsync(cancellationToken)).Single();
            await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {LockTimeout}, true)", cancellationToken);
            try
            {
                // The row exists before it is locked, so a month nobody has touched cannot be closed under a
                // posting: the close finds the same row and waits on it.
                await periods.EnsureExistsAsync(shopId, start, end, cancellationToken);

                // FOR SHARE: postings share the row with one another and exclude only a close or reopen.
                row = (await context.Database
                    .SqlQuery<PeriodRow>($"SELECT id AS \"Id\", state AS \"State\" FROM posting_periods WHERE shop_id = {shopId} AND start_date = {start} FOR SHARE")
                    .ToListAsync(cancellationToken))
                    .SingleOrDefault();
            }
            // Npgsql counts a lock timeout as transient, so the execution strategy hands it back wrapped.
            catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                // The statement failed, so the transaction is now aborted; the caller rolls it back.
                return Errors.PostingPeriod.Busy;
            }

            // The budget is for this lock only; later locks keep whatever the caller had set.
            await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {previousTimeout}, true)", cancellationToken);
        }
        else
        {
            await periods.EnsureExistsAsync(shopId, start, end, cancellationToken);
            var tracked = context.PostingPeriods.Local.FirstOrDefault(p => p.ShopId == shopId && p.StartDate == start);
            row = tracked is not null
                ? new PeriodRow { Id = tracked.Id, State = (int)tracked.State }
                : await context.PostingPeriods
                    .AsNoTracking()
                    .Where(p => p.ShopId == shopId && p.StartDate == start)
                    .Select(p => new PeriodRow { Id = p.Id, State = (int)p.State })
                    .SingleOrDefaultAsync(cancellationToken);
        }

        if (row is null)
            throw new InvalidOperationException("The posting period row was not visible after it was ensured.");

        return (PostingPeriodState)row.State == PostingPeriodState.Closed
            ? Errors.PostingPeriod.Closed(start)
            : new PostingPeriodLease(row.Id, start, end);
    }
}
