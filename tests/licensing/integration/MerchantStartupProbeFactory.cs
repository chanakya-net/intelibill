using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Intelibill.Tests.Integration;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class MerchantStartupProbeFactory(
    NpgsqlConnectionStringBuilder runtime, string environment = "Testing",
    IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    : WebApplicationFactory<global::Program>
{
    private static readonly string[] ExporterNames = ["OtlpTraceExporter", "OtlpMetricExporter"];


    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(ProjectPaths.Root, "src/backend/Intelibill.Api"));
        builder.UseEnvironment(environment);
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
                // Consent and trial deliveries otherwise wait up to 3 s for the worker's next tick;
                // most fixtures link several shops, so that idle time dominated the suite.
                ["MerchantDeliveryPolling:Interval"] = "00:00:00.050",
                // A parallel run can delay one in-process licensing reply past production's 5 s budget.
                ["MerchantBillingDelegation:RequestTimeout"] = "00:00:30",
            });
            configuration.AddInMemoryCollection(TestDatabaseBudgets.Merchant);
            if (settings is not null) configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            foreach (var name in ExporterNames)
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new LocalTelemetrySink());

            var mailWorkers = services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType?.Name is "IdentityMailWorker" or "IdentitySmsWorker").ToArray();
            foreach (var worker in mailWorkers)
                services.Remove(worker);
            if (PregeneratedHandlers.Enabled)
            {
                services.CritterStackDefaults(options =>
                {
                    options.Development.GeneratedCodeMode = TypeLoadMode.Static;
                    options.Development.AssertAllPreGeneratedTypesExist = true;
                });
            }

            TestHostResources.Configure(services);
            configure?.Invoke(services);
        });
    }

    private sealed class LocalTelemetrySink : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
