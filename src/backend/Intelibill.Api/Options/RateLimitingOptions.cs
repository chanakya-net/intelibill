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

    /// <summary>
    /// Ignores the per-endpoint <see cref="Middleware.RateLimiting.RateLimitAttribute"/> values and
    /// applies the defaults above to every limited endpoint.
    /// </summary>
    /// <remarks>
    /// For local development, where five sign-up attempts per fifteen minutes — shared by every
    /// browser behind the compose proxy, because the bucket is keyed on the caller's address —
    /// stops testing the flow at all. It widens limits rather than removing them: a runaway loop is
    /// still caught, it just takes the default allowance rather than five requests.
    /// <see cref="RateLimitingOptionsValidator"/> refuses this outside Development and Testing, so
    /// a value copied into a deployed configuration stops that revision from starting instead of
    /// quietly unhardening the auth endpoints.
    /// </remarks>
    public bool RelaxEndpointPolicies { get; init; }
}

