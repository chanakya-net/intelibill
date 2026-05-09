using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;

namespace Intelibill.Api.Middleware.RateLimiting;

public class RateLimitFilter : IAsyncActionFilter
{
    private readonly IDistributedCache _cache;
    private readonly IRateLimitPolicyResolver _policyResolver;

    public RateLimitFilter(IDistributedCache cache, IRateLimitPolicyResolver policyResolver)
    {
        _cache = cache;
        _policyResolver = policyResolver;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var policy = _policyResolver.Resolve(context);
        var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = context.HttpContext.User.Identity?.IsAuthenticated == true
            ? context.HttpContext.User.FindFirst("sub")?.Value ?? "anonymous"
            : "anonymous";

        var path = context.HttpContext.Request.Path.ToString();
        var cacheKey = $"RL_{user}_{ipAddress}_{path}";
        var blockKey = $"RL_BLOCK_{user}_{ipAddress}_{path}";

        // 1. Check if blocked (Backoff)
        var blockedAt = await _cache.GetAsync(blockKey);
        if (blockedAt != null)
        {
            context.Result = CreateTooManyRequestsResult();
            return;
        }

        // 2. Check current count
        var cachedValue = await _cache.GetAsync(cacheKey);
        int currentCount = 0;

        if (cachedValue != null)
        {
            currentCount = BitConverter.ToInt32(cachedValue, 0);
        }

        if (currentCount >= policy.Limit)
        {
            // Apply backoff if specified
            if (policy.BackoffMinutes > 0)
            {
                await _cache.SetAsync(blockKey, BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(policy.BackoffMinutes)
                });
            }

            context.Result = CreateTooManyRequestsResult();
            return;
        }

        // 3. Increment count
        currentCount++;
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(policy.PeriodInMinutes)
        };

        await _cache.SetAsync(cacheKey, BitConverter.GetBytes(currentCount), options);

        await next();
    }

    private static ObjectResult CreateTooManyRequestsResult()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "RateLimitExceeded",
            Detail = $"Too many requests. Try again later.",
            Extensions = { ["errors"] = new[] { new { Code = "RateLimitExceeded", Description = "Rate limit reached or backoff in effect." } } }
        };

        return new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status429TooManyRequests };
    }
}
