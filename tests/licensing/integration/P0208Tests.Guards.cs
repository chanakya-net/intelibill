using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0208Tests
{
    private const string PublicationMigration = "20261116130000_AddCataloguePublication";

    [Fact]
    public async Task RetirementEvidence_IsEnforcedAndRetainedByTheDatabase()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (plan, version, _, _) = await PublishedPlanAsync(manager);
        await AssertGuardAsync(fixture, $"""
            UPDATE plan_versions SET status = 3, revision = revision + 1
            WHERE id = '{version}'
            """);
        await AssertGuardAsync(fixture, $"""
            UPDATE plan_versions SET status = 3, revision = revision + 1 WHERE id = '{version}';
            INSERT INTO catalogue_retirements(version_id, plan_id, admin_id, operation_key, expected_version, reason, retired_at)
                SELECT id, plan_id, updated_by_admin_id, gen_random_uuid(), revision - 1, 'Missing marker', updated_at
                FROM plan_versions WHERE id = '{version}';
            """);
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{version}'"));

        using var retired = await RetireAsync(manager, plan, version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        await AssertGuardAsync(fixture, $"UPDATE plan_versions SET annual_price_paise = 0, revision = revision + 1 WHERE id = '{version}'");
        await AssertGuardAsync(fixture, "DELETE FROM catalogue_retirements");
        await AssertGuardAsync(fixture, "UPDATE catalogue_retirements SET reason = 'Rewritten'");
        await AssertGuardAsync(fixture, "DELETE FROM withdrawn_catalogue_offers");
        await AssertGuardAsync(fixture, "TRUNCATE withdrawn_catalogue_offers");
        await AssertOneRetirementAsync(fixture);
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime, "SELECT count(*) FROM catalogue_retirements"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Runtime, "SELECT count(*) FROM withdrawn_catalogue_offers"));

        using var migrationFixture = new P0201Fixture(database, fixture.Runtime.Database!);
        await using var context = migrationFixture.OpenMigrationContext();
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.GetService<IMigrator>().MigrateAsync(PublicationMigration));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused.InnerException).SqlState);
        await AssertOneRetirementAsync(fixture);
    }

    [Fact]
    public async Task RetirementMigration_KeepsPopulatedCatalogueAndOpenDraft()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        using var migrationFixture = new P0201Fixture(database, fixture.Runtime.Database!);
        await using var context = migrationFixture.OpenMigrationContext();
        await context.GetService<IMigrator>().MigrateAsync(PublicationMigration);
        await LicensingPostgreSqlFixture.ExecuteAsync(migrationFixture.Owner, """
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT ON TABLES TO licensing_runtime
            """);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (plan, version, trial, _) = await PublishedPlanAsync(manager);
        var before = await FactsAsync(fixture, version);
        var facts = P0207Tests.PlanFacts(0);
        facts["sourceVersionId"] = version;
        facts["sourceExpectedVersion"] = 2;
        var draft = await P0207Tests.SavePlanAsync(manager, plan, facts, HttpStatusCode.Created);
        var draftId = draft.GetProperty("versionId").GetGuid();

        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(before, await FactsAsync(fixture, version));
        using var retired = await RetireAsync(manager, plan, version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        Assert.Equal(1, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{draftId}'"));
        await PublishAsync(manager, plan, draftId, trial);
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{draftId}'"));
    }

    private static async Task AssertGuardAsync(P0206Fixture fixture, string sql)
    {
        var refused = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync(sql));
        Assert.Equal("55000", refused.SqlState);
    }
}
