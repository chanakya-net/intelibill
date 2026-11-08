using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>Pauses a real posting after its INSERT while its business row locks remain held.</summary>
internal sealed class TillPostgresBarrier : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly string _table;
    private readonly string _name = $"till_barrier_{Guid.NewGuid():N}";
    private readonly long _key = Random.Shared.NextInt64(1, long.MaxValue);
    private bool _released;
    private TillPostgresBarrier(NpgsqlConnection connection, string table) { _connection = connection; _table = table; }

    public static async Task<TillPostgresBarrier> CreateAsync(string connection, string table, Guid shop)
    {
        // Table comes only from test literals; the random identifier isolates trigger cleanup.
        var result = new TillPostgresBarrier(new NpgsqlConnection(connection), table);
        await result._connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT pg_advisory_lock({result._key});
            CREATE FUNCTION {result._name}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN PERFORM pg_advisory_xact_lock({result._key}); RETURN NEW; END $$;
            CREATE TRIGGER {result._name} AFTER INSERT ON {table}
            FOR EACH ROW WHEN (NEW.shop_id='{shop}') EXECUTE FUNCTION {result._name}();
            """, result._connection);
        await command.ExecuteNonQueryAsync();
        return result;
    }

    public Task<int> WaitForArrivalAsync() => WaitForBlockedByAsync(_connection.ProcessID);

    public async Task<int> WaitForBlockedByAsync(int blockerPid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE wait_event_type='Lock' AND @blocker=ANY(pg_blocking_pids(pid)) LIMIT 1", _connection);
            command.Parameters.AddWithValue("blocker", blockerPid);
            var value = await command.ExecuteScalarAsync(timeout.Token);
            if (value is int pid) return pid;
            await Task.Delay(10, timeout.Token);
        }
    }

    public async Task ReleaseAsync()
    {
        if (_released) return;
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({_key})", _connection);
        await command.ExecuteNonQueryAsync();
        _released = true;
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        await using var command = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {_name} ON {_table}; DROP FUNCTION IF EXISTS {_name}();", _connection);
        await command.ExecuteNonQueryAsync();
        await _connection.DisposeAsync();
    }
}
