using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Api.Options;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class RateLimitFilterTests
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WhenQueryStringDiffers_UsesSameBudget()
    {
        var store = new RecordingStore(Allow(3));
        var filter = CreateFilter(store, new RateLimitPolicy(3, 1, 3));

        await ExecuteAsync(filter, CreateContext("/api/items", "?search=milk", "GET"));
        await ExecuteAsync(filter, CreateContext("/api/items", "?search=bread", "GET"));

        Assert.Equal(store.Calls[0].Single().Key, store.Calls[1].Single().Key);
        Assert.Equal(store.Calls[0].Single().PolicyId, store.Calls[1].Single().PolicyId);
    }

    [Fact]
    public async Task WhenHttpMethodDiffers_UsesDifferentBudgets()
    {
        var store = new RecordingStore(Allow(3));
        var filter = CreateFilter(store, new RateLimitPolicy(3, 1, 3));

        await ExecuteAsync(filter, CreateContext("/api/items", string.Empty, "GET"));
        await ExecuteAsync(filter, CreateContext("/api/items", string.Empty, "POST"));

        Assert.NotEqual(store.Calls[0].Single().PolicyId, store.Calls[1].Single().PolicyId);
    }

    [Fact]
    public async Task WhenAllowed_AddsRateLimitHeadersAndContinues()
    {
        var store = new RecordingStore(new RateDecision(true, 3, 2, FixedNow.AddMinutes(2), null));
        var filter = CreateFilter(store, new RateLimitPolicy(3, 2, 3));
        var context = CreateContext("/api/items", string.Empty, "GET");

        var nextCalled = await ExecuteAsync(filter, context);

        Assert.True(nextCalled);
        Assert.Null(context.Result);
        Assert.Equal("3", context.HttpContext.Response.Headers["X-RateLimit-Limit"]);
        Assert.Equal("2", context.HttpContext.Response.Headers["X-RateLimit-Remaining"]);
        Assert.Equal(Unix(FixedNow.AddMinutes(2)), context.HttpContext.Response.Headers["X-RateLimit-Reset"]);
        Assert.Equal(new RateBudget(store.Calls[0][0].PolicyId, store.Calls[0][0].Key, 3, 120, 180), store.Calls[0][0]);
    }

    [Fact]
    public async Task WhenDenied_ReturnsProblemDetailsAndRetryHeaders()
    {
        var store = new RecordingStore(new RateDecision(false, 1, 0, FixedNow.AddMinutes(2), FixedNow.AddMinutes(3)));
        var filter = CreateFilter(store, new RateLimitPolicy(1, 2, 3));
        var context = CreateContext("/api/items", string.Empty, "GET");

        var nextCalled = await ExecuteAsync(filter, context);

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        var problemDetails = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.Equal("RateLimitExceeded", problemDetails.Title);
        Assert.Equal("1", context.HttpContext.Response.Headers["X-RateLimit-Limit"]);
        Assert.Equal("0", context.HttpContext.Response.Headers["X-RateLimit-Remaining"]);
        Assert.Equal("180", context.HttpContext.Response.Headers["Retry-After"]);
        Assert.Equal(Unix(FixedNow.AddMinutes(3)), context.HttpContext.Response.Headers["X-RateLimit-Reset"]);
    }

    [Fact]
    public async Task WhenRetryIsFarAway_CapsRetryAfterAtOneDay()
    {
        var store = new RecordingStore(new RateDecision(false, 1, 0, FixedNow, FixedNow.AddDays(30)));
        var filter = CreateFilter(store, new RateLimitPolicy(1, 2, 3));
        var context = CreateContext("/api/items", string.Empty, "GET");

        await ExecuteAsync(filter, context);

        Assert.Equal("86400", context.HttpContext.Response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task WhenPolicyIsDisabled_SkipsStoreAndContinues()
    {
        var store = new RecordingStore(Allow(1));
        var filter = CreateFilter(store, RateLimitPolicy.Disabled);
        var context = CreateContext("/api/items", string.Empty, "GET");

        var nextCalled = await ExecuteAsync(filter, context);

        Assert.True(nextCalled);
        Assert.Empty(store.Calls);
        Assert.Empty(context.HttpContext.Response.Headers);
    }

    [Fact]
    public async Task WhenSubjectDeclared_AcquiresRequestAndSubjectBudgetsTogether()
    {
        var store = new RecordingStore(Allow(10));
        var filter = CreateFilter(store, new RateLimitPolicy(10, 1, 3) { Subject = RateLimitSubject.Account });

        await ExecuteAsync(filter, CreateContext("/api/auth/login/email", string.Empty, "POST", new Subject("a@example.com")));

        var call = Assert.Single(store.Calls);
        Assert.Equal(["req:POST api/auth/login/email", "subj:account"], call.Select(b => b.PolicyId));
    }

    [Fact]
    public async Task StoreUnavailable_FailClosed_Returns503WithoutContinuing()
    {
        var store = new RecordingStore(new RateLimitStoreUnavailableException(new TimeoutException()));
        var filter = CreateFilter(store, new RateLimitPolicy(10, 1, 3) { FailClosed = true });
        var context = CreateContext("/api/auth/login/email", string.Empty, "POST");

        var nextCalled = await ExecuteAsync(filter, context);

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("RateLimit.Unavailable", Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [Fact]
    public async Task StoreUnavailable_FailOpen_UsesBoundedLocalFallback()
    {
        var store = new RecordingStore(new RateLimitStoreUnavailableException(new TimeoutException()));
        var filter = CreateFilter(store, new RateLimitPolicy(1, 1, 3));
        var first = CreateContext("/api/items", string.Empty, "GET");
        var second = CreateContext("/api/items", string.Empty, "GET");

        var firstContinued = await ExecuteAsync(filter, first);
        var secondContinued = await ExecuteAsync(filter, second);

        Assert.True(firstContinued);
        Assert.False(first.HttpContext.Response.Headers.ContainsKey("X-RateLimit-Limit"));
        Assert.False(secondContinued);
        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(second.Result).StatusCode);
        Assert.Equal("60", second.HttpContext.Response.Headers["Retry-After"]);
    }

    [Fact]
    public async Task ShadowMode_SubjectDenialDoesNotBlock()
    {
        var store = new RecordingStore(Allow(10), new RateDecision(false, 20, 0, FixedNow, FixedNow.AddMinutes(15)));
        var filter = CreateFilter(
            store,
            new RateLimitPolicy(10, 1, 3) { Subject = RateLimitSubject.Account },
            new RateLimitingOptions { BucketKey = Key, SubjectMode = SubjectBudgetMode.Shadow });

        var nextCalled = await ExecuteAsync(filter, CreateContext("/api/auth/login/email", string.Empty, "POST", new Subject("a@example.com")));

        Assert.True(nextCalled);
        Assert.Equal(["req:POST api/auth/login/email"], store.Calls[0].Select(b => b.PolicyId));
        Assert.Equal(["subj:account"], store.Calls[1].Select(b => b.PolicyId));
    }

    [Fact]
    public void FilterOrder_RunsBeforeModelStateValidation()
    {
        Assert.True(RateLimitFilter.FilterOrder < -2000);
    }

    private static RateDecision Allow(int limit) => new(true, limit, limit - 1, FixedNow.AddMinutes(1), null);

    private static string Unix(DateTimeOffset value) => value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static RateLimitFilter CreateFilter(RecordingStore store, RateLimitPolicy policy, RateLimitingOptions? options = null)
    {
        var wrapped = Microsoft.Extensions.Options.Options.Create(options ?? new RateLimitingOptions { BucketKey = Key });
        var phones = Substitute.For<IPhoneAuthPolicy>();
        var budgets = new RateBudgetFactory(wrapped, new RateLimitKeyHasher(wrapped), phones);
        return new RateLimitFilter(
            store,
            new StaticRateLimitPolicyResolver(policy),
            budgets,
            new LocalFallbackRateLimiter(),
            new RateLimitMetrics(new Meter("Intelibill.Api.Unit.Tests.RateLimit")),
            wrapped,
            new FakeTimeProvider(FixedNow),
            NullLogger<RateLimitFilter>.Instance);
    }

    private static async Task<bool> ExecuteAsync(RateLimitFilter filter, ActionExecutingContext context)
    {
        var nextCalled = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, filters: [], controller: new object()));
        });
        return nextCalled;
    }

    private static ActionExecutingContext CreateContext(string path, string queryString, string method, object? argument = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(queryString);

        var descriptor = new ActionDescriptor { AttributeRouteInfo = new AttributeRouteInfo { Template = path.TrimStart('/') } };
        var arguments = new Dictionary<string, object?>();
        if (argument is not null)
            arguments["request"] = argument;

        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), descriptor),
            filters: [],
            actionArguments: arguments,
            controller: new object());
    }

    private sealed record Subject(string? Value) : IRateLimitSubject
    {
        string? IRateLimitSubject.Subject => Value;
    }

    private sealed class StaticRateLimitPolicyResolver(RateLimitPolicy policy) : IRateLimitPolicyResolver
    {
        public RateLimitPolicy Resolve(ActionExecutingContext context) => policy;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers with the given outcomes in order (the last one repeats) and records what it was asked.</summary>
    private sealed class RecordingStore(params object[] outcomes) : IAtomicRateLimitStore
    {
        public List<IReadOnlyList<RateBudget>> Calls { get; } = [];

        public Task<RateDecision> AcquireAsync(IReadOnlyList<RateBudget> budgets, DateTimeOffset now, CancellationToken ct)
        {
            Calls.Add(budgets);
            var outcome = outcomes[Math.Min(Calls.Count - 1, outcomes.Length - 1)];
            return outcome is Exception exception
                ? Task.FromException<RateDecision>(exception)
                : Task.FromResult((RateDecision)outcome);
        }

        public Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct) => Task.FromResult(0);
    }
}
