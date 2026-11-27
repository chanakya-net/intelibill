using Intelibill.Application.Common.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Intelibill.Api.HealthChecks;

/// <summary>
/// Readiness for identity mail. A replica that cannot queue mail (delivery disabled or no outbox
/// key) cannot enroll or recover anyone, so outside Development it is not ready. A slow or failing
/// provider is different: the queue keeps every message and retries it, so a backlog or a message
/// that ran out of attempts reports Degraded — visible to operators, still serving traffic —
/// rather than draining every replica because a third party is down.
/// </summary>
internal sealed class IdentityMailReadinessCheck(IIdentityMailOutbox outbox, NpgsqlDataSource dataSource, IHostEnvironment environment) : IHealthCheck
{
    internal static readonly TimeSpan BacklogThreshold = TimeSpan.FromMinutes(5);
    internal const int MaxAttempts = 5;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!outbox.Enabled)
        {
            return environment.IsDevelopment() || environment.IsEnvironment("Testing")
                ? HealthCheckResult.Degraded("Identity mail delivery is not configured.")
                : HealthCheckResult.Unhealthy("Identity mail delivery is not configured.");
        }

        try
        {
            await using var command = dataSource.CreateCommand("""
                SELECT count(*) FILTER (WHERE attempts >= $1),
                       coalesce(extract(epoch FROM now() - min(created_at) FILTER (WHERE attempts < $1)), 0)
                FROM identity_mail
                WHERE completed_at IS NULL AND expires_at > now()
                """);
            command.Parameters.AddWithValue(MaxAttempts);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var exhausted = reader.GetInt64(0);
            var oldestPending = TimeSpan.FromSeconds(Convert.ToDouble(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture));
            return Evaluate(exhausted, oldestPending);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            // PostgreSQL reachability is its own check; this one only reports on the queue.
            return HealthCheckResult.Degraded("Identity mail queue state is unavailable.");
        }
    }

    internal static HealthCheckResult Evaluate(long exhausted, TimeSpan oldestPending)
    {
        var data = new Dictionary<string, object>
        {
            ["exhausted"] = exhausted,
            ["oldestPendingSeconds"] = (long)oldestPending.TotalSeconds,
        };
        if (exhausted > 0)
            return HealthCheckResult.Degraded("Identity mail has messages that exhausted their delivery attempts.", data: data);
        if (oldestPending > BacklogThreshold)
            return HealthCheckResult.Degraded("Identity mail delivery is behind.", data: data);
        return HealthCheckResult.Healthy(data: data);
    }
}
