using System.ComponentModel.DataAnnotations;

namespace Intelibill.Infrastructure.Options;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public LoggingOptions Logging { get; init; } = new();
    public TracingOptions Tracing { get; init; } = new();
    public MetricsOptions Metrics { get; init; } = new();
    public NewRelicOptions NewRelic { get; init; } = new();
}

public sealed class LoggingOptions
{
    [Required]
    public string MinimumLevel { get; init; } = "Information";

    public bool EnablePiiMasking { get; init; }

    [Range(1000, 1_000_000)]
    public int AsyncBufferSize { get; init; } = 10_000;
}

public sealed class TracingOptions
{
    [Range(0.0, 1.0)]
    public double SamplingRatio { get; init; } = 1.0;

    public bool EnableW3CPropagation { get; init; } = true;
}

public sealed class MetricsOptions
{
    public bool EnableRuntimeMetrics { get; init; } = true;
}

public sealed class NewRelicOptions
{
    [Required]
    public string OtlpEndpoint { get; init; } = "https://otlp.nr-data.net:4318";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string ServiceName { get; init; } = "InteliBill";

    [Required]
    public string ServiceVersion { get; init; } = string.Empty;

    [Required]
    public string Environment { get; init; } = string.Empty;
}
