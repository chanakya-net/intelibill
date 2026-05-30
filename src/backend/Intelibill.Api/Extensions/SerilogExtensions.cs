using Intelibill.Api.Logging;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Serilog;
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

            if (logging.EnablePiiMasking)
            {
                config
                    .Destructure.With<SensitiveDataDestructuringPolicy>()
                    .Enrich.With<PiiScrubbingEnricher>();
            }

            config.WriteTo.Async(
                a =>
                {
                    // Console — compact JSON for local dev / container stdout
                    a.Console(new CompactJsonFormatter());

                    // New Relic via OTLP HTTP/protobuf
                    a.OpenTelemetry(otlp =>
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
                },
                bufferSize: logging.AsyncBufferSize);
        });

        return builder;
    }

    /// <summary>
    /// Pre-configured <see cref="RequestLoggingOptions"/> action for
    /// <c>app.UseSerilogRequestLogging(SerilogExtensions.RequestLoggingOptions)</c>.
    /// </summary>
    public static readonly Action<RequestLoggingOptions> RequestLoggingOptions = options =>
    {
        options.GetLevel = static (ctx, _, ex) =>
        {
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
