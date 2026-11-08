using ErrorOr;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Tills;

/// <summary>Runs inside the source transaction. The caller acquires all posting-period fences first.</summary>
public interface ITillPostingService
{
    /// <summary>Locks and reloads the explicit session; null is only disabled/no-context legacy flow.</summary>
    Task<ErrorOr<TillSession?>> AcquireAsync(Guid shopId, Guid actorId, TillPostingContext? context,
        DateOnly sourceBusinessDate, CancellationToken ct = default);
    /// <summary>Stages unique source attribution and immutable movements; never saves or commits.</summary>
    Task<ErrorOr<Success>> AppendAsync(TillSession session, TillSourcePosting source, Guid actorId, CancellationToken ct = default);
}
