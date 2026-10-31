using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Proves the harness itself is production-shaped: requests run as a role that
/// holds DML and nothing more, and the session context the policies read is set
/// on every connection the pool hands out. A green isolation suite proves
/// nothing if it was collected as a superuser, so these four assertions guard
/// every test that follows.
/// </summary>
[Collection("Integration Tests")]
public sealed class RestrictedRoleRlsTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<NpgsqlConnection> OpenRuntimeConnectionAsync()
    {
        var connection = new NpgsqlConnection(_factory.RuntimeConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    [Fact]
    public async Task RuntimeRole_IsNotSuperuserAndCannotBypassRls()
    {
        await using var connection = await OpenRuntimeConnectionAsync();

        await using var command = new NpgsqlCommand(
            "select current_user, rolsuper, rolbypassrls from pg_roles where rolname = current_user",
            connection);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(RestrictedRuntimeRole.Name, reader.GetString(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
    }

    [Fact]
    public async Task RuntimeRole_DoesNotOwnTenantTables()
    {
        await using var connection = await OpenRuntimeConnectionAsync();

        await using var ownedCommand = new NpgsqlCommand(
            "select count(*) from pg_tables where schemaname = 'public' and tableowner = current_user",
            connection);
        var owned = (long)(await ownedCommand.ExecuteScalarAsync())!;

        await using var ownerCommand = new NpgsqlCommand(
            "select tableowner from pg_tables where schemaname = 'public' and tablename = 'items'",
            connection);
        var itemsOwner = (string)(await ownerCommand.ExecuteScalarAsync())!;

        Assert.Equal(0, owned);
        Assert.NotEqual(RestrictedRuntimeRole.Name, itemsOwner);
    }

    [Fact]
    public async Task RuntimeRole_CannotDisableRowLevelSecurity()
    {
        await using var connection = await OpenRuntimeConnectionAsync();

        await using var disable = new NpgsqlCommand(
            "alter table discount_rules disable row level security",
            connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => disable.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task SessionContext_IsSetOnEveryPooledConnection()
    {
        var session = new MutableSessionContext();
        await using var factory = new ApiWebApplicationFactory(
            fixture,
            services =>
            {
                services.RemoveAll<ICurrentSessionContext>();
                services.AddScoped<ICurrentSessionContext>(_ => session);
            },
            maxPoolSize: 1);
        await factory.InitializeAsync();

        // More iterations than the pool holds, so every read after the first is
        // served by a physical connection a previous iteration already used.
        for (var iteration = 0; iteration < 8; iteration++)
        {
            session.UserId = Guid.NewGuid();
            session.ActiveShopId = Guid.NewGuid();

            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await context.Database.OpenConnectionAsync();
            try
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText =
                    "select current_setting('app.active_shop_id', true), current_setting('app.current_user_id', true)";
                await using var reader = await command.ExecuteReaderAsync();

                Assert.True(await reader.ReadAsync());
                Assert.Equal(session.ActiveShopId!.Value.ToString(), reader.GetString(0));
                Assert.Equal(session.UserId!.Value.ToString(), reader.GetString(1));
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }

}
