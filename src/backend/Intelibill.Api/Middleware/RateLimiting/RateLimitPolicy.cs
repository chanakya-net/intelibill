namespace Intelibill.Api.Middleware.RateLimiting;

public readonly record struct RateLimitPolicy(int Limit, int PeriodInMinutes, int BackoffMinutes)
{
    public bool IsDisabled { get; init; }

    public static RateLimitPolicy Disabled { get; } = new(0, 0, 0) { IsDisabled = true };
}
