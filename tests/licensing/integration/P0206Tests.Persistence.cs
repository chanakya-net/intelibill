using System.Net;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0206Tests
{
    private const string Predecessor = "20261115230000_AddShopAffiliations";
    private const string Owner = "(SELECT owner_id FROM initial_admin_bootstrap)";
    private const string PlanRow = "'00000000-0000-0000-0000-0000000000a1'";

    private static string Version(string id, string columns = "", string values = "") => $"""
        INSERT INTO plan_versions(id, plan_id, version_number, status, revision, name, currency, created_at,
            created_by_admin_id, updated_at, updated_by_admin_id{columns})
        VALUES ('{id}', {PlanRow}, 1, 1, 1, 'Fixture', 'INR', now(), {Owner}, now(), {Owner}{values})
        """;

    [Fact]
    public async Task DatabaseConstraints_RefuseInvalidDraftFacts()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        await fixture.ExecuteAsync($"INSERT INTO plans(id, kind, created_at, created_by_admin_id) VALUES ({PlanRow}, 2, now(), {Owner})");
        var id = Guid.NewGuid().ToString();
        foreach (var sql in new[]
                 {
                     Version(id, ", monthly_price_paise", ", -1"),
                     Version(id, ", annual_price_paise", ", 1000000000001"),
                     Version(id, ", included_shops", ", 0"),
                     Version(id, ", members_per_shop", ", -1"),
                     Version(id, ", extra_shop_monthly_price_paise", ", 100"),
                     Version(id, ", extra_shops_offered, extra_shop_annual_price_paise", ", false, 100"),
                     Version(id).Replace("'INR'", "'USD'", StringComparison.Ordinal),
                     Version(id).Replace("1, 1, 1, 'Fixture'", "1, 4, 1, 'Fixture'", StringComparison.Ordinal),
                     Version(id).Replace("'Fixture'", "' Fixture'", StringComparison.Ordinal),
                     Version(id, ", customer_terms_version", ", 'terms v1'"),
                     $"INSERT INTO plans(id, kind, created_at, created_by_admin_id) VALUES (gen_random_uuid(), 0, now(), {Owner})",
                     TrialRow("duration_days", "30"),
                     TrialRow("active_member_cap", "0"),
                 })
            await AssertSqlStateAsync(fixture, sql, PostgresErrorCodes.CheckViolation);

        await fixture.ExecuteAsync(Version(id));
        await AssertSqlStateAsync(fixture, Version(Guid.NewGuid().ToString()).Replace(", 1, 1, 1,", ", 2, 1, 1,", StringComparison.Ordinal),
            PostgresErrorCodes.UniqueViolation);
        foreach (var sql in new[]
                 {
                     $"UPDATE plan_versions SET revision = revision + 2 WHERE id = '{id}'",
                     $"UPDATE plan_versions SET status = 2, revision = revision + 1 WHERE id = '{id}'",
                     $"UPDATE plan_versions SET plan_id = gen_random_uuid(), revision = revision + 1 WHERE id = '{id}'",
                     $"DELETE FROM plan_versions WHERE id = '{id}'",
                     "UPDATE plans SET kind = 3",
                     "DELETE FROM plans",
                     "TRUNCATE plan_versions, plans CASCADE",
                 })
            await AssertSqlStateAsync(fixture, sql, "55000");
        await fixture.ExecuteAsync($"UPDATE plan_versions SET revision = revision + 1, name = 'Renamed' WHERE id = '{id}'");
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT revision FROM plan_versions WHERE id = '{id}'"));
    }

    [Fact]
    public async Task CatalogueMigration_MatchesModelAndRefusesEvidenceLoss()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var migrator = database.Login(fixture.Runtime.Database!, LicensingPostgreSqlFixture.MigratorRole);
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(migrator.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var context = new LicensingDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(true, await fixture.ScalarAsync("""
            SELECT bool_and(relrowsecurity AND relforcerowsecurity) FROM pg_class
            WHERE oid IN ('public.plans'::regclass, 'public.plan_versions'::regclass,
                'public.trial_policy_versions'::regclass, 'public.catalogue_draft_changes'::regclass)
            """));

        var (manager, _) = await fixture.ManagerAsync();
        using (manager)
            await SaveAsync(manager, Guid.NewGuid(), MultiShop(0, ("annualPricePaise", null)), HttpStatusCode.Created);
        var history = context.GetService<IMigrator>();
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => history.MigrateAsync(Predecessor));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused as PostgresException ?? refused.InnerException).SqlState);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM plan_versions"));
        Assert.Equal(true, await fixture.ScalarAsync(
            "SELECT relforcerowsecurity FROM pg_class WHERE oid = 'public.plan_versions'::regclass"));
    }

    private static string TrialRow(string column, string value) => $"""
        INSERT INTO trial_policy_versions(id, version_number, status, revision, duration_days, created_at,
            created_by_admin_id, updated_at, updated_by_admin_id)
        VALUES (gen_random_uuid(), 1, 1, 1, 14, now(), {Owner}, now(), {Owner});
        UPDATE trial_policy_versions SET {column} = {value}, revision = revision + 1
        """;

    private static async Task AssertSqlStateAsync(P0206Fixture fixture, string sql, string state)
    {
        await using var connection = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(state, failure.SqlState);
    }
}
