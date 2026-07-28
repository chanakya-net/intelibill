using Intelibill.Infrastructure.Observability;
using Microsoft.AspNetCore.Routing;

namespace Intelibill.Api.Middleware;

internal sealed class RequestDatabaseTelemetryMiddleware(
    RequestDelegate next,
    RequestTelemetryMetrics metrics,
    RequestDatabaseTelemetryAccessor databaseTelemetryAccessor)
{
    private const string UnmatchedRoute = "<unmatched>";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        using var requestTelemetry = databaseTelemetryAccessor.BeginRequest();

        try
        {
            await next(httpContext);
        }
        finally
        {
            var route = (httpContext.GetEndpoint() as RouteEndpoint)?
                .RoutePattern
                .RawText;

            metrics.RecordDatabaseTelemetry(
                requestTelemetry.Telemetry,
                httpContext.Request.Method,
                route ?? UnmatchedRoute,
                httpContext.Response.StatusCode);
        }
    }
}
