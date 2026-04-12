using System.Diagnostics.Metrics;
using System.Net.Http;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.CircuitBreaker;
using Serilog;

namespace Intelibill.Infrastructure.Extensions;

public static class OpenTelemetryServiceCollectionExtensions
{
    private const string OtlpHttpClientName = "otlp-exporter";
    public const string ApplicationMeterName = "InteliBill.Application";

    public static IServiceCollection AddInteliBillOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var observabilityOpts = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var cbOpts = configuration
            .GetSection(CircuitBreakerOptions.SectionName)
            .Get<CircuitBreakerOptions>() ?? new CircuitBreakerOptions();

        var newRelic = observabilityOpts.NewRelic;
        var tracingOpts = observabilityOpts.Tracing;
        var metricsOpts = observabilityOpts.Metrics;

        if (tracingOpts.EnableW3CPropagation)
        {
            Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator([
                new TraceContextPropagator(),
                new BaggagePropagator()
            ]));
        }

        // Polly-wrapped OTLP HTTP client
        services
            .AddHttpClient(OtlpHttpClientName, client =>
                client.DefaultRequestHeaders.Add("api-key", newRelic.ApiKey))
            .AddResilienceHandler("otlp-circuit-breaker", pipeline =>
                pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    FailureRatio = cbOpts.FailureThreshold / 100.0,
                    SamplingDuration = TimeSpan.FromSeconds(cbOpts.SamplingDurationSeconds),
                    MinimumThroughput = cbOpts.MinimumThroughput,
                    BreakDuration = TimeSpan.FromSeconds(cbOpts.BreakDurationSeconds),
                    OnOpened = _ =>
                    {
                        Log.Warning("New Relic OTLP endpoint unavailable — circuit open. Telemetry dropped.");
                        return ValueTask.CompletedTask;
                    }
                }));

        // Custom application meter — injected by application code
        services.AddSingleton(new Meter(ApplicationMeterName));

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: newRelic.ServiceName,
                    serviceVersion: newRelic.ServiceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = newRelic.Environment
                }));

        // Tracing — resolved with IServiceProvider to wire up OTLP HttpClient
        services.ConfigureOpenTelemetryTracerProvider((sp, builder) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            builder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                .AddSource("Wolverine")
                .SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(tracingOpts.SamplingRatio)))
                .AddOtlpExporter(o =>
                {
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    o.Endpoint = new Uri($"{newRelic.OtlpEndpoint.TrimEnd('/')}/v1/traces");
                    o.HttpClientFactory = () => httpClientFactory.CreateClient(OtlpHttpClientName);
                });
        });

        // Metrics — same Polly-wrapped client
        services.ConfigureOpenTelemetryMeterProvider((sp, builder) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            builder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(ApplicationMeterName);

            if (metricsOpts.EnableRuntimeMetrics)
                builder.AddRuntimeInstrumentation();

            builder.AddOtlpExporter(o =>
            {
                o.Protocol = OtlpExportProtocol.HttpProtobuf;
                o.Endpoint = new Uri($"{newRelic.OtlpEndpoint.TrimEnd('/')}/v1/metrics");
                o.HttpClientFactory = () => httpClientFactory.CreateClient(OtlpHttpClientName);
            });
        });

        return services;
    }
}
