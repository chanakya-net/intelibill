using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IPostingPeriodRepository
{
    /// <summary>Creates the month's row when missing (<c>ON CONFLICT DO NOTHING</c>); never fails on a race.</summary>
    Task EnsureExistsAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    /// <summary>Loads the month's row tracked under <c>FOR UPDATE</c>. Requires an open transaction.</summary>
    Task<PostingPeriod?> LockForTransitionAsync(Guid shopId, DateOnly startDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PostingPeriod>> GetRangeAsync(Guid shopId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PostingPeriodEvent>> GetEventsAsync(Guid shopId, IReadOnlyCollection<Guid> periodIds, CancellationToken cancellationToken = default);

    Task AddEventAsync(PostingPeriodEvent periodEvent, CancellationToken cancellationToken = default);
}
