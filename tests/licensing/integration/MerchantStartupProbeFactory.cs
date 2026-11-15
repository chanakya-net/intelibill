using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class MerchantStartupProbeFactory(NpgsqlConnectionStringBuilder runtime)
    : WebApplicationFactory<global::Program>
{
    private static readonly string[] ExporterNames = ["OtlpTraceExporter", "OtlpMetricExporter"];


    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(ProjectPaths.Root, "src/backend/Intelibill.Api"));
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(LicensingApiFactory.DatabaseSettings("Database", runtime));
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://merchant.test",
                ["Database:RequireRestrictedRuntimeRole"] = "true",
                ["Database:UseEntraAuth"] = "false",
                ["Email:Enabled"] = "false",
                ["PhoneAuth:Enabled"] = "false",
                ["Jwt:SigningMode"] = "Hmac",
                ["Jwt:Secret"] = "merchant-test-secret-key-at-least-32-chars",
                ["Jwt:Issuer"] = "merchant.test",
                ["Jwt:Audience"] = "merchant.test",
                ["Observability:NewRelic:ApiKey"] = "test-only",
                // Merchant Serilog creates its own OTLP transport outside the named clients.
                ["Observability:NewRelic:OtlpEndpoint"] = "http://127.0.0.1:1",
            });
        });
        builder.ConfigureServices(services =>
        {
            foreach (var name in ExporterNames)
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new LocalTelemetrySink());

            var mailWorkers = services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType?.Name is "IdentityMailWorker" or "IdentitySmsWorker").ToArray();
            foreach (var worker in mailWorkers)
                services.Remove(worker);
        });
    }

    private sealed class LocalTelemetrySink : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
