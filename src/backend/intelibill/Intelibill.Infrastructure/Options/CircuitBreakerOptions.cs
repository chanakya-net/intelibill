using System.ComponentModel.DataAnnotations;

namespace Intelibill.Infrastructure.Options;

public sealed class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    [Range(1, 100)]
    public int FailureThreshold { get; init; } = 5;

    [Range(1, 300)]
    public int SamplingDurationSeconds { get; init; } = 30;

    [Range(1, 1000)]
    public int MinimumThroughput { get; init; } = 10;

    [Range(1, 300)]
    public int BreakDurationSeconds { get; init; } = 30;
}
