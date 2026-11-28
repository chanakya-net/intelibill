using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Tests.Integration;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class LicensingApiFactory(
    NpgsqlConnectionStringBuilder runtime, NpgsqlConnectionStringBuilder? migrator = null,
    string environment = "Testing", TimeProvider? clock = null, Action<IServiceCollection>? configure = null,
    IReadOnlyDictionary<string, string?>? settings = null,
    SwappableLicensingDatabase? databaseSlot = null) : WebApplicationFactory<Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(ProjectPaths.Root, "src/backend/licensing/Intelibill.Licensing.Api"));
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            if (clock is not null) services.AddSingleton(clock);
            if (databaseSlot is not null)
            {
                services.RemoveAll<IDbContextOptionsConfiguration<LicensingDbContext>>();
                services.RemoveAll<DbContextOptions<LicensingDbContext>>();
                services.RemoveAll<LicensingDbContext>();
                services.AddDbContext<LicensingDbContext>(options => options
                    .UseNpgsql(
                        databaseSlot.ConnectionString,
                        npgsql => npgsql.MigrationsAssembly(typeof(LicensingDbContext).Assembly.GetName().Name))
                    .UseSnakeCaseNamingConvention());
                services.RemoveAll<IBootstrapRateBudget>();
                services.AddSingleton<IBootstrapRateBudget>(new SwappableBootstrapRateBudget(databaseSlot));
            }
            if (settings is null || !settings.TryGetValue("AdminBootstrap:DataProtectionKeyDirectory", out var keyDir)
                || string.IsNullOrEmpty(keyDir))
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            }
            TestHostResources.Configure(services);
            configure?.Invoke(services);
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(DatabaseSettings("LicensingDatabase", runtime));
            configuration.AddInMemoryCollection(TestDatabaseBudgets.Licensing);
            if (migrator is not null)
                configuration.AddInMemoryCollection(DatabaseSettings("LicensingMigrationDatabase", migrator));
            if (settings is not null) configuration.AddInMemoryCollection(settings);
        });
    }

    internal static Dictionary<string, string?> DatabaseSettings(string section, NpgsqlConnectionStringBuilder database) => new()
    {
        [section + ":Host"] = database.Host,
        [section + ":Port"] = database.Port.ToString(CultureInfo.InvariantCulture),
        [section + ":Database"] = database.Database,
        [section + ":Username"] = database.Username,
        [section + ":Password"] = database.Password,
    };
}
