using System.Data;
using System.Data.Common;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Intelibill.Infrastructure.Services.RateLimiting;

/// <summary>
/// Rate-limit budgets held in <c>rate_limit_buckets</c>, shared by every replica.
/// <para>
/// One acquisition is one short transaction. Each budget's row is upserted in a fixed order —
/// policy, then key, ordinal — which both creates a missing row and locks an existing one, so two
/// requests that share any pair of buckets always lock them in the same order and cannot
/// deadlock. With every row locked the decision is made in one place: either each budget is
/// incremented, or none is. A counter is never read in one statement and written back from a
/// stale copy in another, which is what lost increments under the distributed-cache limiter.
/// </para>
/// <para>
/// Windows are fixed: a bucket's window starts at the first request after the previous one
/// ended and runs for its full length however many requests arrive in it. Nothing a caller does
/// moves the end of a window, and the backoff an exhausted bucket starts runs from the first
/// refusal, not from the latest, so every refusal has an end the caller cannot push back.
/// </para>
/// </summary>
public sealed class PostgresRateLimitStore(NpgsqlDataSource dataSource, IOptions<RateLimitStoreOptions> options)
    : IAtomicRateLimitStore
{
    private const int CleanupTimeoutSeconds = 30;

    // Locks the row whether it is inserted or already there. A window that has ended starts again
    // at @now with nothing counted, and a backoff that has ended is cleared, exactly once: the
    // next request to reach this row sees the new window, not the old one.
    private const string UpsertSql = """
        INSERT INTO rate_limit_buckets AS b
            (policy_id, bucket_key, window_start, window_seconds, count, blocked_until, expires_at)
        VALUES (@policy, @key, @now, @window, 0, NULL, @now + make_interval(secs => @window))
        ON CONFLICT (policy_id, bucket_key) DO UPDATE SET
            window_start   = CASE WHEN b.window_start + make_interval(secs => b.window_seconds) <= @now
                                  THEN @now ELSE b.window_start END,
            window_seconds = CASE WHEN b.window_start + make_interval(secs => b.window_seconds) <= @now
                                  THEN @window ELSE b.window_seconds END,
            count          = CASE WHEN b.window_start + make_interval(secs => b.window_seconds) <= @now
                                  THEN 0 ELSE b.count END,
            blocked_until  = CASE WHEN b.blocked_until <= @now THEN NULL ELSE b.blocked_until END
        RETURNING window_start, window_seconds, count, blocked_until
        """;

    private const string ConsumeSql = """
        UPDATE rate_limit_buckets
        SET count = count + 1,
            expires_at = window_start + make_interval(secs => window_seconds)
        WHERE policy_id = @policy AND bucket_key = @key
        """;

    private const string BlockSql = """
        UPDATE rate_limit_buckets
        SET blocked_until = @until,
            expires_at = GREATEST(window_start + make_interval(secs => window_seconds), @until)
        WHERE policy_id = @policy AND bucket_key = @key
        """;

    // SKIP LOCKED: cleanup never waits on a bucket an acquisition holds, so it can never be one
    // side of a deadlock, and the outer predicate re-reads each row after locking it, so a bucket
    // an acquisition renewed in the meantime is left alone.
    private const string CleanupSql = """
        DELETE FROM rate_limit_buckets
        WHERE (policy_id, bucket_key) IN (
                SELECT policy_id, bucket_key
                FROM rate_limit_buckets
                WHERE expires_at <= @now
                ORDER BY expires_at
                LIMIT @batch
                FOR UPDATE SKIP LOCKED)
          AND expires_at <= @now
        """;

    private readonly RateLimitStoreOptions _options = options.Value;

    public async Task<RateDecision> AcquireAsync(IReadOnlyList<RateBudget> budgets, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        if (budgets.Count == 0)
            throw new ArgumentException("At least one budget is required.", nameof(budgets));
        foreach (var budget in budgets)
            Validate(budget);

        now = now.ToUniversalTime();
        var ordered = budgets
            .GroupBy(static b => (b.PolicyId, b.Key))
            .Select(static g => g.MinBy(static b => b.Limit)!)
            .OrderBy(static b => b.PolicyId, StringComparer.Ordinal)
            .ThenBy(static b => b.Key, StringComparer.Ordinal)
            .ToArray();
        var primary = Array.FindIndex(ordered, b => b.PolicyId == budgets[0].PolicyId && b.Key == budgets[0].Key);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.CommandTimeoutSeconds * 2));

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(deadline.Token);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, deadline.Token);

            var buckets = await LockAsync(connection, transaction, ordered, now, deadline.Token);
            var decision = Decide(ordered, buckets, primary, now, out var blocks);

            if (decision.Allowed)
                await WriteAsync(connection, transaction, ordered.Select(b => Consume(b)), deadline.Token);
            else if (blocks.Count > 0)
                await WriteAsync(connection, transaction, blocks.Select(x => Block(ordered[x.Index], x.Until)), deadline.Token);

            await transaction.CommitAsync(deadline.Token);
            return decision;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new RateLimitStoreUnavailableException(exception);
        }
        catch (DbException exception) when (!ct.IsCancellationRequested)
        {
            throw new RateLimitStoreUnavailableException(exception);
        }
        catch (TimeoutException exception)
        {
            throw new RateLimitStoreUnavailableException(exception);
        }
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        await using var command = dataSource.CreateCommand(CleanupSql);
        command.CommandTimeout = CleanupTimeoutSeconds;
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", NpgsqlDbType.TimestampTz) { TypedValue = now.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter<int>("batch", NpgsqlDbType.Integer) { TypedValue = batchSize });
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<Bucket[]> LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, RateBudget[] ordered, DateTimeOffset now, CancellationToken ct)
    {
        await using var batch = new NpgsqlBatch(connection, transaction) { Timeout = _options.CommandTimeoutSeconds };

        // A bucket held by a stalled transaction is an unavailable store, not a reason to queue
        // every request for that bucket behind it.
        var lockTimeout = new NpgsqlBatchCommand("SELECT set_config('lock_timeout', @timeout, true)");
        lockTimeout.Parameters.Add(new NpgsqlParameter<string>("timeout", NpgsqlDbType.Text) { TypedValue = $"{_options.CommandTimeoutSeconds}s" });
        batch.BatchCommands.Add(lockTimeout);

        foreach (var budget in ordered)
        {
            var upsert = new NpgsqlBatchCommand(UpsertSql);
            AddKey(upsert.Parameters, budget);
            upsert.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", NpgsqlDbType.TimestampTz) { TypedValue = now });
            upsert.Parameters.Add(new NpgsqlParameter<int>("window", NpgsqlDbType.Integer) { TypedValue = budget.WindowSeconds });
            batch.BatchCommands.Add(upsert);
        }

        var buckets = new Bucket[ordered.Length];
        await using var reader = await batch.ExecuteReaderAsync(ct);
        for (var i = 0; i < ordered.Length; i++)
        {
            await reader.NextResultAsync(ct);
            await reader.ReadAsync(ct);
            buckets[i] = new Bucket(
                WindowStart: reader.GetFieldValue<DateTimeOffset>(0),
                WindowSeconds: reader.GetInt32(1),
                Count: reader.GetInt32(2),
                BlockedUntil: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
        }

        return buckets;
    }

    private static RateDecision Decide(
        RateBudget[] ordered, Bucket[] buckets, int primary, DateTimeOffset now, out List<(int Index, DateTimeOffset Until)> blocks)
    {
        blocks = [];
        DateTimeOffset? retryAt = null;

        for (var i = 0; i < ordered.Length; i++)
        {
            var budget = ordered[i];
            var bucket = buckets[i];
            DateTimeOffset? refusedUntil = null;

            if (bucket.BlockedUntil is { } blockedUntil && blockedUntil > now)
            {
                refusedUntil = blockedUntil;
            }
            else if (bucket.Count >= budget.Limit)
            {
                refusedUntil = budget.BackoffSeconds > 0 ? now.AddSeconds(budget.BackoffSeconds) : bucket.WindowEnd;
                if (budget.BackoffSeconds > 0)
                    blocks.Add((i, refusedUntil.Value));
            }

            if (refusedUntil is not null && (retryAt is null || refusedUntil > retryAt))
                retryAt = refusedUntil;
        }

        var primaryBudget = ordered[primary];
        var primaryBucket = buckets[primary];

        if (retryAt is null)
            return new RateDecision(
                Allowed: true,
                Limit: primaryBudget.Limit,
                Remaining: Math.Max(primaryBudget.Limit - (primaryBucket.Count + 1), 0),
                ResetAt: primaryBucket.WindowEnd,
                RetryAt: null);

        return new RateDecision(
            Allowed: false,
            Limit: primaryBudget.Limit,
            Remaining: 0,
            ResetAt: primaryBucket.WindowEnd,
            RetryAt: retryAt);
    }

    private async Task WriteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<NpgsqlBatchCommand> commands, CancellationToken ct)
    {
        await using var batch = new NpgsqlBatch(connection, transaction) { Timeout = _options.CommandTimeoutSeconds };
        foreach (var command in commands)
            batch.BatchCommands.Add(command);
        await batch.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlBatchCommand Consume(RateBudget budget)
    {
        var command = new NpgsqlBatchCommand(ConsumeSql);
        AddKey(command.Parameters, budget);
        return command;
    }

    private static NpgsqlBatchCommand Block(RateBudget budget, DateTimeOffset until)
    {
        var command = new NpgsqlBatchCommand(BlockSql);
        AddKey(command.Parameters, budget);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("until", NpgsqlDbType.TimestampTz) { TypedValue = until });
        return command;
    }

    private static void AddKey(NpgsqlParameterCollection parameters, RateBudget budget)
    {
        parameters.Add(new NpgsqlParameter<string>("policy", NpgsqlDbType.Varchar) { TypedValue = budget.PolicyId });
        parameters.Add(new NpgsqlParameter<string>("key", NpgsqlDbType.Varchar) { TypedValue = budget.Key });
    }

    private static void Validate(RateBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentException.ThrowIfNullOrWhiteSpace(budget.PolicyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(budget.Key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(budget.PolicyId.Length, 200);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(budget.Key.Length, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget.WindowSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(budget.BackoffSeconds);
    }

    private readonly record struct Bucket(DateTimeOffset WindowStart, int WindowSeconds, int Count, DateTimeOffset? BlockedUntil)
    {
        public DateTimeOffset WindowEnd => WindowStart.AddSeconds(WindowSeconds);
    }
}
