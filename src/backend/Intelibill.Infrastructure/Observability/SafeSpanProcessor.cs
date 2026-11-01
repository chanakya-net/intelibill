using System.Diagnostics;
using OpenTelemetry;

namespace Intelibill.Infrastructure.Observability;

/// <summary>
/// Removes exception text from spans before they are exported. EF Core instrumentation sets a
/// failed command's status description to the provider's message, which can quote the value a
/// customer typed; Wolverine does the same for a failed handler. The span keeps its error status
/// and exception type, so a failure is still found and classified — by its trace id, against the
/// safe diagnostic in the logs.
/// </summary>
/// <remarks>
/// Span events cannot be edited after they are added. ASP.NET Core and HttpClient instrumentation
/// only add exception events when <c>RecordException</c> is on, and it is not.
/// </remarks>
public sealed class SafeSpanProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Status == ActivityStatusCode.Error && data.StatusDescription is not null)
            data.SetStatus(ActivityStatusCode.Error);

        data.SetTag("exception.message", null);
        data.SetTag("exception.stacktrace", null);
    }
}
