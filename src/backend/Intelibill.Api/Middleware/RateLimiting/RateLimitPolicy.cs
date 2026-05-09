namespace Intelibill.Api.Middleware.RateLimiting;

public readonly record struct RateLimitPolicy(int Limit, int PeriodInMinutes, int BackoffMinutes);

