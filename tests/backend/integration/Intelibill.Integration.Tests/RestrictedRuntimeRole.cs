using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The deployed role split, reproduced in the test container: migrations and
/// test inspection run as the owner, API requests run as a login role that
/// holds DML and nothing else. Without this the integration suite collects its
/// evidence as a superuser, which bypasses row-level security outright and
/// makes a green run say nothing about tenant isolation.
/// </summary>
/// <remarks>
/// Mirrors <c>scripts/db/phase7-bootstrap.sql</c>: USAGE on the schema without
/// CREATE, no ownership, no BYPASSRLS. The role is a cluster object, so it
/// survives the per-factory drop and recreate of the database; the privileges
/// are database objects and are re-applied after every migration.
/// </remarks>
internal static class RestrictedRuntimeRole
{
    public const string Name = "intelibill_app";

    public const string Password = "intelibill_app_password";

    public static string ConnectionStringFor(string migratorConnectionString, int? maxPoolSize = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(migratorConnectionString)
        {
            Username = Name,
            Password = Password,
        };

        if (maxPoolSize is not null)
        {
            builder.MaxPoolSize = maxPoolSize.Value;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates the login role, or re-asserts its attributes if a previous run
    /// left it behind. Attributes are restated rather than assumed: a role that
    /// kept BYPASSRLS from an earlier experiment would silently disarm every
    /// isolation assertion in the suite.
    /// </summary>
    public static async Task EnsureAsync(string migratorConnectionString)
    {
        await using var connection = new NpgsqlConnection(migratorConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, $"""
            do $$
            begin
                if not exists (select 1 from pg_roles where rolname = '{Name}') then
                    create role {Name} login password '{Password}';
                end if;
            end
            $$;
            alter role {Name} with login password '{Password}'
                nosuperuser nocreatedb nocreaterole nobypassrls noinherit;
            """);
    }

    /// <summary>
    /// Grants the runtime role exactly what the application needs: schema
    /// USAGE, table DML, sequence usage. Called after every migration, because
    /// the privileges live in the database the factory drops and recreates.
    /// </summary>
    public static async Task GrantAsync(string migratorConnectionString)
    {
        await using var connection = new NpgsqlConnection(migratorConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, $"""
            revoke create on schema public from public;
            revoke create on schema public from {Name};
            grant usage on schema public to {Name};
            grant select, insert, update, delete on all tables in schema public to {Name};
            grant usage, select on all sequences in schema public to {Name};
            alter default privileges in schema public
                grant select, insert, update, delete on tables to {Name};
            alter default privileges in schema public
                grant usage, select on sequences to {Name};
            """);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
