using Intelibill.Shared.Web.Logging;
using Intelibill.Shared.Web.Observability;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Intelibill.Licensing.Api.Observability;

public static class LicensingObservabilityExtensions
{
    private const string ServiceName = "intelibill-licensing";

    /// <summary>
    /// Serilog for every log producer in the host — requests, background workers, audit
    /// failures — with each sink behind the shared redactor.
    /// </summary>
    public static WebApplicationBuilder AddLicensingSerilog(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<LicensingLoggingOptions>()
            .Bind(builder.Configuration.GetSection(LicensingLoggingOptions.SectionName))
            .Validate(options => options.IsValid(), "Observability:Logging needs a Serilog level and a positive buffer size.")
            .ValidateOnStart();

        builder.Host.UseSerilog((_, services, config) =>
        {
            var logging = services.GetRequiredService<IOptions<LicensingLoggingOptions>>().Value;
            config
                .MinimumLevel.Is(logging.Level)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "InteliBill.Licensing")
                .WriteToRedactedSinks(
                    services,
                    new LogEventRedactor(maskPersonalData: logging.EnablePiiMasking),
                    logging.AsyncBufferSize,
                    redacted => redacted.Console(new CompactJsonFormatter()));
        });

        return builder;
    }

    /// <summary>
    /// Request tracing whose spans lose exception text before any exporter sees them. No
    /// exporter is configured yet; one added later goes after <see cref="SafeSpanProcessor"/>.
    /// </summary>
    public static IServiceCollection AddLicensingTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddProcessor(new SafeSpanProcessor()));
        return services;
    }
}
