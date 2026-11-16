using System.Net;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0212Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0212Tests
{
    [Fact]
    public async Task CompetingConfirmationsCommitExactlyOneGrantPerRevision()
    {
        await using var fixture = await StartAsync(database);
        var (left, seed) = await fixture.ManagerAsync();
        using var leftSession = left;
        using var right = await fixture.Provider.SignInOperatorAsync(seed);
        var (account, _, shops) = await fixture.SeedAccountAsync(2);
        var (other, _, otherShops) = await fixture.SeedAccountAsync(1);

        // Same key, same body, two sessions: one grant and an exact replay.
        var preview = await PreviewAsync(left, account, Draft([shops[0]]));
        var key = Guid.NewGuid();
        var same = await RaceAsync(fixture, account,
            () => PostAsync(left, Base(account), Confirm(preview, key)), () => PostAsync(right, Base(account), Confirm(preview, key)));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], same.Select(item => item.Status).Order().ToArray());
        Assert.Equal(same[0].Body, same[1].Body);
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));

        // Different keys from one preview: one winner, one stale-version conflict, one revision increment.
        var next = await PreviewAsync(left, account, Draft([shops[1]]));
        var distinct = await RaceAsync(fixture, account,
            () => PostAsync(left, Base(account), Confirm(next, Guid.NewGuid())),
            () => PostAsync(right, Base(account), Confirm(next, Guid.NewGuid())));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], distinct.Select(item => item.Status).Order().ToArray());
        Assert.Contains("Grant.VersionConflict", distinct.Single(item => item.Status == HttpStatusCode.Conflict).Body);
        Assert.Equal(2L, await fixture.CountAsync($"SELECT revision FROM complimentary_grant_scopes WHERE account_id = '{account:D}'"));

        // Another account is not behind this account's fence.
        await using var fence = await HoldAccountFenceAsync(fixture, account);
        var independent = await GrantAsync(left, other, Draft(otherShops));
        Assert.Equal(1, independent.GetProperty("grantedVersion").GetInt64());
        await fence.Transaction.RollbackAsync();
        Assert.Equal(3L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
    }

    private static async Task<(HttpStatusCode Status, string Body)[]> RaceAsync(P0212Fixture fixture, Guid account,
        Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        HttpResponseMessage[] responses;
        await using (var fence = await HoldAccountFenceAsync(fixture, account))
        {
            var requests = new[] { first(), second() };
            await WaitForAdvisoryWaitersAsync(fixture, fence.Connection.ProcessID, 2);
            await fence.Transaction.CommitAsync();
            responses = await Task.WhenAll(requests);
        }
        var results = new List<(HttpStatusCode, string)>();
        foreach (var response in responses)
            using (response)
                results.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        return [.. results];
    }

    internal sealed record Fence(NpgsqlConnection Connection, NpgsqlTransaction Transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    /// <summary>Holds the same account advisory fence trial activation and grants take, on its own connection.</summary>
    internal static async Task<Fence> HoldAccountFenceAsync(P0212Fixture fixture, Guid account)
    {
        var connection = new NpgsqlConnection(fixture.Owner.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction);
        take.Parameters.AddWithValue("b" + account.ToString("D"));
        await take.ExecuteNonQueryAsync();
        return new Fence(connection, transaction);
    }

    /// <summary>Observed PostgreSQL waiters blocked (directly or through each other) by the fence holder; no sleeps.</summary>
    internal static async Task WaitForAdvisoryWaitersAsync(P0212Fixture fixture, int blocker, long expected)
    {
        await using var observer = new NpgsqlConnection(fixture.Owner.ConnectionString);
        await observer.OpenAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory'
                AND ($1 = ANY(pg_blocking_pids(pid)) OR EXISTS (
                    SELECT FROM unnest(pg_blocking_pids(pid)) AS previous WHERE $1 = ANY(pg_blocking_pids(previous))))
            """, observer);
        count.Parameters.AddWithValue(blocker);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((long)(await count.ExecuteScalarAsync(timeout.Token))! != expected)
            await Task.Delay(15, timeout.Token);
    }
}
