using System.Collections.Concurrent;
using Intelibill.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Tests.Integration;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Intelibill.Licensing.Integration.Tests;

public class LicensingPostgreSqlFixture : IAsyncLifetime
{
    internal const string LicensingDatabase = "licensing";
    internal const string MerchantDatabase = "merchant";
    internal const string RuntimeRole = "licensing_runtime";
    internal const string MigratorRole = "licensing_migrator";
    internal const string MerchantRole = "merchant_runtime";
    private const string LicensingTemplate = "licensing_template";
    private const string Password = "integration;fixture=password";

    // Testcontainers reaps the shared server when the test process exits.
    private static readonly PostgreSqlContainer SharedContainer = BuildContainer();
    private static readonly Lazy<Task> StartShared = new(() => StartAsync(SharedContainer));
    private static readonly Lazy<Task> SharedTemplate = new(() => PrepareLicensingTemplateAsync(SharedContainer));

    private static PostgreSqlContainer BuildContainer() => new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("bootstrap")
        .WithUsername("bootstrap")
        .WithPassword(Password)
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCommand("-c", "log_statement=ddl", "-c", "log_line_prefix=%u@%d ",
            "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=500")
        .Build();

    private readonly PostgreSqlContainer _container;
    private readonly Lazy<Task> _licensingTemplate;
    private readonly bool _isolated;
    private readonly string _databaseSuffix = Guid.NewGuid().ToString("N");
    private readonly ConcurrentBag<string> _databases = [];
    private bool _holdsSlot;

    public LicensingPostgreSqlFixture() : this(isolated: false)
    {
    }

    protected LicensingPostgreSqlFixture(bool isolated)
    {
        _isolated = isolated;
        _container = isolated ? BuildContainer() : SharedContainer;
        _licensingTemplate = isolated ? new(() => PrepareLicensingTemplateAsync(_container)) : SharedTemplate;
    }

    public async Task InitializeAsync()
    {
        if (_isolated)
        {
            await StartAsync(_container);
        }
        else
        {
            await IntegrationParallelism.Slots.WaitAsync();
            _holdsSlot = true;
            await StartShared.Value;
        }

        var licensing = DatabaseName(LicensingDatabase);
        var merchant = DatabaseName(MerchantDatabase);
        await CreateEmptyLicensingDatabaseAsync(LicensingDatabase);
        await CreateDatabaseAsync(merchant, "");
        await ExecuteAsync(Bootstrap(), $"""
            REVOKE ALL ON DATABASE "{licensing}", "{merchant}" FROM PUBLIC;
            GRANT CONNECT ON DATABASE "{licensing}" TO licensing_runtime;
            GRANT CONNECT ON DATABASE "{merchant}" TO merchant_runtime;
            """);
        await ExecuteAsync(Bootstrap(LicensingDatabase), """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            """);
    }

    private static async Task StartAsync(PostgreSqlContainer container)
    {
        await container.StartAsync();
        await ExecuteAsync(Bootstrap(container, "bootstrap"), """
            CREATE ROLE licensing_runtime LOGIN PASSWORD 'integration;fixture=password';
            CREATE ROLE licensing_migrator LOGIN PASSWORD 'integration;fixture=password';
            CREATE ROLE merchant_runtime LOGIN PASSWORD 'integration;fixture=password';
            """);
    }

    public async Task DisposeAsync()
    {
        if (_isolated)
        {
            await _container.DisposeAsync();
            return;
        }

        try
        {
            var failures = new List<NpgsqlException>();
            using var commands = new NpgsqlCommandBuilder();
            while (_databases.TryTake(out var name))
            {
                var identifier = commands.QuoteIdentifier(name);
                try
                {
                    await ExecuteAsync(Bootstrap(), $"DROP DATABASE IF EXISTS {identifier} WITH (FORCE)");
                }
                catch (NpgsqlException exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count > 0)
                throw new AggregateException("Could not clean up all licensing test databases.", failures);
        }
        finally
        {
            if (_holdsSlot)
            {
                _holdsSlot = false;
                IntegrationParallelism.Slots.Release();
            }
        }
    }

    internal NpgsqlConnectionStringBuilder Bootstrap(string? database = null) =>
        Bootstrap(_container, DatabaseName(database ?? "bootstrap"));

    private string DatabaseName(string name) => !_isolated && name is LicensingDatabase or MerchantDatabase
        ? $"{name}_{_databaseSuffix}"
        : name;

    private static NpgsqlConnectionStringBuilder Bootstrap(PostgreSqlContainer container, string database) =>
        new()
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = database,
            Username = "bootstrap",
            Password = Password,
            Pooling = false,
        };

    internal NpgsqlConnectionStringBuilder Login(string database, string role) =>
        new(Bootstrap(database).ConnectionString) { Username = role, Password = Password };

    internal async Task CreateMigratedLicensingDatabaseAsync(string name)
    {
        await _licensingTemplate.Value;
        var identifier = await CreateDatabaseAsync(name, $"OWNER licensing_migrator TEMPLATE {LicensingTemplate}");
        // PostgreSQL copies schema privileges from the template, while database privileges belong to the new database.
        await ExecuteAsync(Bootstrap(), $"""
            REVOKE ALL ON DATABASE {identifier} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {identifier} TO licensing_runtime;
            """);
    }

    internal Task CreateEmptyLicensingDatabaseAsync(string name) =>
        CreateDatabaseAsync(DatabaseName(name), "OWNER licensing_migrator");

    private async Task<string> CreateDatabaseAsync(string name, string options)
    {
        using var commands = new NpgsqlCommandBuilder();
        var identifier = commands.QuoteIdentifier(name);
        await ExecuteAsync(Bootstrap(), $"CREATE DATABASE {identifier} {options}");
        _databases.Add(name);
        return identifier;
    }

    private static async Task PrepareLicensingTemplateAsync(PostgreSqlContainer container)
    {
        var admin = Bootstrap(container, "bootstrap");
        await ExecuteAsync(admin, $"CREATE DATABASE {LicensingTemplate} OWNER licensing_migrator");
        await ExecuteAsync(admin, $"REVOKE ALL ON DATABASE {LicensingTemplate} FROM PUBLIC");
        var template = Bootstrap(container, LicensingTemplate);
        template.Username = MigratorRole;
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(template.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        // Fixture connections are unpooled. Disposing the context leaves no session attached to the template.
        await using (var context = new LicensingDbContext(options))
            await context.Database.MigrateAsync();
        await ExecuteAsync(admin, $"ALTER DATABASE {LicensingTemplate} WITH ALLOW_CONNECTIONS false IS_TEMPLATE true");
    }

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
            GRANT EXECUTE ON FUNCTION public.claim_entitlement_delivery(timestamptz,uuid) TO merchant_runtime;
            GRANT EXECUTE ON FUNCTION public.finish_entitlement_delivery(uuid,uuid,timestamptz,boolean,boolean,text) TO merchant_runtime;
            GRANT EXECUTE ON FUNCTION public.due_merchant_entitlement_reconciliation(timestamptz) TO merchant_runtime;
            """);
    }

    internal async Task SetConnectionsAsync(string database, bool allowed)
    {
        database = DatabaseName(database);
        Assert.True(_databases.Contains(database, StringComparer.Ordinal),
            "Only databases created by this Testcontainers fixture can be paused.");
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

// Cluster-level privilege tests run serially. Migration tests already hold their outer fixture's slot.
public sealed class IsolatedLicensingPostgreSqlFixture() : LicensingPostgreSqlFixture(isolated: true);
