using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Options;

internal class LicensingDatabaseOptions
{
    internal const string SectionName = "LicensingDatabase";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public int MaxPoolSize { get; init; } = 12;
    public int TimeoutSeconds { get; init; } = 5;

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Database) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        Port is >= 1 and <= 65535 &&
        MaxPoolSize is >= 1 and <= 200 &&
        TimeoutSeconds is >= 1 and <= 30;

    internal string ToConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password,
        MaxPoolSize = MaxPoolSize,
        Timeout = TimeoutSeconds,
        CommandTimeout = TimeoutSeconds,
    }.ConnectionString;
}
