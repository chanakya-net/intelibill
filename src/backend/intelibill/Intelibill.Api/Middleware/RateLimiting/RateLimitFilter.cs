using System.Globalization;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Api.Options;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Middleware.RateLimiting;

/// <summary>
/// Admits or refuses each limited request against the shared budgets in
/// <see cref="IAtomicRateLimitStore"/>: the caller's own budget for the endpoint and, where the
/// endpoint declares one, the account or destination the request acts on, checked and consumed
/// together.
/// <para>
/// Runs at <see cref="FilterOrder"/>, ahead of <c>ModelStateInvalidFilter</c>: a body that fails
/// validation still spends the caller's budget, so leaving the body out is not a way past it.
/// </para>
/// </summary>
public sealed partial class RateLimitFilter(
    IAtomicRateLimitStore store,
    IRateLimitPolicyResolver policyResolver,
    RateBudgetFactory budgetFactory,
    LocalFallbackRateLimiter fallback,
    RateLimitMetrics metrics,
    IOptions<RateLimitingOptions> options,
    TimeProvider timeProvider,
    ILogger<RateLimitFilter> logger) : IAsyncActionFilter
{
    /// <summary>Before <c>ModelStateInvalidFilter</c> (-2000).</summary>
    public const int FilterOrder = -3000;

    private const int MaxRetryAfterSeconds = 86_400;

    private static readonly Error StoreUnavailable =
        Error.Failure("RateLimit.Unavailable", "This service is temporarily unavailable. Try again shortly.");

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var policy = policyResolver.Resolve(context);
        if (policy.IsDisabled)
        {
            await next();
            return;
        }

        var budgets = budgetFactory.Create(context, policy);
        var now = timeProvider.GetUtcNow();
        var shared = true;
        RateDecision decision;

        try
        {
            decision = await AcquireAsync(budgets, now, context.HttpContext.RequestAborted);
        }
        catch (RateLimitStoreUnavailableException exception)
        {
            metrics.StoreUnavailable(policy.FailClosed);
            LogStoreUnavailable(logger, exception.InnerException?.GetType().Name ?? nameof(RateLimitStoreUnavailableException), policy.FailClosed);

            // A credential check or a message send is not worth making without the shared limit:
            // a per-replica count would let the attempts it exists to stop through.
            if (policy.FailClosed)
            {
                context.HttpContext.Response.Headers.RetryAfter = "5";
                context.Result = StoreUnavailable.ToUnavailableResult();
                return;
            }

            decision = fallback.Acquire(budgets.Request[0], now);
            shared = false;
        }

        if (!decision.Allowed)
        {
            metrics.Denied();
            var retryAt = decision.RetryAt ?? decision.ResetAt;
            if (shared)
                AddLimitHeaders(context.HttpContext, decision.Limit, remaining: 0, retryAt);
            context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(now, retryAt).ToString(CultureInfo.InvariantCulture);
            context.Result = CreateTooManyRequestsResult();
            return;
        }

        metrics.Allowed();
        // The local fallback's numbers describe one replica; showing them as X-RateLimit-* would
        // present them as the shared limit.
        if (shared)
            AddLimitHeaders(context.HttpContext, decision.Limit, decision.Remaining, decision.ResetAt);

        await next();
    }

    private async Task<RateDecision> AcquireAsync(RateBudgetSet budgets, DateTimeOffset now, CancellationToken ct)
    {
        if (budgets.Subject.Count == 0 || options.Value.SubjectMode == SubjectBudgetMode.Enforce)
            return await store.AcquireAsync(budgets.All, now, ct);

        // Shadow: the caller's own budget decides; the subject budgets are counted separately and
        // a refusal there is only recorded.
        var decision = await store.AcquireAsync(budgets.Request, now, ct);
        if (decision.Allowed && !(await store.AcquireAsync(budgets.Subject, now, ct)).Allowed)
            metrics.ShadowDenied();

        return decision;
    }

    private static void AddLimitHeaders(HttpContext httpContext, int limit, int remaining, DateTimeOffset resetAt)
    {
        httpContext.Response.Headers["X-RateLimit-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
        httpContext.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString(CultureInfo.InvariantCulture);
        httpContext.Response.Headers["X-RateLimit-Reset"] = resetAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private static int RetryAfterSeconds(DateTimeOffset now, DateTimeOffset retryAt)
        => (int)Math.Clamp(Math.Ceiling((retryAt - now).TotalSeconds), 1, MaxRetryAfterSeconds);

    private static ObjectResult CreateTooManyRequestsResult()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "RateLimitExceeded",
            Detail = "Too many requests. Try again later.",
            Extensions = { ["errors"] = new[] { new { Code = "RateLimitExceeded", Description = "Rate limit reached or backoff in effect." } } }
        };

        return new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    // The exception object is not handed to the logger: a provider message can quote the
    // connection or the statement. Its type is enough to tell an outage from a timeout.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit store unavailable ({Cause}); FailClosed={FailClosed}.")]
    private static partial void LogStoreUnavailable(ILogger logger, string cause, bool failClosed);
}
