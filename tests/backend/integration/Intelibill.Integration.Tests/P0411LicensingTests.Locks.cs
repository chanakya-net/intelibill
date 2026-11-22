using System.IdentityModel.Tokens.Jwt;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0411LicensingTests
{
    private const string ShopRootLock = "%FROM shops WHERE id = %FOR UPDATE%";

    /// <summary>Holds the shop root row so concurrent admissions queue behind it with their earlier locks held.</summary>
    private async Task<RowHold> HoldShopRowAsync(Guid shop) =>
        await RowHold.BeginAsync(Migrator, $"SELECT 1 FROM shops WHERE id = '{shop}' FOR UPDATE");

    /// <summary>
    /// Holds the person's account row and revokes their device session inside the same uncommitted
    /// transaction: a request still passes entry authentication, then waits on the actor lock and
    /// must see the committed revocation once it proceeds.
    /// </summary>
    private async Task<RowHold> HoldAndRevokeSessionAsync(Person person)
    {
        var session = Guid.Parse(new JwtSecurityToken(person.Token).Claims.Single(x => x.Type == "sid").Value);
        return await RowHold.BeginAsync(Migrator, $"""
            SELECT 1 FROM users WHERE id = '{person.UserId}' FOR UPDATE;
            UPDATE auth_sessions SET revoked_at = now(), revocation_reason = 'p0411-revoked-under-lock'
            WHERE id = '{session}';
            """);
    }

    /// <summary>
    /// Waits until <paramref name="count"/> backends are queued on the shop root lock behind
    /// <paramref name="holder"/>, directly or behind one another, so the decision under test is
    /// known to run after that serialization point rather than after some other shared lock.
    /// </summary>
    private async Task WaitForShopWaitersAsync(RowHold holder, int count)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                WITH waiters AS (
                    SELECT pid, pg_blocking_pids(pid) AS blockers FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> @holder
                        AND query LIKE @shopLock)
                SELECT count(*)::int FROM waiters
                WHERE @holder = ANY(blockers) OR blockers && ARRAY(SELECT pid FROM waiters)
                """, connection);
            command.Parameters.AddWithValue("holder", holder.Pid);
            command.Parameters.AddWithValue("shopLock", ShopRootLock);
            if ((int)(await command.ExecuteScalarAsync(timeout.Token))! >= count) return;
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>Waits until <paramref name="count"/> backends of this database are waiting on a lock.</summary>
    private async Task WaitForLockWaitersAsync(int count)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*)::int FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0
                """, connection);
            if ((int)(await command.ExecuteScalarAsync(timeout.Token))! >= count) return;
            await Task.Delay(10, timeout.Token);
        }
    }

    /// <summary>Waits until <paramref name="operation"/> is blocked by <paramref name="holder"/>.</summary>
    private async Task WaitForBlockedByAsync(Task operation, RowHold holder)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!operation.IsCompleted)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*)::int FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND @holder = ANY(pg_blocking_pids(pid))
                """, connection);
            command.Parameters.AddWithValue("holder", holder.Pid);
            if ((int)(await command.ExecuteScalarAsync(timeout.Token))! > 0) return;
            await Task.Delay(10, timeout.Token);
        }
        Assert.Fail("The request completed before it waited on the held row.");
    }

    /// <summary>An open migrator transaction; disposal always ends it, committed only by <see cref="ReleaseAsync"/>.</summary>
    private sealed class RowHold : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private bool _ended;

        private RowHold(NpgsqlConnection connection) => _connection = connection;

        public int Pid => _connection.ProcessID;

        public static async Task<RowHold> BeginAsync(string connectionString, string statements)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(Ct);
            var hold = new RowHold(connection);
            try
            {
                await using var command = new NpgsqlCommand($"BEGIN; {statements}", connection);
                await command.ExecuteNonQueryAsync(Ct);
                return hold;
            }
            catch
            {
                await hold.DisposeAsync();
                throw;
            }
        }

        public async Task ReleaseAsync()
        {
            if (_ended) return;
            _ended = true;
            await using var command = new NpgsqlCommand("COMMIT", _connection);
            await command.ExecuteNonQueryAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            _ended = true;
            // Closing the connection rolls back anything not released.
            await _connection.DisposeAsync();
        }
    }
}
