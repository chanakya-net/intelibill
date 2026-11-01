using System.Globalization;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The manifest is only worth having if it cannot drift. One test compares it
/// to the EF model for exact set equality, so a table added by a future
/// migration fails here until someone has decided how the tenant boundary
/// treats it.
/// </summary>
[Collection("Integration Tests")]
public sealed class TenantPolicyManifestTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryMappedTable_HasExplicitSecurityClassification()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var mapped = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null)
            .Select(table => table!)
            .ToHashSet(StringComparer.Ordinal);

        var classified = TenantPolicyManifest.Entries
            .Select(entry => entry.Table)
            .ToHashSet(StringComparer.Ordinal);

        var unclassified = mapped.Except(classified).Order(StringComparer.Ordinal).ToList();
        var stale = classified.Except(mapped).Order(StringComparer.Ordinal).ToList();

        Assert.Empty(unclassified);
        Assert.Empty(stale);
        Assert.Equal(TenantPolicyManifest.Entries.Count, classified.Count);
    }

    [Fact]
    public void EveryExemptTable_RecordsWhyItIsExempt()
    {
        var unexplained = TenantPolicyManifest.ExemptTables
            .Where(entry => string.IsNullOrWhiteSpace(entry.ExemptionReason))
            .Select(entry => entry.Table)
            .ToList();

        Assert.Empty(unexplained);
    }

    [Fact]
    public void EveryTenantTable_NamesItsPolicyPredicateAndIndex()
    {
        var incomplete = TenantPolicyManifest.TenantTables
            .Where(entry =>
                string.IsNullOrWhiteSpace(entry.PolicyName) ||
                string.IsNullOrWhiteSpace(entry.PredicateColumn) ||
                string.IsNullOrWhiteSpace(entry.RequiredIndexColumn) ||
                entry.ExemptionReason is not null)
            .Select(entry => entry.Table)
            .ToList();

        Assert.Empty(incomplete);

        var childrenWithoutParent = TenantPolicyManifest.TenantTables
            .Where(entry => entry.Classification == TenantSecurityClassification.TenantChildData)
            .Where(entry => string.IsNullOrWhiteSpace(entry.ParentTable))
            .Select(entry => entry.Table)
            .ToList();

        Assert.Empty(childrenWithoutParent);
    }

    /// <summary>
    /// Read from the catalog, not from the manifest: the manifest states intent,
    /// and this is the only thing that says what the database actually enforces.
    /// FORCE matters because the migrator owns these tables, and an owner is
    /// exempt from its own policies without it.
    /// </summary>
    [Fact]
    public async Task TenantTables_EnableAndForceRls()
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();

        var offenders = new List<string>();
        foreach (var entry in TenantPolicyManifest.TenantTables)
        {
            var (enabled, forced) = await ReadRlsFlagsAsync(connection, entry.Table);
            if (!enabled || !forced)
            {
                offenders.Add($"{entry.Table}: enabled={enabled}, forced={forced}");
                continue;
            }

            var policies = await ReadPoliciesAsync(connection, entry.Table);
            if (policies.Count != 1)
            {
                offenders.Add($"{entry.Table}: {policies.Count} policies, expected exactly 1");
                continue;
            }

            var (name, usingExpression, checkExpression) = policies[0];
            if (!string.Equals(name, entry.PolicyName, StringComparison.Ordinal))
                offenders.Add($"{entry.Table}: policy is '{name}', manifest says '{entry.PolicyName}'");
            else if (usingExpression is null || checkExpression is null)
                offenders.Add($"{entry.Table}: USING or WITH CHECK is missing");
            else if (!usingExpression.Contains("nullif", StringComparison.OrdinalIgnoreCase)
                || !checkExpression.Contains("nullif", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{entry.Table}: predicate does not parse an absent context safely");
            else if (!usingExpression.Contains(entry.PredicateColumn!, StringComparison.Ordinal))
                offenders.Add($"{entry.Table}: predicate does not read {entry.PredicateColumn}");
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Review focus 1: a policy on any identity table breaks login, switching or
    /// invitation acceptance, so their absence from row-level security is asserted
    /// rather than assumed.
    /// </summary>
    [Fact]
    public async Task ExemptTables_StayOutsideRowLevelSecurity()
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();

        var offenders = new List<string>();
        foreach (var entry in TenantPolicyManifest.ExemptTables.Concat(TenantPolicyManifest.UnmappedInfrastructureTables))
        {
            var (enabled, _) = await ReadRlsFlagsAsync(connection, entry.Table);
            var policies = await ReadPoliciesAsync(connection, entry.Table);

            if (enabled || policies.Count > 0)
                offenders.Add($"{entry.Table}: enabled={enabled}, policies={policies.Count}");
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The predicate runs on every row the statement touches, so the column it
    /// reads has to lead an index.
    /// </summary>
    [Fact]
    public async Task TenantTables_HaveShopIdIndex()
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();

        var offenders = new List<string>();
        foreach (var entry in TenantPolicyManifest.TenantTables)
        {
            await using var command = new NpgsqlCommand(
                """
                select count(*)
                from pg_index i
                join pg_class c on c.oid = i.indrelid
                join pg_namespace n on n.oid = c.relnamespace
                join pg_attribute a on a.attrelid = c.oid and a.attnum = i.indkey[0]
                where n.nspname = 'public' and c.relname = @table and a.attname = @column
                """,
                connection);
            command.Parameters.AddWithValue("table", entry.Table);
            command.Parameters.AddWithValue("column", entry.RequiredIndexColumn!);

            if ((long)(await command.ExecuteScalarAsync())! == 0)
                offenders.Add($"{entry.Table}: no index leading with {entry.RequiredIndexColumn}");
        }

        Assert.Empty(offenders);
    }

    private static async Task<(bool Enabled, bool Forced)> ReadRlsFlagsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand(
            """
            select c.relrowsecurity, c.relforcerowsecurity
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'public' and c.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), string.Create(CultureInfo.InvariantCulture, $"Table {table} is missing from the database."));
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task<List<(string Name, string? Using, string? Check)>> ReadPoliciesAsync(
        NpgsqlConnection connection,
        string table)
    {
        await using var command = new NpgsqlCommand(
            """
            select p.polname,
                   pg_get_expr(p.polqual, p.polrelid),
                   pg_get_expr(p.polwithcheck, p.polrelid)
            from pg_policy p
            join pg_class c on c.oid = p.polrelid
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'public' and c.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("table", table);

        var policies = new List<(string, string?, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            policies.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return policies;
    }
}
