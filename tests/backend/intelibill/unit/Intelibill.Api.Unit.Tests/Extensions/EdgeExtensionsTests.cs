using System.Net;
using Intelibill.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Extensions;

public class EdgeExtensionsTests
{
    [Fact]
    public void ForwardedHeaders_AreIgnoredWhenNoProxyIsConfigured()
    {
        var forwarded = ResolveForwardedHeaders([]);

        Assert.Equal(ForwardedHeaders.None, forwarded.ForwardedHeaders);
    }

    [Fact]
    public void ForwardedHeaders_ReadSchemeHostAndClientWhenEnabled()
    {
        var forwarded = ResolveForwardedHeaders(new Dictionary<string, string?>
        {
            ["Proxy:Enabled"] = "true",
            ["Proxy:ForwardLimit"] = "2",
            ["Proxy:TrustAnyProxy"] = "true",
        });

        Assert.True(forwarded.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(forwarded.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(forwarded.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
        Assert.Equal(2, forwarded.ForwardLimit);
    }

    [Fact]
    public void ForwardedHeaders_TrustAnyProxy_LeavesNoAddressRestriction()
    {
        var forwarded = ResolveForwardedHeaders(new Dictionary<string, string?>
        {
            ["Proxy:Enabled"] = "true",
            ["Proxy:TrustAnyProxy"] = "true",
        });

        // The loopback defaults must be gone, or ingress traffic is not trusted and
        // the headers are dropped in the environment this setting exists for.
        Assert.Empty(forwarded.KnownProxies);
        Assert.Empty(forwarded.KnownIPNetworks);
    }

    [Fact]
    public void ForwardedHeaders_WithoutTrustAnyProxy_TrustOnlyTheConfiguredSources()
    {
        var forwarded = ResolveForwardedHeaders(new Dictionary<string, string?>
        {
            ["Proxy:Enabled"] = "true",
            ["Proxy:KnownProxies:0"] = "10.1.2.3",
            ["Proxy:KnownNetworks:0"] = "10.20.0.0/16",
        });

        Assert.Equal(IPAddress.Parse("10.1.2.3"), Assert.Single(forwarded.KnownProxies));
        var network = Assert.Single(forwarded.KnownIPNetworks);
        Assert.Equal(IPAddress.Parse("10.20.0.0"), network.BaseAddress);
        Assert.Equal(16, network.PrefixLength);
    }

    [Fact]
    public void Cors_AllowsNoOriginWhenNoneIsConfiguredOutsideDevelopment()
    {
        var policy = ResolveCorsPolicy([], "Production");

        Assert.Empty(policy.Origins);
    }

    [Fact]
    public void Cors_FallsBackToLocalhostOriginsInDevelopment()
    {
        var policy = ResolveCorsPolicy([], "Development");

        Assert.Contains("http://localhost:4200", policy.Origins);
        Assert.Contains("http://localhost:4000", policy.Origins);
    }

    [Fact]
    public void Cors_UsesTheConfiguredOriginsExactly()
    {
        var policy = ResolveCorsPolicy(
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://app.example.com",
            },
            "Production");

        Assert.Equal(["https://app.example.com"], policy.Origins);
        Assert.True(policy.SupportsCredentials);
        Assert.False(policy.AllowAnyOrigin);
    }

    [Fact]
    public void Cors_ConfiguredOriginsWinOverTheDevelopmentFallback()
    {
        var policy = ResolveCorsPolicy(
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://staging.example.com",
            },
            "Development");

        Assert.Equal(["https://staging.example.com"], policy.Origins);
    }

    private static ForwardedHeadersOptions ResolveForwardedHeaders(Dictionary<string, string?> settings)
        => BuildProvider(settings, "Production").GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

    private static Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicy ResolveCorsPolicy(
        Dictionary<string, string?> settings,
        string environmentName)
    {
        var corsOptions = BuildProvider(settings, environmentName)
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>()
            .Value;

        var policy = corsOptions.GetPolicy(EdgeExtensions.CorsPolicyName);
        Assert.NotNull(policy);
        return policy;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings, string environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEdge(configuration, environment);

        return services.BuildServiceProvider();
    }
}
