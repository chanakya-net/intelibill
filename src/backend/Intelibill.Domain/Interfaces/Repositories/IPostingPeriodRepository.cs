using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPostingPeriodRepository
{
    /// <summary>Creates the month's row when missing (<c>ON CONFLICT DO NOTHING</c>); never fails on a race.</summary>
    Task EnsureExistsAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the month's row when missing, then loads it tracked under <c>FOR UPDATE</c>, both within one 5 s lock budget. Requires an open
    /// transaction; on <see cref="RowLockOutcome.TimedOut"/> the transaction is aborted and must be rolled back.
    /// </summary>
    Task<PostingPeriodLock> LockForTransitionAsync(Guid shopId, DateOnly startDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PostingPeriod>> GetRangeAsync(Guid shopId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PostingPeriodEvent>> GetEventsAsync(Guid shopId, IReadOnlyCollection<Guid> periodIds, CancellationToken cancellationToken = default);

    Task AddEventAsync(PostingPeriodEvent periodEvent, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of <see cref="IPostingPeriodRepository.LockForTransitionAsync"/>; <see cref="Period"/> is set only when acquired.</summary>
public sealed record PostingPeriodLock(RowLockOutcome Outcome, PostingPeriod? Period);
