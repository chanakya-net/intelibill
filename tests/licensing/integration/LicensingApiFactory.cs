using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class LicensingApiFactory(
    NpgsqlConnectionStringBuilder runtime, NpgsqlConnectionStringBuilder? migrator = null,
    string environment = "Testing") : WebApplicationFactory<Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(ProjectPaths.Root, "src/licensing/Intelibill.Licensing.Api"));
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(DatabaseSettings("LicensingDatabase", runtime));
            if (migrator is not null)
                configuration.AddInMemoryCollection(DatabaseSettings("LicensingMigrationDatabase", migrator));
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
