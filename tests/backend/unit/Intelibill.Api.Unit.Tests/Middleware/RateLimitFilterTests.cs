using System.Globalization;
using System.Net;
using System.Security.Claims;
using Intelibill.Api.Middleware.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class RateLimitFilterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnActionExecutionAsync_WhenQueryStringDiffers_UsesSameBucket()
    {
        var cache = new RecordingDistributedCache();
        var filter = CreateFilter(cache, new RateLimitPolicy(1, 1, 3));

        await ExecuteAsync(filter, CreateContext("/api/items", "?search=milk", "GET"));
        var second = CreateContext("/api/items", "?search=bread", "GET");

        await ExecuteAsync(filter, second);

        Assert.IsType<ObjectResult>(second.Result);
        Assert.Single(cache.SetKeys, static key => key.StartsWith("RL_", StringComparison.Ordinal) && !key.StartsWith("RL_BLOCK_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenHttpMethodDiffers_UsesDifferentBuckets()
    {
        var cache = new RecordingDistributedCache();
        var filter = CreateFilter(cache, new RateLimitPolicy(1, 1, 3));

        await ExecuteAsync(filter, CreateContext("/api/items", string.Empty, "GET"));
        var post = CreateContext("/api/items", string.Empty, "POST");

        await ExecuteAsync(filter, post);

        Assert.Null(post.Result);
        Assert.Equal(2, cache.SetKeys.Count(key => key.StartsWith("RL_", StringComparison.Ordinal) && !key.StartsWith("RL_BLOCK_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenAllowed_AddsRateLimitHeaders()
    {
        var cache = new RecordingDistributedCache();
        var filter = CreateFilter(cache, new RateLimitPolicy(3, 2, 3));
        var context = CreateContext("/api/items", string.Empty, "GET");

        await ExecuteAsync(filter, context);

        Assert.Null(context.Result);
        Assert.Equal("3", context.HttpContext.Response.Headers["X-RateLimit-Limit"]);
        Assert.Equal("2", context.HttpContext.Response.Headers["X-RateLimit-Remaining"]);
        Assert.Equal(FixedNow.AddMinutes(2).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), context.HttpContext.Response.Headers["X-RateLimit-Reset"]);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenLimitExceeded_ReturnsProblemDetailsAndHeaders()
    {
        var cache = new RecordingDistributedCache();
        var filter = CreateFilter(cache, new RateLimitPolicy(1, 2, 3));

        await ExecuteAsync(filter, CreateContext("/api/items", string.Empty, "GET"));
        var exceeded = CreateContext("/api/items", string.Empty, "GET");

        await ExecuteAsync(filter, exceeded);

        var result = Assert.IsType<ObjectResult>(exceeded.Result);
        var problemDetails = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("RateLimitExceeded", problemDetails.Title);
        Assert.Equal("1", exceeded.HttpContext.Response.Headers["X-RateLimit-Limit"]);
        Assert.Equal("0", exceeded.HttpContext.Response.Headers["X-RateLimit-Remaining"]);
        Assert.Equal("180", exceeded.HttpContext.Response.Headers["Retry-After"]);
        Assert.Equal(FixedNow.AddMinutes(3).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), exceeded.HttpContext.Response.Headers["X-RateLimit-Reset"]);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenBackoffIsActive_ReturnsProblemDetailsAndHeaders()
    {
        var cache = new RecordingDistributedCache();
        var filter = CreateFilter(cache, new RateLimitPolicy(1, 2, 3));

        await ExecuteAsync(filter, CreateContext("/api/items", string.Empty, "GET"));
        await ExecuteAsync(filter, CreateContext("/api/items", string.Empty, "GET"));
        var blocked = CreateContext("/api/items", string.Empty, "GET");

        await ExecuteAsync(filter, blocked);

        var result = Assert.IsType<ObjectResult>(blocked.Result);
        var problemDetails = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("RateLimitExceeded", problemDetails.Title);
        Assert.Equal("180", blocked.HttpContext.Response.Headers["Retry-After"]);
        Assert.Equal(FixedNow.AddMinutes(3).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), blocked.HttpContext.Response.Headers["X-RateLimit-Reset"]);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenPolicyIsDisabled_SkipsCacheAndContinues()
    {
        var cache = new RecordingDistributedCache();
        var filter = CreateFilter(cache, RateLimitPolicy.Disabled);
        var context = CreateContext("/api/items", string.Empty, "GET");
        var nextWasCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, filters: [], controller: new object()));
        });

        Assert.True(nextWasCalled);
        Assert.Null(context.Result);
        Assert.Empty(cache.SetKeys);
        Assert.Empty(context.HttpContext.Response.Headers);
    }

    private static RateLimitFilter CreateFilter(RecordingDistributedCache cache, RateLimitPolicy policy)
        => new(cache, new StaticRateLimitPolicyResolver(policy), new FakeTimeProvider(FixedNow));

    private static async Task ExecuteAsync(RateLimitFilter filter, ActionExecutingContext context)
    {
        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(
            context,
            filters: [],
            controller: new object())));
    }

    private static ActionExecutingContext CreateContext(string path, string queryString, string method)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], authenticationType: "Test"))
        };
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(queryString);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            filters: [],
            actionArguments: new Dictionary<string, object?>(),
            controller: new object());
    }

    private sealed class StaticRateLimitPolicyResolver(RateLimitPolicy policy) : IRateLimitPolicyResolver
    {
        public RateLimitPolicy Resolve(ActionExecutingContext context) => policy;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public List<string> SetKeys { get; } = [];

        public byte[]? Get(string key) => _entries.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _entries.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _entries[key] = value;
            SetKeys.Add(key);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
