using Serilog;
using Serilog.Configuration;
using Serilog.Core;

namespace Intelibill.Shared.Web.Logging;

public static class RedactedSinkExtensions
{
    /// <summary>
    /// Writes to the sinks <paramref name="configure"/> adds only after <paramref name="redactor"/>
    /// has rewritten each event.
    /// </summary>
    public static LoggerConfiguration Redacted(
        this LoggerSinkConfiguration writeTo,
        LogEventRedactor redactor,
        Action<LoggerSinkConfiguration> configure) =>
        writeTo.Sink(LoggerSinkConfiguration.Wrap(sink => new RedactingSink(sink, redactor), configure));

    /// <summary>
    /// Puts every sink a host writes to behind <paramref name="redactor"/>: the host's own exporters
    /// (<paramref name="exporters"/>) asynchronously, then each <see cref="ILogEventSink"/> registered
    /// in the container (none in production) synchronously, so what it records is exactly what an
    /// exporter would have sent. Nothing is written beside them.
    /// </summary>
    public static LoggerConfiguration WriteToRedactedSinks(
        this LoggerConfiguration configuration,
        IServiceProvider services,
        LogEventRedactor redactor,
        int asyncBufferSize,
        Action<LoggerSinkConfiguration> exporters)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(services);

        configuration.WriteTo.Async(a => a.Redacted(redactor, exporters), bufferSize: asyncBufferSize);

        var registeredSinks = services.GetServices<ILogEventSink>().ToArray();
        if (registeredSinks.Length > 0)
        {
            configuration.WriteTo.Redacted(redactor, redacted =>
            {
                foreach (var sink in registeredSinks)
                    redacted.Sink(sink);
            });
        }

        return configuration;
    }
}
