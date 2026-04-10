using Microsoft.AspNetCore.Mvc.Filters;

namespace Intelibill.Api.Middleware.RateLimiting;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RateLimitAttribute : Attribute, IFilterFactory
{
    public int Limit { get; set; }
    public int PeriodInMinutes { get; set; }
    public int BackoffMinutes { get; set; }

    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var cache = serviceProvider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        return new RateLimitFilter(cache, Limit, PeriodInMinutes, BackoffMinutes);
    }
}
