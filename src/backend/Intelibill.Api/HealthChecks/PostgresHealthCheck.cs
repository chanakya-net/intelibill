using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Intelibill.Api.HealthChecks;

/// <summary>
/// Readiness probe for PostgreSQL. Deliberately goes through the application's
/// own <see cref="NpgsqlDataSource"/> rather than opening a connection of its
/// own: under Entra authentication the interesting failure is not "is the server
/// up" but "can this identity still get a token and be accepted", and only the
/// shared data source exercises that path.
/// </summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}
