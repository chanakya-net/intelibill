using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Configuration;

namespace Intelibill.Api.Unit.Tests.Options;

public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void ProductionConfiguration_SamplesHalfOfDistributedTraces()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

        var options = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>();

        Assert.NotNull(options);
        Assert.Equal(0.5, options.Tracing.SamplingRatio);
    }
}
