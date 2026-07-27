using System.Net;
using Intelibill.Api.HealthChecks;
using Intelibill.Api.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Extensions;

/// <summary>
/// The edge of the application: what it believes about the proxy in front of it,
/// which origins may call it from a browser, and how the platform asks whether it
/// is alive and ready.
/// </summary>
internal static class EdgeExtensions
{
    internal const string CorsPolicyName = "Frontend";
    internal const string ReadinessTag = "ready";

    /// <summary>
    /// Origins the Angular dev server and its container build run on. Applied when
    /// the Development environment configures none of its own, so that a fresh
    /// clone works without an untracked appsettings.Development.json.
    /// </summary>
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:4200", "https://localhost:4200",
        "http://localhost:4000", "https://localhost:4000",
    ];

    public static IServiceCollection AddEdge(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ProxyOptions>>((forwarded, proxyOptions) =>
            {
                var proxy = proxyOptions.Value;
                if (!proxy.Enabled)
                {
                    forwarded.ForwardedHeaders = ForwardedHeaders.None;
                    return;
                }

                forwarded.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                forwarded.ForwardLimit = proxy.ForwardLimit;

                // The defaults trust loopback only, which no external ingress is.
                forwarded.KnownProxies.Clear();
                forwarded.KnownIPNetworks.Clear();

                if (proxy.TrustAnyProxy)
                {
                    // An empty list of known proxies and networks means "accept from
                    // anywhere". Safe only where nothing can reach the container
                    // except the ingress that put the headers there.
                    return;
                }

                foreach (var address in proxy.KnownProxies)
                {
                    forwarded.KnownProxies.Add(IPAddress.Parse(address));
                }

                foreach (var network in proxy.KnownNetworks)
                {
                    forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                var origins = configuration
                    .GetSection(CorsOptions.SectionName)
                    .Get<CorsOptions>()?.AllowedOrigins ?? [];

                if (origins.Count == 0 && environment.IsDevelopment())
                {
                    origins = DevelopmentOrigins;
                }

                if (origins.Count == 0)
                {
                    // No origins: every cross-origin browser request is denied. Same-
                    // origin calls through the web proxy and non-browser clients such
                    // as the mobile app are unaffected.
                    return;
                }

                policy
                    .WithOrigins([.. origins])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: [ReadinessTag]);

        return services;
    }

    /// <summary>
    /// Liveness answers "is this process still running" and must not depend on
    /// PostgreSQL: a database outage that fails liveness gets every replica killed
    /// and restarted, turning a recoverable outage into a crash loop. Readiness is
    /// the one that checks dependencies, so traffic stops arriving until they are
    /// back.
    /// </summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadinessTag),
        }).AllowAnonymous();

        return app;
    }
}
