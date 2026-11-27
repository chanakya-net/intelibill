using Intelibill.Shared.Web.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Api.Bootstrap;

internal sealed class AdminRequestBoundary(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISafeDiagnosticWriter diagnostic)
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        var endpoint = context.GetEndpoint();
        if (endpoint is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
        {
            await BootstrapHttp.Problem(context, 403, "Admin.AccessDenied", "Provider access is unavailable.").ExecuteAsync(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted && IsDatabaseUnavailable(exception))
        {
            const string code = "Bootstrap.StorageUnavailable";
            diagnostic.Unexpected(exception, SafeDiagnosticContext.For(context, context.TraceIdentifier, code, 503));
            await BootstrapHttp.Problem(context, 503, code, "Provider access storage is unavailable.").ExecuteAsync(context);
        }
    }

    private static bool IsDatabaseUnavailable(Exception exception) =>
        exception is NpgsqlException or TimeoutException
        || (exception is DbUpdateException && exception.InnerException is NpgsqlException);
}
