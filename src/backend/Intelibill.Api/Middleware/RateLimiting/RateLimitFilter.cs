using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;

namespace Intelibill.Api.Middleware.RateLimiting;

public class RateLimitFilter : IAsyncActionFilter
{
    private readonly IDistributedCache _cache;
    private readonly IRateLimitPolicyResolver _policyResolver;
    private readonly TimeProvider _timeProvider;

    public RateLimitFilter(IDistributedCache cache, IRateLimitPolicyResolver policyResolver)
        : this(cache, policyResolver, TimeProvider.System)
    {
    }

    internal RateLimitFilter(IDistributedCache cache, IRateLimitPolicyResolver policyResolver, TimeProvider timeProvider)
    {
        _cache = cache;
        _policyResolver = policyResolver;
        _timeProvider = timeProvider;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var policy = _policyResolver.Resolve(context);
        var now = _timeProvider.GetUtcNow();
        var bucketKey = BuildBucketKey(context.HttpContext);
        var cacheKey = $"RL_{bucketKey}";
        var blockKey = $"RL_BLOCK_{bucketKey}";

        var blockedState = await ReadStateAsync(blockKey);
        if (blockedState is not null)
        {
            AddHeaders(context.HttpContext, policy, remaining: 0, resetAtUnixSeconds: blockedState.ResetAtUnixSeconds, retryAfterSeconds: RetryAfterSeconds(now, blockedState.ResetAtUnixSeconds));
            context.Result = CreateTooManyRequestsResult();
            return;
        }

        var state = await ReadStateAsync(cacheKey);
        var resetAtUnixSeconds = state?.ResetAtUnixSeconds ?? now.AddMinutes(policy.PeriodInMinutes).ToUnixTimeSeconds();
        var currentCount = state?.Count ?? 0;

        if (currentCount >= policy.Limit)
        {
            var retryAtUnixSeconds = resetAtUnixSeconds;
            if (policy.BackoffMinutes > 0)
            {
                retryAtUnixSeconds = now.AddMinutes(policy.BackoffMinutes).ToUnixTimeSeconds();
                var blockState = new RateLimitState(policy.Limit, retryAtUnixSeconds);
                await WriteStateAsync(blockKey, blockState, TimeSpan.FromMinutes(policy.BackoffMinutes));
            }

            AddHeaders(context.HttpContext, policy, remaining: 0, resetAtUnixSeconds: retryAtUnixSeconds, retryAfterSeconds: RetryAfterSeconds(now, retryAtUnixSeconds));
            context.Result = CreateTooManyRequestsResult();
            return;
        }

        currentCount++;
        var updatedState = new RateLimitState(currentCount, resetAtUnixSeconds);
        await WriteStateAsync(cacheKey, updatedState, TimeSpan.FromMinutes(policy.PeriodInMinutes));

        AddHeaders(context.HttpContext, policy, remaining: Math.Max(policy.Limit - currentCount, 0), resetAtUnixSeconds: resetAtUnixSeconds);

        await next();
    }

    private async Task<RateLimitState?> ReadStateAsync(string key)
    {
        var cachedValue = await _cache.GetAsync(key);
        if (cachedValue is null)
        {
            return null;
        }

        if (cachedValue.Length == sizeof(int))
        {
            return new RateLimitState(BitConverter.ToInt32(cachedValue, 0), _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        }

        return JsonSerializer.Deserialize<RateLimitState>(cachedValue);
    }

    private Task WriteStateAsync(string key, RateLimitState state, TimeSpan lifetime)
        => _cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(state),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime
            });

    private static string BuildBucketKey(HttpContext httpContext)
    {
        var user = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst("sub")?.Value ?? "anonymous"
            : "anonymous";
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var method = httpContext.Request.Method.ToUpperInvariant();
        var path = httpContext.Request.Path.Value?.TrimEnd('/').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        return $"{user}_{ipAddress}_{method}_{path}";
    }

    private static void AddHeaders(HttpContext httpContext, RateLimitPolicy policy, int remaining, long resetAtUnixSeconds, int? retryAfterSeconds = null)
    {
        httpContext.Response.Headers["X-RateLimit-Limit"] = policy.Limit.ToString(CultureInfo.InvariantCulture);
        httpContext.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString(CultureInfo.InvariantCulture);
        httpContext.Response.Headers["X-RateLimit-Reset"] = resetAtUnixSeconds.ToString(CultureInfo.InvariantCulture);
        if (retryAfterSeconds is not null)
        {
            httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static int RetryAfterSeconds(DateTimeOffset now, long resetAtUnixSeconds)
        => Math.Max(1, (int)Math.Ceiling(DateTimeOffset.FromUnixTimeSeconds(resetAtUnixSeconds).Subtract(now).TotalSeconds));

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

    private sealed record RateLimitState(int Count, long ResetAtUnixSeconds);
}
