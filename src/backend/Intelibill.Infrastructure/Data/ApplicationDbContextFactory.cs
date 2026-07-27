using Intelibill.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Intelibill.Infrastructure.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configurationBasePath = ResolveConfigurationBasePath(Directory.GetCurrentDirectory());
        var configuration = new ConfigurationBuilder()
            .SetBasePath(configurationBasePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? throw new InvalidOperationException("Database configuration is missing for design-time ApplicationDbContext creation.");

        // Through the same factory the runtime uses, so `dotnet ef` and the
        // migration bundle authenticate exactly the way the application does —
        // including with Entra tokens, which no connection string can express.
        var dataSource = NpgsqlDataSourceFactory.Create(databaseOptions);

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder
            .UseNpgsql(
                dataSource,
                npgsql => npgsql.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName))
            .UseSnakeCaseNamingConvention();

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    internal static string ResolveConfigurationBasePath(string currentDirectory)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(currentDirectory, "src/backend/Intelibill.Api")),
            Path.GetFullPath(Path.Combine(currentDirectory, "../Intelibill.Api")),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "Intelibill.Api.csproj")))
            {
                return candidate;
            }
        }

        // A migration bundle contains no source tree. Its working directory is
        // still a valid base for optional JSON files, while environment
        // variables provide the production database configuration.
        return Path.GetFullPath(currentDirectory);
    }
}
