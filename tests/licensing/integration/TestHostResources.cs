using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Test-host service overrides that keep a suite of thousands of short-lived API hosts cheap.
/// </summary>
internal static class TestHostResources
{
    internal static void Configure(IServiceCollection services)
    {
        FastPasswordHashing.Use(services);
        // IHttpClientFactory arms a timer for every handler it builds, for the handler lifetime (2 minutes by
        // default). The timer roots the handler's DI scope, and through it the whole host, long after the test
        // disposed it. With several hosts per test, a full run kept gigabytes of dead hosts reachable and slowed
        // down steadily. An infinite lifetime arms no timer, so a disposed host is collectable at once.
        services.ConfigureHttpClientDefaults(http => http.SetHandlerLifetime(Timeout.InfiniteTimeSpan));
    }
}
