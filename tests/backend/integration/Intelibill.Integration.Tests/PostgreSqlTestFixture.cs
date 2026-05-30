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

    public async Task InitializeAsync()
    {
        await DbContainer.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(DbContainer.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
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
