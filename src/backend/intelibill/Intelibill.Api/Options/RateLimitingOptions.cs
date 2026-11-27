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
    /// still caught, it just takes the default allowance rather than five requests. Account and
    /// destination budgets widen to the same default.
    /// <see cref="RateLimitingOptionsValidator"/> refuses this outside Development and Testing, so
    /// a value copied into a deployed configuration stops that revision from starting instead of
    /// quietly unhardening the auth endpoints.
    /// </remarks>
    public bool RelaxEndpointPolicies { get; init; }

    /// <summary>
    /// Attempts against one account — sign-in and password-reset confirmation — from any number of
    /// addresses. Twenty per fifteen minutes stops a distributed guess at one password without
    /// locking its owner out for longer than the window.
    /// </summary>
    public SubjectBudgetOptions Account { get; init; } = new() { Limit = 20, PeriodInMinutes = 15 };

    /// <summary>
    /// Messages one mailbox or phone can be sent — verification, reset, sign-in codes — from any
    /// number of addresses: three per fifteen minutes and ten per day.
    /// </summary>
    public SubjectBudgetOptions Destination { get; init; } = new() { Limit = 3, PeriodInMinutes = 15, DailyLimit = 10 };

    /// <summary>
    /// <see cref="SubjectBudgetMode.Shadow"/> counts account and destination budgets and records
    /// what they would have refused without refusing it, for measuring before enforcing.
    /// </summary>
    public SubjectBudgetMode SubjectMode { get; init; } = SubjectBudgetMode.Enforce;

    /// <summary>
    /// Base64 of 32 random bytes. Bucket keys are HMACs under it, so the store holds nothing a
    /// leaked table could be checked against. Every replica must share it. When absent, buckets
    /// are hashed without a key (see <see cref="Middleware.RateLimiting.RateLimitKeyHasher"/>).
    /// </summary>
    public string? BucketKey { get; init; }

    /// <summary>
    /// The key <see cref="BucketKey"/> replaced. While set, every budget is also counted under it,
    /// so a rotation never hands out a fresh allowance. Remove it once the longest window (a day)
    /// has passed since the rotation. <c>unkeyed</c> names the unkeyed hashing used before a
    /// first key was configured.
    /// </summary>
    public string? PreviousBucketKey { get; init; }
}

public sealed class SubjectBudgetOptions
{
    public int Limit { get; init; }

    public int PeriodInMinutes { get; init; }

    /// <summary>A second, day-long budget for the same subject; zero for none.</summary>
    public int DailyLimit { get; init; }
}

public enum SubjectBudgetMode
{
    Enforce,
    Shadow,
}
