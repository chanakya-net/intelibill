using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0101Tests
{
    [Fact]
    public async Task DesignTime_RequiresExplicitMigratorInBothWorkingDirectories()
    {
        var runtime = database.Login(LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.RuntimeRole);
        var migrator = database.Login(LicensingPostgreSqlFixture.LicensingDatabase, LicensingPostgreSqlFixture.MigratorRole);
        var runtimeSettings = LicensingApiFactory.DatabaseSettings("LicensingDatabase", runtime);
        var migratorSettings = LicensingApiFactory.DatabaseSettings("LicensingMigrationDatabase", migrator);
        var saved = runtimeSettings.Keys.Concat(migratorSettings.Keys)
            .Select(EnvironmentKey).ToDictionary(key => key, Environment.GetEnvironmentVariable);
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            SetEnvironment(runtimeSettings);
            foreach (var key in migratorSettings.Keys)
                Environment.SetEnvironmentVariable(EnvironmentKey(key), null);

            var factory = new LicensingDbContextFactory();
            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext([]));
            Assert.Contains("LicensingMigrationDatabase", error.Message, StringComparison.Ordinal);
            SetEnvironment(migratorSettings);
            await AssertMigratorIdentityAsync(factory, ProjectPaths.Root);
            await AssertMigratorIdentityAsync(factory, Path.Combine(ProjectPaths.Root, "src/licensing/Intelibill.Licensing.Api"));
            output.WriteLine("Design-time factory selected licensing:licensing_migrator from both directories and refused runtime-only settings.");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            foreach (var (key, value) in saved)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static void SetEnvironment(Dictionary<string, string?> settings)
    {
        foreach (var (key, value) in settings)
            Environment.SetEnvironmentVariable(EnvironmentKey(key), value);
    }

    private static string EnvironmentKey(string key) => key.Replace(":", "__", StringComparison.Ordinal);

    private static async Task AssertMigratorIdentityAsync(LicensingDbContextFactory factory, string workingDirectory)
    {
        Directory.SetCurrentDirectory(workingDirectory);
        await using var context = factory.CreateDbContext([]);
        Assert.Empty(context.Model.GetEntityTypes());
        var settings = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        var identity = await LicensingPostgreSqlFixture.ReadScalarAsync(settings,
            "SELECT current_database() || ':' || current_user");
        Assert.Equal("licensing:licensing_migrator", identity);
    }
}
