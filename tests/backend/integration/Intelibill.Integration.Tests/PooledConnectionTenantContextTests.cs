using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The shop context lives in a session variable on a connection the pool
/// hands round, so the question that decides whether any of this holds is:
/// can one request's shop be carried into the next? Every test here pins the
/// pool to a single physical connection, so each operation is served by the
/// connection a previous one used — the arrangement most likely to leak, made
/// certain instead of left to chance.
/// </summary>
[Collection("Integration Tests")]
public sealed class PooledConnectionTenantContextTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly MutableSessionContext _session = new();
    private ApiWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiWebApplicationFactory(
            fixture,
            services =>
            {
                services.RemoveAll<ICurrentSessionContext>();
                services.AddScoped<ICurrentSessionContext>(_ => _session);
            },
            maxPoolSize: 1,
            runtimeIdentityOutsideRequests: true);
        await _factory.InitializeAsync();
    }

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

    private const string ReadContextSql =
        "select coalesce(current_setting('app.active_shop_id', true), ''), coalesce(current_setting('app.current_user_id', true), '')";

    /// <summary>
    /// Runs one scope's worth of work the way a request does: a scope of its
    /// own, a DbContext resolved inside it, and a connection the pool opens
    /// when the first statement runs.
    /// </summary>
    private async Task AssertScopeSeesOwnContextAsync(Guid? shopId, bool openSynchronously = false)
    {
        _session.UserId = Guid.NewGuid();
        _session.ActiveShopId = shopId;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (openSynchronously)
        {
            context.Database.OpenConnection();
        }
        else
        {
            await context.Database.OpenConnectionAsync();
        }

        try
        {
            var (shop, user) = await ReadContextAsync(context);
            Assert.Equal(_session.ExpectedShopSetting, shop);
            Assert.Equal(_session.ExpectedUserSetting, user);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<(string Shop, string User)> ReadContextAsync(ApplicationDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = ReadContextSql;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    [Fact]
    public async Task PooledConnection_A_B_Anonymous_DoesNotLeak()
    {
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();

        // Four passes over the cycle, so the single pooled connection is reused
        // many times and every reuse is checked.
        for (var pass = 0; pass < 4; pass++)
        {
            await AssertScopeSeesOwnContextAsync(shopA);
            await AssertScopeSeesOwnContextAsync(shopB);
            await AssertScopeSeesOwnContextAsync(null);
        }
    }

    [Fact]
    public async Task PooledConnection_SynchronousOpen_SetsOwnContext()
    {
        // The interceptor hooks both ConnectionOpened and ConnectionOpenedAsync;
        // a shop that was only applied on the async path would leak here.
        await AssertScopeSeesOwnContextAsync(Guid.NewGuid(), openSynchronously: true);
        await AssertScopeSeesOwnContextAsync(Guid.NewGuid(), openSynchronously: false);
        await AssertScopeSeesOwnContextAsync(null, openSynchronously: true);
    }

    [Fact]
    public async Task PooledConnection_InsideExplicitTransaction_SeesOwnContext()
    {
        var shop = Guid.NewGuid();
        _session.UserId = Guid.NewGuid();
        _session.ActiveShopId = shop;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();

            var (inTransaction, _) = await ReadContextAsync(context);
            Assert.Equal(shop.ToString(), inTransaction);

            await transaction.CommitAsync();
        }

        await AssertScopeSeesOwnContextAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task PooledConnection_AfterCancelledOperation_DoesNotLeak()
    {
        var abandoned = Guid.NewGuid();
        _session.UserId = Guid.NewGuid();
        _session.ActiveShopId = abandoned;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => context.Database.ExecuteSqlRawAsync("select pg_sleep(10)", cancellation.Token));
        }

        // Whether the pool recovered that connection or discarded it, the next
        // scope must see its own shop and never the abandoned one.
        var next = Guid.NewGuid();
        Assert.NotEqual(abandoned, next);
        await AssertScopeSeesOwnContextAsync(next);
        await AssertScopeSeesOwnContextAsync(null);
    }

    [Fact]
    public async Task PooledConnection_AlreadyOpen_KeepsOneContextForTheWholeOperation()
    {
        var shop = Guid.NewGuid();
        _session.UserId = Guid.NewGuid();
        _session.ActiveShopId = shop;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.OpenConnectionAsync();
        try
        {
            var (before, _) = await ReadContextAsync(context);
            await context.Database.ExecuteSqlRawAsync("select 1");
            _ = await context.Users.AsNoTracking().Take(1).ToListAsync();
            var (after, _) = await ReadContextAsync(context);

            // Nothing outside ITenantWriteScope may move the shop mid-operation.
            Assert.Equal(shop.ToString(), before);
            Assert.Equal(before, after);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task PooledConnection_AfterNonEfTraffic_DoesNotInheritItsContext()
    {
        // Cache traffic and health probes open connections without the EF
        // interceptor, so nothing sets a shop on them. With the pool pinned to
        // one connection, this is the same physical connection EF will be
        // handed next.
        var strangerShop = Guid.NewGuid();
        await using (var raw = new NpgsqlConnection(_factory.RuntimeConnectionString))
        {
            await raw.OpenAsync();
            await using var pollute = raw.CreateCommand();
            pollute.CommandText = "select set_config('app.active_shop_id', @shop, false)";
            pollute.Parameters.AddWithValue("shop", strangerShop.ToString());
            await pollute.ExecuteNonQueryAsync();
        }

        var own = Guid.NewGuid();
        Assert.NotEqual(strangerShop, own);
        await AssertScopeSeesOwnContextAsync(own);
        await AssertScopeSeesOwnContextAsync(null);
    }
}
