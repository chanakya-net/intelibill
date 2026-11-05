namespace Intelibill.Api.Middleware;

/// <summary>
/// Keeps protected answers out of every cache between the API and the reader.
/// </summary>
/// <remarks>
/// Everything under <c>/api</c> and <c>/hubs</c> answers for one user in one shop, and a cache keys
/// what it stores by URL: a stored copy would be handed to whoever asks for the same URL next,
/// under another identity. The header is written before the endpoint runs and again as the response
/// starts, so neither an endpoint's own policy nor an error answer written further in can replace it.
/// Health probes and anything outside these paths keep the policy they set.
/// See docs/frontend-architecture.md § Protected API Cache Isolation.
/// </remarks>
public sealed class ProtectedResponseCacheMiddleware(RequestDelegate next)
{
    private const string NoStore = "no-store";

    public Task InvokeAsync(HttpContext context)
    {
        if (!IsProtected(context.Request.Path))
            return next(context);

        context.Response.Headers.CacheControl = NoStore;
        context.Response.OnStarting(static state =>
        {
            ((HttpResponse)state).Headers.CacheControl = NoStore;
            return Task.CompletedTask;
        }, context.Response);

        return next(context);
    }

    private static bool IsProtected(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);
}
