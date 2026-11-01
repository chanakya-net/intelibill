using Intelibill.Api.Logging;
using Intelibill.Api.Middleware;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;
using Serilog.AspNetCore;

namespace Intelibill.Api.Extensions;

public static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog as the application's logging provider.
    /// Reads <see cref="ObservabilityOptions"/> from DI so appsettings.json changes
    /// take effect on restart without recompilation.
    /// </summary>
    public static WebApplicationBuilder AddInteliBillSerilog(
        this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((_, services, config) =>
        {
            var obs = services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
            var logging = obs.Logging;
            var newRelic = obs.NewRelic;

            var levelSwitch = new LoggingLevelSwitch(
                Enum.Parse<LogEventLevel>(logging.MinimumLevel, ignoreCase: true));

            config
                .MinimumLevel.ControlledBy(levelSwitch)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Wolverine", LogEventLevel.Warning)
                .MinimumLevel.Override("Wolverine.Http.HttpGraph", LogEventLevel.Error)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("Application", "InteliBill");

            // Attribute-driven masking for DTOs that opt in; the name-based redaction below
            // applies to every event whether or not a type was annotated.
            if (logging.EnablePiiMasking)
                config.Destructure.With<SensitiveDataDestructuringPolicy>();

            var redactor = new LogEventRedactor(maskPersonalData: logging.EnablePiiMasking);

            // Every sink sits behind the redactor. Nothing is written beside it: a sink added
            // here outside Redacted() would receive raw exceptions and unmasked credentials.
            config.WriteTo.Async(
                a => a.Redacted(redactor, redacted =>
                {
                    // Console — compact JSON for local dev / container stdout
                    redacted.Console(new CompactJsonFormatter());

                    // New Relic via OTLP HTTP/protobuf
                    redacted.OpenTelemetry(otlp =>
                    {
                        otlp.Endpoint = newRelic.OtlpEndpoint;
                        otlp.Protocol = OtlpProtocol.HttpProtobuf;
                        otlp.Headers = new Dictionary<string, string>
                        {
                            ["api-key"] = newRelic.ApiKey,
                        };
                        otlp.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = newRelic.ServiceName,
                            ["service.version"] = newRelic.ServiceVersion,
                            ["deployment.environment"] = newRelic.Environment,
                        };
                    });
                }),
                bufferSize: logging.AsyncBufferSize);

            // Sinks registered in the container (none in production) get the same treatment,
            // synchronously, so what they record is exactly what an exporter would have sent.
            var registeredSinks = services.GetServices<ILogEventSink>().ToArray();
            if (registeredSinks.Length > 0)
            {
                config.WriteTo.Redacted(redactor, redacted =>
                {
                    foreach (var sink in registeredSinks)
                        redacted.Sink(sink);
                });
            }
        });

        return builder;
    }

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
    /// Pre-configured <see cref="RequestLoggingOptions"/> action for
    /// <c>app.UseSerilogRequestLogging(SerilogExtensions.RequestLoggingOptions)</c>.
    /// </summary>
    public static readonly Action<RequestLoggingOptions> RequestLoggingOptions = options =>
    {
        options.GetLevel = static (ctx, _, ex) =>
        {
            // A caller who went away is not a failed request, whatever escaped the endpoint.
            if (ex is not null && CallerDisconnect.Explains(ctx, ex))
                return LogEventLevel.Information;
            if (ex is not null || ctx.Response.StatusCode >= 500)
                return LogEventLevel.Error;
            if (ctx.Response.StatusCode >= 400)
                return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };

        options.EnrichDiagnosticContext = static (diagCtx, httpCtx) =>
        {
            diagCtx.Set("ClientIp",
                httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            diagCtx.Set("RequestPath", httpCtx.Request.Path.Value ?? string.Empty);
            diagCtx.Set("StatusCode", httpCtx.Response.StatusCode);
            // Elapsed is populated automatically by Serilog request logging.
        };
    };
}
