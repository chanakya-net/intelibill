using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using NpgsqlTypes;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class SwappableTimeProvider(TimeProvider initial) : TimeProvider
{
    private TimeProvider _current = initial;

    internal void Set(TimeProvider provider) => Volatile.Write(ref _current, provider);

    public override DateTimeOffset GetUtcNow() => Volatile.Read(ref _current).GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => Volatile.Read(ref _current).LocalTimeZone;

    public override long GetTimestamp() => Volatile.Read(ref _current).GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        Volatile.Read(ref _current).CreateTimer(callback, state, dueTime, period);
}

internal sealed class SwappableHttpMessageHandler : HttpMessageHandler
{
    private HttpMessageHandler? _current;

    internal void Set(HttpMessageHandler? handler) => Volatile.Write(ref _current, handler);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var handler = Volatile.Read(ref _current);
        if (handler is null)
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        return invoker.SendAsync(request, cancellationToken);
    }
}

internal sealed class SwappableLicensingDatabase(string initialConnectionString)
{
    private string _connectionString = initialConnectionString;

    internal string ConnectionString => Volatile.Read(ref _connectionString);

    internal void Set(string connectionString) => Volatile.Write(ref _connectionString, connectionString);
}

internal sealed class SwappableBootstrapRateBudget(SwappableLicensingDatabase database) : IBootstrapRateBudget
{
    public async Task<DateTimeOffset?> TakeAsync(bool isResume, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '3s'", connection, transaction);
        await timeout.ExecuteNonQueryAsync(cancellationToken);
        var global = await TakeBucketAsync(connection, transaction, 1, 30, now, TimeSpan.FromMinutes(5), cancellationToken);
        var reset = global ?? await TakeBucketAsync(
            connection,
            transaction,
            isResume ? 3 : 2,
            5,
            now,
            TimeSpan.FromMinutes(15),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reset;
    }

    public async Task<DateTimeOffset?> TakeInvitationAsync(bool isAcceptance, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '3s'", connection, transaction);
        await timeout.ExecuteNonQueryAsync(cancellationToken);
        var reset = isAcceptance
            ? await TakeBucketAsync(connection, transaction, 4, 30, now, TimeSpan.FromMinutes(15), cancellationToken)
            : await TakeBucketAsync(connection, transaction, 5, 20, now, TimeSpan.FromHours(1), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return reset;
    }

    private static async Task<DateTimeOffset?> TakeBucketAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int id,
        int limit,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
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

internal sealed class SwappableAdminMailer : IAdminInvitationMailer
{
    private IAdminInvitationMailer? _current;

    internal void Set(IAdminInvitationMailer mailer) => Volatile.Write(ref _current, mailer);

    public Task<bool> TrySendAsync(AdminInvitationMessage message, CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _current)
            ?? throw new InvalidOperationException("No active test mailer is bound to this pooled host.");
        return current.TrySendAsync(message, cancellationToken);
    }
}

internal sealed class SwappableTrafficFilter : IStartupFilter
{
    private P0213Traffic? _current;

    internal void Set(P0213Traffic? traffic) => Volatile.Write(ref _current, traffic);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, pipeline) =>
        {
            Volatile.Read(ref _current)?.Observe(context.Request.Path);
            return pipeline(context);
        });
        next(app);
    };
}

internal sealed record SeededProviderTemplate(
    string TemplateDatabase,
    string MfaKey,
    string SessionSigningKey,
    string OwnerPassword,
    string OwnerSeed,
    DateTimeOffset ReadyClockUtc,
    string? ManagerSeed = null,
    IReadOnlyDictionary<string, string>? ManagerCookies = null,
    Guid TrialPolicyId = default,
    int TrialPolicyRevision = 0);
