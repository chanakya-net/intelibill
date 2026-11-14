using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Trace;
using Serilog.Core;

namespace Intelibill.P0102ConsumerRegressionTests;

/// <summary>What a test observes about one host: its HTTP surface, its sink and its exported spans.</summary>
internal interface IHostUnderTest : IDisposable
{
    string Name { get; }

    CapturingSink Sink { get; }

    CapturingSpanProcessor Spans { get; }

    IServiceProvider Services { get; }

    HttpClient CreateHttpsClient();
}

internal static class HostProbes
{
    internal static string Root { get; } = FindRoot();

    internal static Dictionary<string, string?> Settings(string section, NpgsqlConnectionStringBuilder database, bool maskPii) => new()
    {
        [section + ":Host"] = database.Host,
        [section + ":Port"] = database.Port.ToString(CultureInfo.InvariantCulture),
        [section + ":Database"] = database.Database,
        [section + ":Username"] = database.Username,
        [section + ":Password"] = database.Password,
        ["Observability:Logging:MinimumLevel"] = "Information",
        ["Observability:Logging:EnablePiiMasking"] = maskPii ? "true" : "false",
    };

    /// <summary>Test seams only: a sink, a span reader after the host's processors, a failing path and worker.</summary>
    internal static void Install(IWebHostBuilder builder, CapturingSink sink, CapturingSpanProcessor spans) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(sink);
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, FailureProbe>();
            services.AddHostedService<WorkerProbe>();
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.SetSampler(new AlwaysOnSampler()).AddProcessor(spans));
        });

    internal static HttpClient HttpsClient<T>(WebApplicationFactory<T> factory)
        where T : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}

internal sealed class MerchantHost(NpgsqlConnectionStringBuilder runtime, bool maskPii)
    : WebApplicationFactory<global::Program>, IHostUnderTest
{
    private static readonly string[] ExporterNames = ["OtlpTraceExporter", "OtlpMetricExporter"];

    public string Name => "merchant";

    public CapturingSink Sink { get; } = new();

    public CapturingSpanProcessor Spans { get; } = new();

    public HttpClient CreateHttpsClient() => HostProbes.HttpsClient(this);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(HostProbes.Root, "src/backend/Intelibill.Api"));
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(HostProbes.Settings("Database", runtime, maskPii));
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://merchant.test",
                ["Database:RequireRestrictedRuntimeRole"] = "true",
                ["Database:UseEntraAuth"] = "false",
                ["Email:Enabled"] = "false",
                ["PhoneAuth:Enabled"] = "false",
                ["Jwt:SigningMode"] = "Hmac",
                ["Jwt:Secret"] = "p0102-merchant-test-secret-key-at-least-32-chars",
                ["Jwt:Issuer"] = "merchant.test",
                ["Jwt:Audience"] = "merchant.test",
                ["Observability:NewRelic:ApiKey"] = "test-only",
                ["Observability:NewRelic:OtlpEndpoint"] = "http://127.0.0.1:1",
            });
        });
        builder.ConfigureServices(services =>
        {
            foreach (var name in ExporterNames)
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new LocalTelemetrySink());

            var workers = services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType?.Name is "IdentityMailWorker" or "IdentitySmsWorker").ToArray();
            foreach (var worker in workers)
                services.Remove(worker);
        });
        HostProbes.Install(builder, Sink, Spans);
    }

    private sealed class LocalTelemetrySink : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

internal sealed class LicensingHost(NpgsqlConnectionStringBuilder runtime, bool maskPii)
    : WebApplicationFactory<Intelibill.Licensing.Api.Program>, IHostUnderTest
{
    public string Name => "licensing";

    public CapturingSink Sink { get; } = new();

    public CapturingSpanProcessor Spans { get; } = new();

    public HttpClient CreateHttpsClient() => HostProbes.HttpsClient(this);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(HostProbes.Root, "src/licensing/Intelibill.Licensing.Api"));
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(HostProbes.Settings("LicensingDatabase", runtime, maskPii)));
        HostProbes.Install(builder, Sink, Spans);
    }
}
