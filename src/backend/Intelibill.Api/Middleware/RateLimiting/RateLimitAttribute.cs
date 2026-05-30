using Microsoft.AspNetCore.Mvc.Filters;

namespace Intelibill.Api.Middleware.RateLimiting;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RateLimitAttribute : Attribute
{
    public int Limit { get; set; }
    public int PeriodInMinutes { get; set; }
    public int BackoffMinutes { get; set; }
}
