using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Infrastructure.Data;

/// <summary>
/// Refuses to start when the runtime identity can ignore row-level security.
/// </summary>
/// <remarks>
/// A connection secret pointing at a superuser, or at a role holding
/// BYPASSRLS, switches the tenant boundary off everywhere at once and says
/// nothing about it: every query succeeds, including the ones that should have
/// returned nothing. That is not a failure anything downstream can detect, so
/// it is checked here, once, before the revision serves a request.
/// </remarks>
public sealed partial class RuntimeRoleGuard(
    NpgsqlDataSource dataSource,
    IOptions<DatabaseOptions> options,
    IHostEnvironment environment,
    ILogger<RuntimeRoleGuard> logger) : IHostedService
{
    /// <summary>
    /// On unless configuration says otherwise, except in Development, where the
    /// local database is normally the developer's own superuser.
    /// </summary>
    public static bool ShouldVerify(bool? configured, bool isDevelopment) =>
        configured ?? !isDevelopment;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!ShouldVerify(options.Value.RequireRestrictedRuntimeRole, environment.IsDevelopment()))
        {
            LogCheckSkipped(logger);
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await VerifyAsync(connection, cancellationToken);

        LogIdentityVerified(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Restricted runtime role check skipped. Set Database:RequireRestrictedRuntimeRole to enable it.")]
    private static partial void LogCheckSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runtime identity verified: it cannot bypass row-level security.")]
    private static partial void LogIdentityVerified(ILogger logger);

    /// <summary>
    /// Reads the connection's own role out of <c>pg_roles</c> and throws unless
    /// it is subject to row-level security.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The role is a superuser, holds BYPASSRLS, or could not be found.
    /// </exception>
    public static async Task VerifyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select current_user, rolsuper, rolbypassrls
            from pg_roles
            where rolname = current_user
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The runtime identity could not be read from pg_roles, so it cannot be shown to be " +
                "subject to row-level security. Refusing to start.");
        }

        var role = reader.GetString(0);
        var isSuperuser = reader.GetBoolean(1);
        var canBypass = reader.GetBoolean(2);

        if (isSuperuser || canBypass)
        {
            throw new InvalidOperationException(
                $"The runtime identity '{role}' can ignore row-level security " +
                $"(superuser: {isSuperuser}, bypassrls: {canBypass}), which switches tenant isolation " +
                "off for every request without any visible failure. Point the connection secret at the " +
                "restricted application role, and never resolve this by granting BYPASSRLS.");
        }
    }
}
