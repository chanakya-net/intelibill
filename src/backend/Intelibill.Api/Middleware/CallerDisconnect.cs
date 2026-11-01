namespace Intelibill.Api.Middleware;

/// <summary>
/// Whether a failure is just the caller going away: the request was aborted, and what surfaced is
/// a cancellation or the I/O error Kestrel raises on the next read or write of a reset connection.
/// Neither is an incident, and nobody is left to answer.
/// </summary>
internal static class CallerDisconnect
{
    public static bool Explains(HttpContext context, Exception exception) =>
        context.RequestAborted.IsCancellationRequested
        && exception is OperationCanceledException or IOException;
}
