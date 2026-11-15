using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Intelibill.P0102ConsumerRegressionTests;

/// <summary>
/// One real PostgreSQL server holding each API's own database and restricted runtime role, so
/// both hosts boot exactly as they do in an environment: the merchant host refuses a privileged
/// login and the licensing host never sees the merchant database.
/// </summary>
public sealed class SharedPostgreSqlFixture : IAsyncLifetime
{
    private const string Password = "p0102;fixture=password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("bootstrap")
        .WithUsername("bootstrap")
        .WithPassword(Password)
        .Build();

    internal NpgsqlConnectionStringBuilder MerchantRuntime => Login("merchant", "merchant_runtime");

    internal NpgsqlConnectionStringBuilder LicensingRuntime => Login("licensing", "licensing_runtime");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync(Bootstrap("bootstrap"), """
            CREATE ROLE licensing_runtime LOGIN PASSWORD 'p0102;fixture=password';
            CREATE ROLE merchant_runtime LOGIN PASSWORD 'p0102;fixture=password';
            """);
        await ExecuteAsync(Bootstrap("bootstrap"), "CREATE DATABASE licensing");
        await ExecuteAsync(Bootstrap("bootstrap"), "CREATE DATABASE merchant");
        await ExecuteAsync(Bootstrap("bootstrap"), """
            REVOKE ALL ON DATABASE licensing, merchant FROM PUBLIC;
            GRANT CONNECT ON DATABASE licensing TO licensing_runtime;
            GRANT CONNECT ON DATABASE merchant TO merchant_runtime;
            """);
        await PrepareMerchantAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task PrepareMerchantAsync()
    {
        var owner = Bootstrap("merchant");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(owner.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var context = new ApplicationDbContext(options))
            await context.Database.MigrateAsync();

        await ExecuteAsync(owner, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO merchant_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO merchant_runtime;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO merchant_runtime;
            """);
    }

    private NpgsqlConnectionStringBuilder Bootstrap(string database) => new()
    {
        Host = _container.Hostname,
        Port = _container.GetMappedPublicPort(5432),
        Database = database,
        Username = "bootstrap",
        Password = Password,
        Pooling = false,
    };

    private NpgsqlConnectionStringBuilder Login(string database, string role) =>
        new(Bootstrap(database).ConnectionString) { Username = role };

    private static async Task ExecuteAsync(NpgsqlConnectionStringBuilder settings, string sql)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedHostsGroup : ICollectionFixture<SharedPostgreSqlFixture>
{
    public const string Name = "P0102 shared hosts";
}
