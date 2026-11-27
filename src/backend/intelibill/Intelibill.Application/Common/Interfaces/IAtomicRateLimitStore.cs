namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// One allowance a request must fit inside: at most <see cref="Limit"/> admissions per fixed
/// window of <see cref="WindowSeconds"/> for the bucket <see cref="Key"/> under
/// <see cref="PolicyId"/>. <see cref="Key"/> is opaque: callers hash the identifying material
/// before it gets here, so the store never holds an address, account or destination.
/// </summary>
/// <param name="BackoffSeconds">
/// When the window is exhausted, refuse the bucket for this long from the first refusal, even if
/// the window resets sooner. Zero means the window end is the only cooldown.
/// </param>
public sealed record RateBudget(string PolicyId, string Key, int Limit, int WindowSeconds, int BackoffSeconds = 0);

/// <summary>
/// The outcome of one acquisition. <see cref="Limit"/>, <see cref="Remaining"/> and
/// <see cref="ResetAt"/> describe the first budget passed in, the only one a caller may show;
/// the others decide admission without their counts ever leaving the store.
/// </summary>
/// <param name="RetryAt">Set only when refused: the earliest moment every refusing budget clears.</param>
public sealed record RateDecision(bool Allowed, int Limit, int Remaining, DateTimeOffset ResetAt, DateTimeOffset? RetryAt);

/// <summary>
/// The store could not decide. Callers choose what that means per endpoint: refuse the request
/// outright, or fall back to a limit they know is local.
/// </summary>
public sealed class RateLimitStoreUnavailableException(Exception innerException)
    : Exception("The rate limit store is unavailable.", innerException);

/// <summary>
/// Shared, atomic rate-limit budgets. Every replica reads and writes the same counters, and one
/// acquisition checks and consumes all of its budgets together or none of them.
/// </summary>
public interface IAtomicRateLimitStore
{
    /// <summary>
    /// Admits the request only if every budget has room, and then consumes one unit from each.
    /// A refusal consumes nothing, though it may start an exhausted budget's backoff.
    /// </summary>
    /// <exception cref="RateLimitStoreUnavailableException">The store could not be reached in time.</exception>
    Task<RateDecision> AcquireAsync(IReadOnlyList<RateBudget> budgets, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> buckets whose window and backoff have both
    /// ended, and returns how many it deleted.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
}
