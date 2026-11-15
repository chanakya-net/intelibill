using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Intelibill.P0103ConsumerRegressionTests;

internal static class SecurityFixtures
{
    internal const string Password = "P0103-Fixture-Password!";
    // Stored cost-12 bcrypt vector generated independently of the application.
    internal const string PasswordHash = "$2y$12$oTfRV26CUKcFUs4rpGwa8etst3WwXC5xfJZWvkiaeq5xtREtbjegO";
    internal const string LegacyToken = "P0103-legacy-refresh-fixture-secret-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    internal const string LegacyHash = "CBFD4DEFD0E2273B62F2C66850E0FFBC02A03CC085EDD97EA302DB8D41AA8C8F";
    internal static readonly (string Token, string Hash) LegacyBeforeExpiry =
        ("P0103-legacy-refresh-fixture-secret-before-expiry", "25841271808C6D6ADD989C352E87B56FD6C1FDE8DA41F2D0B32B0DEC1C48759B");
    internal static readonly (string Token, string Hash) LegacyAtExpiry =
        ("P0103-legacy-refresh-fixture-secret-at-expiry", "03427427514139F88D2AA44D89B4BBFF18766E370CF485CD5B30E5F6E7B093D2");
    internal static readonly (string Token, string Hash) LegacyConcurrent =
        ("P0103-legacy-refresh-fixture-secret-concurrent", "05357DB9F680CF818A9892EFC62BBD24C67502B2C1AF9228FED4EA33C03602D3");
    internal const string MerchantIssuer = "https://p0103-merchant.test";
    internal const string MerchantAudience = "p0103-merchant-fixture";
    internal const string MerchantKey = "p0103-merchant-fixture-signing-key-never-deploy";
    internal const string LicensingIssuer = "https://p0103-licensing.test";
    internal const string LicensingAudience = "p0103-licensing-fixture";
    internal const string LicensingKey = "p0103-licensing-fixture-signing-key-never-deploy";

    internal static string Root { get; } = FindRoot();

    internal static HttpClient HttpsClient<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    internal static void Configure(
        IWebHostBuilder builder, string project, string section, NpgsqlConnectionStringBuilder database,
        string environment, Dictionary<string, string?>? settings = null)
    {
        settings ??= [];
        settings[section + ":Host"] = database.Host;
        settings[section + ":Port"] = database.Port.ToString(CultureInfo.InvariantCulture);
        settings[section + ":Database"] = database.Database;
        settings[section + ":Username"] = database.Username;
        settings[section + ":Password"] = database.Password;

        builder.UseContentRoot(Path.Combine(Root, project));
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
    }

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

internal sealed class MerchantHost(NpgsqlConnectionStringBuilder database) : WebApplicationFactory<global::Program>
{
    internal FixtureClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        SecurityFixtures.Configure(builder, "src/backend/Intelibill.Api", "Database", database, "Development", new()
        {
            ["App:BaseUrl"] = SecurityFixtures.MerchantIssuer,
            ["Database:RequireRestrictedRuntimeRole"] = "true",
            ["Database:UseEntraAuth"] = "false",
            ["Email:Enabled"] = "false",
            ["PhoneAuth:Enabled"] = "false",
            ["Jwt:SigningMode"] = "Hmac",
            ["Jwt:Secret"] = SecurityFixtures.MerchantKey,
            ["Jwt:Issuer"] = SecurityFixtures.MerchantIssuer,
            ["Jwt:Audience"] = SecurityFixtures.MerchantAudience,
            ["RateLimiting:BucketKey"] = Convert.ToBase64String("p0103 rate limit fixture key...."u8),
            ["Observability:NewRelic:ApiKey"] = "p0103-fixture-only",
            ["Observability:NewRelic:OtlpEndpoint"] = "http://127.0.0.1:1",
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            var workers = services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType?.Name is "IdentityMailWorker" or "IdentitySmsWorker").ToArray();
            foreach (var worker in workers)
                services.Remove(worker);
        });
    }
}

internal sealed class LicensingHost(NpgsqlConnectionStringBuilder database)
    : WebApplicationFactory<Intelibill.Licensing.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        SecurityFixtures.Configure(builder, "src/licensing/Intelibill.Licensing.Api", "LicensingDatabase", database, "Testing");
}

internal sealed class FixtureClock : TimeProvider
{
    private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    internal void Set(DateTimeOffset instant) => Interlocked.Exchange(ref _utcTicks, instant.UtcTicks);
}
