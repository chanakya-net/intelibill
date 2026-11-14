using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class LicensingPostgreSqlFixture : IAsyncLifetime
{
    internal const string LicensingDatabase = "licensing";
    internal const string MerchantDatabase = "merchant";
    internal const string RuntimeRole = "licensing_runtime";
    internal const string MigratorRole = "licensing_migrator";
    internal const string MerchantRole = "merchant_runtime";
    private const string Password = "integration;fixture=password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("bootstrap")
        .WithUsername("bootstrap")
        .WithPassword(Password)
        .WithCommand("-c", "log_statement=ddl", "-c", "log_line_prefix=%u@%d ")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync(Bootstrap(), """
            CREATE ROLE licensing_runtime LOGIN PASSWORD 'integration;fixture=password';
            CREATE ROLE licensing_migrator LOGIN PASSWORD 'integration;fixture=password';
            CREATE ROLE merchant_runtime LOGIN PASSWORD 'integration;fixture=password';
            """);
        await ExecuteAsync(Bootstrap(), "CREATE DATABASE licensing OWNER licensing_migrator");
        await ExecuteAsync(Bootstrap(), "CREATE DATABASE merchant");
        await ExecuteAsync(Bootstrap(), """
            REVOKE ALL ON DATABASE licensing, merchant FROM PUBLIC;
            GRANT CONNECT ON DATABASE licensing TO licensing_runtime;
            GRANT CONNECT ON DATABASE merchant TO merchant_runtime;
            """);
        await ExecuteAsync(Bootstrap(LicensingDatabase), """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            """);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    internal NpgsqlConnectionStringBuilder Bootstrap(string? database = null) =>
        new()
        {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = database ?? "bootstrap",
            Username = "bootstrap",
            Password = Password,
            Pooling = false,
        };

    internal NpgsqlConnectionStringBuilder Login(string database, string role) =>
        new(Bootstrap(database).ConnectionString) { Username = role, Password = Password };

    internal async Task PrepareMerchantAsync()
    {
        var owner = Bootstrap(MerchantDatabase);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(owner.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        await ExecuteAsync(owner, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO merchant_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO merchant_runtime;
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO merchant_runtime;
            """);
    }

    internal async Task SetConnectionsAsync(string database, bool allowed)
    {
        Assert.Contains(database, new[] { LicensingDatabase, MerchantDatabase });
        await ExecuteAsync(Bootstrap(), $"""ALTER DATABASE "{database}" ALLOW_CONNECTIONS {allowed}""");
        if (!allowed)
        {
            await ExecuteAsync(Bootstrap(), $"""
                SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE datname = '{database}';
                """);
        }
    }

    internal async Task<string[]> ReadRuntimeDdlAsync()
    {
        var (_, stderr) = await _container.GetLogsAsync();
        var lines = stderr.Split('\n');
        Assert.Contains(lines, line => line.Contains("licensing_migrator@licensing LOG:", StringComparison.Ordinal)
            && line.Contains("CREATE TABLE public.licensing_probe", StringComparison.Ordinal));
        return lines.Where(line => line.Contains("licensing_runtime@licensing LOG:", StringComparison.Ordinal)
            && (line.Contains("statement:", StringComparison.Ordinal) || line.Contains("execute ", StringComparison.Ordinal))).ToArray();
    }

    internal Task ChangeRuntimeRoleAsync(string sql) => ExecuteAsync(Bootstrap(LicensingDatabase), sql);

    internal Task CreateProbeTableAsync() => ExecuteAsync(Login(LicensingDatabase, MigratorRole), """
        CREATE TABLE public.licensing_probe (value integer NOT NULL);
        INSERT INTO public.licensing_probe VALUES (42);
        GRANT SELECT, INSERT ON public.licensing_probe TO licensing_runtime;
        """);

    internal Task DropProbeTableAsync() => ExecuteAsync(
        Login(LicensingDatabase, MigratorRole), "DROP TABLE public.licensing_probe");

    internal async Task<CatalogSnapshot> ReadLicensingCatalogAsync()
    {
        await using var connection = new NpgsqlConnection(Bootstrap(LicensingDatabase).ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT coalesce(string_agg(nspname, ',' ORDER BY nspname), '')
            FROM pg_namespace WHERE nspname !~ '^pg_' AND nspname <> 'information_schema';
            SELECT coalesce(string_agg(n.nspname || '.' || c.relname, ',' ORDER BY n.nspname, c.relname), '')
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema';
            """, connection);
        string schemas;
        string tables;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            schemas = reader.GetString(0);
            await reader.NextResultAsync();
            await reader.ReadAsync();
            tables = reader.GetString(0);
        }
        command.CommandText = "SELECT value FROM public.licensing_probe ORDER BY value LIMIT 1";
        var value = tables.Contains("public.licensing_probe", StringComparison.Ordinal)
            ? (int?)await command.ExecuteScalarAsync()
            : null;
        return new CatalogSnapshot(schemas, tables, value);
    }

    internal sealed record CatalogSnapshot(string Schemas, string Tables, int? ProbeValue);

    internal static async Task<object?> ReadScalarAsync(NpgsqlConnectionStringBuilder settings, string sql)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    internal static async Task ExecuteAsync(NpgsqlConnectionStringBuilder settings, string sql)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
