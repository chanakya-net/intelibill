using System.Diagnostics;
using ErrorOr;
using Serilog;

namespace Intelibill.Application.Common.Behaviours;

/// <summary>
/// Wolverine conventional middleware. Intercepts any handler that returns IErrorOr
/// and emits a structured log when the result represents a failure.
/// Does NOT throw — unhandled exceptions remain Wolverine's responsibility.
/// </summary>
public sealed class ErrorOrResultMiddleware
{
    /// <summary>
    /// Wolverine calls this method after the handler completes when the handler
    /// return value is assignable to <see cref="IErrorOr"/>.
    /// </summary>
    public static void After(IErrorOr result)
    {
        if (!result.IsError) return;

        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString();
        var spanId = activity?.SpanId.ToString();

        var errors = result.Errors;
        if (errors is null || errors.Count == 0) return;

        var firstErrorType = errors[0].Type;

        if (firstErrorType == ErrorType.Validation)
        {
            Log.Warning(
                "Handler returned validation errors. TraceId={TraceId} SpanId={SpanId} Codes={Codes} Descriptions={Descriptions}",
                traceId,
                spanId,
                errors.Select(static e => e.Code).ToArray(),
                errors.Select(static e => e.Description).ToArray());
        }
        else
        {
            Log.Error(
                "Handler returned error result. Type={ErrorType} TraceId={TraceId} SpanId={SpanId} Codes={Codes} Descriptions={Descriptions}",
                firstErrorType,
                traceId,
                spanId,
                errors.Select(static e => e.Code).ToArray(),
                errors.Select(static e => e.Description).ToArray());
        }
    }
}
