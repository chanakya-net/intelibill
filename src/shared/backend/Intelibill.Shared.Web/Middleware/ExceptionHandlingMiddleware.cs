using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Shared.Web.PublicErrors;
using Intelibill.Shared.Web.Logging;

namespace Intelibill.Shared.Web.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ISafeErrorFactory errorFactory,
    ISafeDiagnosticWriter diagnostics)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (CallerDisconnect.Explains(context, exception))
        {
            // The caller went away. Nothing failed here and nobody is left to read a body, so
            // this is neither an incident nor a response.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception exception)
        {
            var traceId = SafeTraceId.Resolve(context);
            var error = errorFactory.Create(exception, traceId);
            var started = context.Response.HasStarted;
            var diagnostic = SafeDiagnosticContext.For(context, traceId, error.Code, error.Status, started);

            if (error.Status >= StatusCodes.Status500InternalServerError)
                diagnostics.Unexpected(exception, diagnostic);
            else
                diagnostics.Rejected(exception, diagnostic);

            if (started)
            {
                // Status and headers are already on the wire and the body may stop mid-record. A
                // problem document appended now would read as more data, so the connection is cut
                // instead: the client sees a failed transfer, never a complete one.
                context.Abort();
                return;
            }

            await WriteProblemAsync(context, error);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, PublicError error)
    {
        var problem = new
        {
            type = error.Type,
            title = error.Title,
            status = error.Status,
            code = error.Code,
            traceId = error.TraceId,
            errors = error.Errors,
        };

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = error.Status;
        return context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
