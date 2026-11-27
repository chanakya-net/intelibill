using System.Text.RegularExpressions;

namespace Intelibill.Shared.Web.Logging;

/// <summary>Where a failure happened and how it was answered; nothing taken from the exception.</summary>
public sealed partial record SafeDiagnosticContext(
    string TraceId,
    string ErrorCode,
    int Status,
    string Method,
    string Route,
    bool ResponseStarted = false)
{
    private const string UnmatchedRoute = "<unmatched>";
    private const string OtherMethod = "<other>";

    public static SafeDiagnosticContext For(
        HttpContext context,
        string traceId,
        string errorCode,
        int status,
        bool responseStarted = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The route template is code; the raw path could carry an id or a token.
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? UnmatchedRoute;
        var method = MethodFormat().IsMatch(context.Request.Method) ? context.Request.Method : OtherMethod;
        return new SafeDiagnosticContext(traceId, errorCode, status, method, route, responseStarted);
    }

    [GeneratedRegex("^[A-Z]{1,16}$")]
    private static partial Regex MethodFormat();
}

public interface ISafeDiagnosticWriter
{
    /// <summary>A failure nobody planned for: the incident support finds by its trace id.</summary>
    void Unexpected(Exception exception, SafeDiagnosticContext context);

    /// <summary>A request refused because of what the caller sent.</summary>
    void Rejected(Exception exception, SafeDiagnosticContext context);
}

/// <summary>
/// Records failures from allowlisted facts only. The exception object is never handed to the
/// logger, so its message and <c>Data</c> cannot reach a sink by way of this event.
/// </summary>
public sealed partial class SafeDiagnosticWriter(ILogger<SafeDiagnosticWriter> logger) : ISafeDiagnosticWriter
{
    public void Unexpected(Exception exception, SafeDiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        LogUnexpected(
            logger,
            context.ErrorCode,
            context.Method,
            context.Route,
            context.Status,
            context.ResponseStarted,
            context.TraceId,
            SafeExceptionDescription.Types(exception),
            SafeExceptionDescription.SqlState(exception),
            SafeExceptionDescription.ProviderStatus(exception),
            SafeExceptionDescription.Frames(exception));
    }

    public void Rejected(Exception exception, SafeDiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        LogRejected(
            logger,
            context.ErrorCode,
            context.Method,
            context.Route,
            context.Status,
            context.TraceId,
            SafeExceptionDescription.Types(exception),
            SafeExceptionDescription.Frames(exception));
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unexpected failure {ErrorCode} on {Method} {Route}, answered {Status} (response started: {ResponseStarted}), trace {TraceId}. "
            + "Exception {ExceptionTypes}, SQLSTATE {SqlState}, provider status {ProviderStatus}, frames {ExceptionFrames}")]
    private static partial void LogUnexpected(
        ILogger logger,
        string errorCode,
        string method,
        string route,
        int status,
        bool responseStarted,
        string traceId,
        IReadOnlyList<string> exceptionTypes,
        string? sqlState,
        int? providerStatus,
        IReadOnlyList<string> exceptionFrames);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Request rejected {ErrorCode} on {Method} {Route}, answered {Status}, trace {TraceId}. "
            + "Exception {ExceptionTypes}, frames {ExceptionFrames}")]
    private static partial void LogRejected(
        ILogger logger,
        string errorCode,
        string method,
        string route,
        int status,
        string traceId,
        IReadOnlyList<string> exceptionTypes,
        IReadOnlyList<string> exceptionFrames);
}
