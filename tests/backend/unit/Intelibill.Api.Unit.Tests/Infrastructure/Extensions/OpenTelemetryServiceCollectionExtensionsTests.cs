using Intelibill.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Extensions;

public sealed class OpenTelemetryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInteliBillOpenTelemetry_RegistersTracerProvider()
    {
        using var serviceProvider = BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<TracerProvider>());
    }

    [Fact]
    public void AddInteliBillOpenTelemetry_RegistersMeterProvider()
    {
        using var serviceProvider = BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<MeterProvider>());
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:NewRelic:OtlpEndpoint"] = "http://localhost:4318",
                ["Observability:NewRelic:ApiKey"] = "test-api-key",
                ["Observability:NewRelic:ServiceName"] = "Intelibill.Api.Tests",
                ["Observability:NewRelic:ServiceVersion"] = "1.0.0",
                ["Observability:NewRelic:Environment"] = "test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInteliBillOpenTelemetry(configuration);
        return services.BuildServiceProvider();
    }
}
