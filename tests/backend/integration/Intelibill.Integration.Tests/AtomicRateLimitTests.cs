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
}
