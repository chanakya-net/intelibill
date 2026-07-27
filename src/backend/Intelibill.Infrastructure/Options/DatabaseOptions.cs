using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Intelibill.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 5432;

    [Required]
    public string Database { get; init; } = string.Empty;

    [Required]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Unset under Entra authentication: there is no password, an access token
    /// takes its place and is supplied per connection by the data source.
    /// Required otherwise — see <see cref="DatabaseOptionsValidator"/>.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Connections per replica. Npgsql defaults to 100, which a few replicas can
    /// multiply past what a B1ms server allows in total. Leave headroom for
    /// migrations, administration, cache traffic, and recovery.
    /// </summary>
    [Range(1, 200)]
    public int MaxPoolSize { get; init; } = 12;

    /// <summary>
    /// Authenticate with Microsoft Entra access tokens instead of a password.
    /// </summary>
    public bool UseEntraAuth { get; init; }

    /// <summary>
    /// Client ID of the user-assigned managed identity to request tokens for.
    /// Only needed when <see cref="UseEntraAuth"/> is set and the environment does
    /// not already carry <c>AZURE_CLIENT_ID</c>; a host with more than one assigned
    /// identity cannot pick for us.
    /// </summary>
    public string? ManagedIdentityClientId { get; init; }

    /// <summary>
    /// Built through <see cref="NpgsqlConnectionStringBuilder"/> rather than by
    /// interpolation, which corrupts the string for any password containing
    /// <c>;</c> or <c>=</c>.
    /// </summary>
    public string ToConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            MaxPoolSize = MaxPoolSize,
            // Entra tokens are bearer credentials: readable on the wire without
            // TLS, so Require rather than Prefer.
            SslMode = UseEntraAuth ? SslMode.Require : SslMode.Prefer,
        };

        if (!UseEntraAuth)
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
