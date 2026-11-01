using Serilog.Core;
using Serilog.Events;

namespace Intelibill.Api.Logging;

/// <summary>Hands the sinks it wraps only events that have been through <see cref="LogEventRedactor"/>.</summary>
internal sealed class RedactingSink(ILogEventSink inner, LogEventRedactor redactor) : ILogEventSink, IDisposable
{
    public void Emit(LogEvent logEvent) => inner.Emit(redactor.Redact(logEvent));

    // Serilog disposes only the outermost sink of a wrapper, so the wrapped sinks are flushed here.
    public void Dispose() => (inner as IDisposable)?.Dispose();
}
