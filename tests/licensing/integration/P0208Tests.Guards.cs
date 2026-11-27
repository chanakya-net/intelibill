using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0208Tests
{
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
    }

    private static async Task AssertGuardAsync(P0206Fixture fixture, string sql)
    {
        var refused = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync(sql));
        Assert.Equal("55000", refused.SqlState);
    }
}
