using System.ComponentModel.DataAnnotations;

namespace Intelibill.Api.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int Limit { get; init; } = 100;

    [Range(1, int.MaxValue)]
    public int PeriodInMinutes { get; init; } = 1;

    [Range(0, int.MaxValue)]
    public int BackoffMinutes { get; init; } = 3;
}

