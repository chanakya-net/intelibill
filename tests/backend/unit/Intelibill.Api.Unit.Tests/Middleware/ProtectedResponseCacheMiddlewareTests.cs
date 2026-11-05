using Intelibill.Api.Logging;
using Intelibill.Api.Middleware;
using Intelibill.Api.PublicErrors;
using Intelibill.Api.Unit.Tests.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Api.Unit.Tests.Middleware;

public class ProtectedResponseCacheMiddlewareTests
{
    [Fact]
    public async Task ApiOk_IsNoStore()
    {
        var context = await InvokeAsync("/api/customers", inner => inner.Response.StatusCode = StatusCodes.Status200OK);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status409Conflict)]
    public async Task ApiRefusals_AreNoStore(int status)
    {
        var context = await InvokeAsync("/api/sales/123", inner => inner.Response.StatusCode = status);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task EndpointCachePolicy_IsOverridden()
    {
        // An endpoint that asks for caching is still answering for one identity: the header the
        // browser sees is the one written when the response starts.
        var context = await InvokeAsync(
            "/api/items",
            inner => inner.Response.Headers.CacheControl = "public, max-age=60");

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task UnhandledException_ProblemIsNoStore()
    {
        using var log = new CapturedLog();
        var exceptionHandling = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("unplanned"),
            new SafeErrorFactory(),
            new SafeDiagnosticWriter(log.CreateLogger<SafeDiagnosticWriter>()));

        var context = await InvokeAsync("/api/bank-accounts", exceptionHandling.InvokeAsync);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task HubPath_IsNoStore()
    {
        var context = await InvokeAsync("/hubs/products/negotiate", _ => { });

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task ProtectedPath_IsNoStoreEvenBeforeTheResponseStarts()
    {
        string? seenByEndpoint = null;

        await InvokeAsync(
            "/api/customers",
            inner => seenByEndpoint = inner.Response.Headers.CacheControl.ToString(),
            fireOnStarting: false);

        Assert.Equal("no-store", seenByEndpoint);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/favicon.ico")]
    public async Task NonProtectedPath_KeepsItsPolicy(string path)
    {
        var context = await InvokeAsync(path, inner => inner.Response.Headers.CacheControl = "public, max-age=3600");

        Assert.Equal("public, max-age=3600", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("/apis/x")]
    [InlineData("/api-docs")]
    [InlineData("/hubsx/products")]
    public async Task LookalikePath_IsNotProtected(string path)
    {
        var context = await InvokeAsync(path, _ => { });

        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }

    private static Task<HttpContext> InvokeAsync(string path, Action<HttpContext> endpoint, bool fireOnStarting = true) =>
        InvokeAsync(path, context =>
        {
            endpoint(context);
            return Task.CompletedTask;
        }, fireOnStarting);

    private static async Task<HttpContext> InvokeAsync(string path, RequestDelegate next, bool fireOnStarting = true)
    {
        var context = new DefaultHttpContext();
        var response = new StartingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        context.Request.Path = path;

        var middleware = new ProtectedResponseCacheMiddleware(next);
        await middleware.InvokeAsync(context);

        if (fireOnStarting)
            await response.StartAsync();

        return context;
    }

    /// <summary>Records <c>OnStarting</c> callbacks and runs them the way a server does: last registered first.</summary>
    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> _onStarting = new();
        private bool _started;

        public override bool HasStarted => _started;

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Push((callback, state));

        public async Task StartAsync()
        {
            while (_onStarting.TryPop(out var entry))
                await entry.Callback(entry.State);

            _started = true;
        }
    }
}
