using Intelibill.Licensing.Application.Bootstrap;
using Npgsql;
using NpgsqlTypes;

namespace Intelibill.Licensing.Infrastructure.Bootstrap;

internal sealed class PostgresBootstrapRateBudget(NpgsqlDataSource dataSource) : IBootstrapRateBudget
{
    public async Task<DateTimeOffset?> TakeAsync(bool isResume, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '3s'", connection, transaction);
        await timeout.ExecuteNonQueryAsync(cancellationToken);
        var global = await TakeBucketAsync(connection, transaction, 1, 30, now, TimeSpan.FromMinutes(5), cancellationToken);
        var reset = global ?? await TakeBucketAsync(connection, transaction, isResume ? 3 : 2, 5, now,
            TimeSpan.FromMinutes(15), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reset;
    }

    public async Task<DateTimeOffset?> TakeInvitationAsync(bool isAcceptance, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '3s'", connection, transaction);
        await timeout.ExecuteNonQueryAsync(cancellationToken);
        var reset = isAcceptance
            ? await TakeBucketAsync(connection, transaction, 4, 30, now, TimeSpan.FromMinutes(15), cancellationToken)
            : await TakeBucketAsync(connection, transaction, 5, 20, now, TimeSpan.FromHours(1), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reset;
    }

    private static async Task<DateTimeOffset?> TakeBucketAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        int id, int limit, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.bootstrap_rate_buckets (id, window_ends_at, attempts)
            VALUES (@id, @next, 1)
            ON CONFLICT (id) DO UPDATE SET
                window_ends_at = CASE WHEN bootstrap_rate_buckets.window_ends_at <= @now
                    THEN @next ELSE bootstrap_rate_buckets.window_ends_at END,
                attempts = CASE WHEN bootstrap_rate_buckets.window_ends_at <= @now
                    THEN 1 ELSE LEAST(bootstrap_rate_buckets.attempts + 1, @limit + 1) END
            RETURNING CASE WHEN attempts > @limit THEN window_ends_at END
            """, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, id);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("next", NpgsqlDbType.TimestampTz, now + duration);
        var reset = await command.ExecuteScalarAsync(cancellationToken);
        return reset is DateTime time ? new DateTimeOffset(time) : null;
    }
}
