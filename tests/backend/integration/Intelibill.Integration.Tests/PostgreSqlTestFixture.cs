using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Intelibill.Integration.Tests;

public class PostgreSqlTestFixture : IAsyncLifetime
{
    public PostgreSqlContainer DbContainer { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("integration")
        .WithUsername("integration")
        .WithPassword("integration")
        .Build();

    /// <summary>
    /// The container's own superuser: it owns the schema, applies migrations,
    /// and is the only identity a test may use to inspect rows the policies
    /// would otherwise hide.
    /// </summary>
    public string MigratorConnectionString => DbContainer.GetConnectionString();

    /// <summary>
    /// What API requests authenticate as — DML only, no ownership, no
    /// BYPASSRLS. See <see cref="RestrictedRuntimeRole"/>.
    /// </summary>
    public string RuntimeConnectionString => RestrictedRuntimeRole.ConnectionStringFor(MigratorConnectionString);

    public async Task InitializeAsync()
    {
        await DbContainer.StartAsync();
        await RestrictedRuntimeRole.EnsureAsync(MigratorConnectionString);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(MigratorConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        await RestrictedRuntimeRole.GrantAsync(MigratorConnectionString);
    }

    public async Task DisposeAsync()
    {
        await DbContainer.StopAsync();
    }
}

[CollectionDefinition("Integration Tests")]
public class IntegrationTestGroup : ICollectionFixture<PostgreSqlTestFixture>
{
}
