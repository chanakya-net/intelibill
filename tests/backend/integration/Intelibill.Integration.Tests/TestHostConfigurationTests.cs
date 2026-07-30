namespace Intelibill.Integration.Tests;

public sealed class TestHostConfigurationTests
{
    [Fact]
    public void OpenTelemetrySdk_IsDisabledBeforeApplicationStartup()
    {
        Assert.Equal(
            "true",
            Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED"),
            ignoreCase: true);
    }

    [Fact]
    public void DotNetEnvironment_IsDevelopmentBeforeApplicationStartup()
    {
        Assert.Equal(
            "Development",
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
    }
}
