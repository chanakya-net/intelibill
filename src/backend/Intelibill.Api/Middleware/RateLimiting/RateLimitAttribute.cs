using Microsoft.AspNetCore.Mvc.Filters;

namespace Intelibill.Api.Middleware.RateLimiting;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RateLimitAttribute : Attribute
{
    public int Limit { get; set; }
    public int PeriodInMinutes { get; set; }
    public int BackoffMinutes { get; set; }

    /// <summary>
    /// Also count requests against the account or destination the request model names (see
    /// <see cref="IRateLimitSubject"/>), on top of the caller's own budget.
    /// </summary>
    public RateLimitSubject Subject { get; set; }

    /// <summary>
    /// Refuse with 503 when the shared store cannot decide, instead of falling back to a limit
    /// that only this replica knows about. Set on every endpoint that checks a credential or
    /// sends a message.
    /// </summary>
    public bool FailClosed { get; set; }
}
