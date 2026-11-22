using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Intelibill.Tests.Integration;

namespace Intelibill.Integration.Tests;

/// <summary>
/// One private, fully migrated database for one test class, inside the PostgreSQL
/// server the whole run shares (<see cref="SharedPostgres"/>).
/// </summary>
/// <remarks>
/// <see cref="IntegrationCollectionFactory"/> gives every class marked
/// <c>[Collection("Integration Tests")]</c> a collection, and therefore a fixture, of
/// its own. Classes run next to each other; the tests inside one class still run one
/// at a time and may reset this database between tests with
/// <see cref="ResetDatabaseAsync"/>.
/// </remarks>
public class PostgreSqlTestFixture : IAsyncLifetime
{
    /// <summary>The database this class owns. No other class connects to it.</summary>
    public string DatabaseName { get; } = "it_" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// The server's own superuser on this class's database: it owns the schema and
    /// is the only identity a test may use to inspect rows the policies would
    /// otherwise hide.
    /// </summary>
    public string MigratorConnectionString => SharedPostgres.ConnectionStringFor(DatabaseName);

    /// <summary>
    /// What API requests authenticate as — DML only, no ownership, no
    /// BYPASSRLS. See <see cref="RestrictedRuntimeRole"/>.
    /// </summary>
    public string RuntimeConnectionString => RestrictedRuntimeRole.ConnectionStringFor(MigratorConnectionString);

    public async Task InitializeAsync()
    {
        // Held until the class is done: this, not xUnit's thread count, decides how
        // many classes run at once. See IntegrationParallelism.
        await IntegrationParallelism.Slots.WaitAsync();
        await SharedPostgres.StartAsync();
        await SharedPostgres.CreateFromTemplateAsync(DatabaseName);
    }

    /// <summary>
    /// Replaces this class's database with a fresh copy of the migrated template:
    /// every table empty, every migration applied, the runtime role's privileges in
    /// place. Costs one file copy instead of a replay of the migration chain.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await SharedPostgres.DropAsync(DatabaseName);
        await SharedPostgres.CreateFromTemplateAsync(DatabaseName);
    }

    public async Task DisposeAsync()
    {
        try
        {
            await SharedPostgres.DropAsync(DatabaseName);
        }
        finally
        {
            IntegrationParallelism.Slots.Release();
        }
    }
}

/// <summary>
/// The single PostgreSQL server of a test run and the migrated template database
/// every test database is copied from. Started on first use, removed with the test
/// process by Testcontainers' resource reaper.
/// </summary>
internal static class SharedPostgres
{
    private const string TemplateDatabase = "integration_template";

    private static readonly Lazy<Task> Started = new(StartCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    // Durability is switched off and the data directory lives in memory: nothing in
    // this server outlives the run, and creating or dropping a database is the most
    // frequent thing the suite asks of it.
    public static PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("integration")
        .WithUsername("integration")
        .WithPassword("integration")
        .WithTmpfsMount("/var/lib/postgresql/data")
        .WithCommand(
            "-c", "fsync=off",
            "-c", "synchronous_commit=off",
            "-c", "full_page_writes=off",
            "-c", "max_connections=500")
        .Build();

    public static Task StartAsync() => Started.Value;

    public static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = database }.ConnectionString;

    public static async Task CreateFromTemplateAsync(string database)
    {
        await ExecuteAdminAsync($"CREATE DATABASE \"{database}\" TEMPLATE {TemplateDatabase}");
    }

    public static async Task DropAsync(string database)
    {
        // Sessions the previous test left in a pool belong to the database being
        // dropped. Closing them here keeps a later test from being handed one.
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)");
    }

    private static async Task StartCoreAsync()
    {
        await Container.StartAsync();

        var admin = Container.GetConnectionString();
        await RestrictedRuntimeRole.EnsureAsync(admin);
        await ExecuteAdminAsync($"CREATE DATABASE {TemplateDatabase}");

        // Unpooled: a session left open on the template would refuse every copy.
        var template = new NpgsqlConnectionStringBuilder(admin) { Database = TemplateDatabase, Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(template)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var context = new ApplicationDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        // Privileges are database objects, so each copy inherits them.
        await RestrictedRuntimeRole.GrantAsync(template);
        await ExecuteAdminAsync($"ALTER DATABASE {TemplateDatabase} WITH ALLOW_CONNECTIONS false IS_TEMPLATE true");
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public class IntegrationTestGroup : ICollectionFixture<PostgreSqlTestFixture>
{
    public const string Name = "Integration Tests";
}

/// <summary>
/// For the few classes that cannot share the machine with other classes: those that
/// observe state the whole test process shares (Serilog's static logger, which every
/// started API host replaces), and those whose assertions are short wall-clock windows
/// ("a live notification arrives within two seconds", "nothing arrives for two
/// seconds"), which a busy CI runner stretches. xUnit runs this collection alone,
/// after the parallel classes have finished.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SerialIntegrationTestGroup : ICollectionFixture<PostgreSqlTestFixture>
{
    public const string Name = "Integration Tests (serial)";
}
