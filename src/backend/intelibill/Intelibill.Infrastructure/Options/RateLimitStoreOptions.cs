using System.ComponentModel.DataAnnotations;

namespace Intelibill.Infrastructure.Options;

/// <summary>
/// How long the shared rate-limit store may take, and how it keeps its table small.
/// </summary>
public sealed class RateLimitStoreOptions
{
    public const string SectionName = "RateLimitStore";

    /// <summary>
    /// Ceiling for each statement and for waiting on a locked bucket. Short on purpose: an
    /// acquisition sits in front of every limited request, and a store that cannot answer quickly
    /// is treated as unavailable rather than left to stall the request.
    /// </summary>
    [Range(1, 30)]
    public int CommandTimeoutSeconds { get; init; } = 2;

    /// <summary>How often expired buckets are deleted.</summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Most rows one cleanup statement deletes, so a backlog never holds a long lock.</summary>
    [Range(1, 100_000)]
    public int CleanupBatchSize { get; init; } = 1000;
}
