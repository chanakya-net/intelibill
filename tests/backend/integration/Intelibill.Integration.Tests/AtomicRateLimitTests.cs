using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.RateLimiting;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The budget store against real PostgreSQL, as the restricted runtime role. Each store gets its
/// own data source, so two stores are two replicas: nothing in-process is shared between them.
/// </summary>
[Collection("Integration Tests")]
public sealed class AtomicRateLimitTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ApiWebApplicationFactory _factory = new(fixture);
    private readonly List<NpgsqlDataSource> _dataSources = [];

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var dataSource in _dataSources)
            dataSource.Dispose();
        _dataSources.Clear();
        _factory.Dispose();
    }

    [Fact]
    public async Task TwoInstances_ParallelBurst_AdmitsExactlyLimit()
    {
        var replicas = new[] { CreateStore(), CreateStore() };
        var budget = new RateBudget("test:burst", "burst-key", Limit: 10, WindowSeconds: 60);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 100).Select(async i =>
        {
            await release.Task;
            return await replicas[i % 2].AcquireAsync([budget], T0, CancellationToken.None);
        }).ToArray();
        release.SetResult();
        var decisions = await Task.WhenAll(attempts);

        Assert.Equal(10, decisions.Count(d => d.Allowed));
        Assert.Equal(90, decisions.Count(d => !d.Allowed));
        Assert.Equal(10, await PersistedCountAsync("test:burst", "burst-key"));
    }

    [Fact]
    public async Task MultipleBudgets_DenialDoesNotPartiallyConsume()
    {
        var store = CreateStore();
        var wide = new RateBudget("test:multi-wide", "k", Limit: 5, WindowSeconds: 60);
        var narrow = new RateBudget("test:multi-narrow", "k", Limit: 1, WindowSeconds: 60);

        var first = await store.AcquireAsync([wide, narrow], T0, CancellationToken.None);
        var second = await store.AcquireAsync([wide, narrow], T0.AddSeconds(1), CancellationToken.None);

        Assert.True(first.Allowed);
        Assert.False(second.Allowed);
        Assert.Equal(1, await PersistedCountAsync("test:multi-wide", "k"));
        Assert.Equal(1, await PersistedCountAsync("test:multi-narrow", "k"));
    }

    [Fact]
    public async Task WindowBoundary_ResetsOnce()
    {
        var store = CreateStore();
        var budget = new RateBudget("test:boundary", "k", Limit: 2, WindowSeconds: 60);
        await store.AcquireAsync([budget], T0, CancellationToken.None);
        await store.AcquireAsync([budget], T0, CancellationToken.None);
        var boundary = T0.AddSeconds(60);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            await release.Task;
            return await store.AcquireAsync([budget], boundary, CancellationToken.None);
        }).ToArray();
        release.SetResult();
        var decisions = await Task.WhenAll(attempts);

        Assert.Equal(2, decisions.Count(d => d.Allowed));
        Assert.Equal(boundary, await PersistedWindowStartAsync("test:boundary", "k"));
        Assert.All(decisions, d => Assert.Equal(boundary.AddSeconds(60), d.ResetAt));
    }

    [Fact]
    public async Task ReorderedBudgets_DoNotDeadlock()
    {
        var replicas = new[] { CreateStore(), CreateStore() };
        var a = new RateBudget("test:order-a", "k", Limit: 1000, WindowSeconds: 60);
        var b = new RateBudget("test:order-b", "k", Limit: 1000, WindowSeconds: 60);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, 50).Select(async i =>
        {
            await release.Task;
            RateBudget[] budgets = i % 2 == 0 ? [a, b] : [b, a];
            return await replicas[i % 2].AcquireAsync(budgets, T0, CancellationToken.None);
        }).ToArray();
        release.SetResult();
        var all = Task.WhenAll(attempts);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(all, finished);
        Assert.All(await all, d => Assert.True(d.Allowed));
        Assert.Equal(50, await PersistedCountAsync("test:order-a", "k"));
        Assert.Equal(50, await PersistedCountAsync("test:order-b", "k"));
    }

    [Fact]
    public async Task BudgetExpiry_AllowsRecoveryAgain()
    {
        var store = CreateStore();
        var budget = new RateBudget("test:recovery", "k", Limit: 1, WindowSeconds: 60, BackoffSeconds: 300);

        var admitted = await store.AcquireAsync([budget], T0, CancellationToken.None);
        var refused = await store.AcquireAsync([budget], T0.AddSeconds(1), CancellationToken.None);
        var stillCooling = await store.AcquireAsync([budget], T0.AddSeconds(120), CancellationToken.None);
        var recovered = await store.AcquireAsync([budget], T0.AddSeconds(302), CancellationToken.None);

        Assert.True(admitted.Allowed);
        Assert.False(refused.Allowed);
        Assert.Equal(T0.AddSeconds(301), refused.RetryAt);
        Assert.False(stillCooling.Allowed);
        Assert.Equal(T0.AddSeconds(301), stillCooling.RetryAt);
        Assert.True(recovered.Allowed);
    }

    [Fact]
    public async Task Cleanup_DoesNotDeleteActiveWindow()
    {
        var store = CreateStore();
        var expired = new RateBudget("test:cleanup-expired", "k", Limit: 5, WindowSeconds: 60);
        var active = new RateBudget("test:cleanup-active", "k", Limit: 5, WindowSeconds: 600);
        var cooling = new RateBudget("test:cleanup-cooling", "k", Limit: 1, WindowSeconds: 60, BackoffSeconds: 900);
        await store.AcquireAsync([expired], T0, CancellationToken.None);
        await store.AcquireAsync([active], T0, CancellationToken.None);
        await store.AcquireAsync([cooling], T0, CancellationToken.None);
        await store.AcquireAsync([cooling], T0.AddSeconds(1), CancellationToken.None);

        var deleted = await store.DeleteExpiredAsync(T0.AddSeconds(120), batchSize: 100, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Null(await PersistedCountAsync("test:cleanup-expired", "k"));
        Assert.Equal(1, await PersistedCountAsync("test:cleanup-active", "k"));
        Assert.Equal(1, await PersistedCountAsync("test:cleanup-cooling", "k"));
    }

    private PostgresRateLimitStore CreateStore()
    {
        // A replica's pool is bounded; two default pools of 100 would exceed the container's own
        // connection ceiling before they exceeded anything the store does.
        var connectionString = new NpgsqlConnectionStringBuilder(_factory.RuntimeConnectionString) { MaxPoolSize = 20 };
        var dataSource = NpgsqlDataSource.Create(connectionString.ConnectionString);
        _dataSources.Add(dataSource);
        return new PostgresRateLimitStore(dataSource, Microsoft.Extensions.Options.Options.Create(new RateLimitStoreOptions()));
    }

    private async Task<int?> PersistedCountAsync(string policyId, string key)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count FROM rate_limit_buckets WHERE policy_id = @policy AND bucket_key = @key", connection);
        command.Parameters.AddWithValue("policy", policyId);
        command.Parameters.AddWithValue("key", key);
        return (int?)await command.ExecuteScalarAsync();
    }

    private async Task<DateTimeOffset> PersistedWindowStartAsync(string policyId, string key)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT window_start FROM rate_limit_buckets WHERE policy_id = @policy AND bucket_key = @key", connection);
        command.Parameters.AddWithValue("policy", policyId);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetFieldValue<DateTimeOffset>(0);
    }
}
