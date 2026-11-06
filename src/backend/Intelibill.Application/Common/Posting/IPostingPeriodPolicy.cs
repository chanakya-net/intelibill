using ErrorOr;

namespace Intelibill.Application.Common.Posting;

/// <summary>
/// The fence every posting passes before it writes a business date: it refuses a closed month and,
/// for an open one, holds a shared lock on the month's row until the caller's transaction ends.
/// </summary>
/// <remarks>
/// <para>
/// A close takes the same row <c>FOR UPDATE</c>, so a close cannot commit while an authorized
/// posting is still uncommitted, and a posting that starts after a close committed is refused. Either
/// the posting commits before the close, or it is rejected; none commits into a closed month.
/// </para>
/// <para>
/// Call it after the tenant scope is set and before the first posting write, inside the transaction
/// that carries the posting. It throws <see cref="InvalidOperationException"/> without one, because
/// the lock would otherwise be released at once and protect nothing.
/// </para>
/// </remarks>
public interface IPostingPeriodPolicy
{
    Task<ErrorOr<PostingPeriodLease>> AuthorizeForWriteAsync(
        Guid shopId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}

/// <summary>The open month a posting was authorized against.</summary>
public sealed record PostingPeriodLease(Guid PeriodId, DateOnly StartDate, DateOnly EndDate);
