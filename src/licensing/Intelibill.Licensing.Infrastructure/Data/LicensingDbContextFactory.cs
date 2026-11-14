using Intelibill.Licensing.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Intelibill.Licensing.Infrastructure.Data;

public sealed class LicensingDbContextFactory : IDesignTimeDbContextFactory<LicensingDbContext>
{
    public LicensingDbContext CreateDbContext(string[] args)
    {
        const string section = LicensingMigrationDatabaseOptions.MigrationSectionName;
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        var settings = configuration.GetSection(section).Get<LicensingMigrationDatabaseOptions>();
        if (settings is null || !settings.IsValid())
            throw new InvalidOperationException($"{section} settings are missing or invalid.");

        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(settings.ToConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(LicensingDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
        return new LicensingDbContext(options.Options);
    }
}
