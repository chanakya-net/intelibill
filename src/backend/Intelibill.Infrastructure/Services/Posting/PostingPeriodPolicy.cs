using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Posting;

/// <inheritdoc cref="IPostingPeriodPolicy"/>
internal sealed class PostingPeriodPolicy(ApplicationDbContext context, IPostingPeriodRepository periods) : IPostingPeriodPolicy
{
    private sealed class PeriodRow
    {
        public Guid Id { get; set; }

        public int State { get; set; }
    }

    public async Task<ErrorOr<PostingPeriodLease>> AuthorizeForWriteAsync(
        Guid shopId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "The posting period fence requires an open transaction. The shared lock it takes on the " +
                "period is released when the transaction ends; without one it would be released at once " +
                "and a close could commit between the check and the posting.");
        }

        var (start, end) = PostingPeriod.MonthOf(businessDate);

        // The row exists before it is locked, so a month nobody has touched cannot be closed under a
        // posting: the close finds the same row and waits on it.
        await periods.EnsureExistsAsync(shopId, start, end, cancellationToken);

        PeriodRow? row;
        if (context.Database.IsNpgsql())
        {
            // FOR SHARE: postings share the row with one another and exclude only a close or reopen.
            row = (await context.Database
                .SqlQuery<PeriodRow>($"SELECT id AS \"Id\", state AS \"State\" FROM posting_periods WHERE shop_id = {shopId} AND start_date = {start} FOR SHARE")
                .ToListAsync(cancellationToken))
                .SingleOrDefault();
        }
        else
        {
            row = await context.PostingPeriods
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
