using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0208Tests
{
    [Fact]
    public async Task ConcurrentSameKeyRetirements_ReplayOneOutcome()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var (manager, seed) = await fixture.ManagerAsync();
        using var session = manager;
        using var other = await fixture.SignInOperatorAsync(seed);
        var (plan, version, _, _) = await PublishedPlanAsync(manager);
        var actor = (Guid)(await fixture.ScalarAsync(
            $"SELECT id FROM admin_identities WHERE lower(email) = lower('{P0114Tests.Invitee}')"))!;
        var key = Guid.NewGuid();
        var results = await RaceAsync(fixture, $"catalogue:operation:{actor:D}:{key:D}",
            () => RetireAsync(manager, plan, version, key), () => RetireAsync(other, plan, version, key));
        using var a = results.First;
        using var b = results.Second;
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        await AssertOneRetirementAsync(fixture);
    }

    [Fact]
    public async Task ConcurrentRetirements_ProduceOneOutcome()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, P0207Tests.LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (other, _) = await fixture.ManagerAsync("retirement-racing@example.test");
        using var otherSession = other;
        var (plan, version, _, _) = await PublishedPlanAsync(manager);
        var results = await RaceAsync(fixture, $"catalogue:plan:{plan:D}",
            () => RetireAsync(manager, plan, version, Guid.NewGuid()), () => RetireAsync(other, plan, version, Guid.NewGuid()));
        using var a = results.First;
        using var b = results.Second;
        var outcomes = new[] { a, b };
        Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("Catalogue.RetirementConflict", await P0206Fixture.CodeAsync(loser));
        await AssertOneRetirementAsync(fixture);
    }

    [Fact]
    public async Task RetirementRacingSuccessorPublication_NeverRelistsTheRetiredVersion()
    {
        using var signing = new P0201Fixture(database, "retirement_race_fixture");
        await using var fixture = await StartAsync(signing);
        var (manager, seed) = await fixture.ManagerAsync();
        using var session = manager;
        using var other = await fixture.SignInOperatorAsync(seed);
        using var client = fixture.Api.CreateClient();
        var (plan, version, trial, _) = await PublishedPlanAsync(manager);
        var facts = P0207Tests.PlanFacts(0, 390000);
        facts["sourceVersionId"] = version;
        facts["sourceExpectedVersion"] = 2;
        var draft = await P0207Tests.SavePlanAsync(manager, plan, facts, HttpStatusCode.Created);
        var successor = draft.GetProperty("versionId").GetGuid();
        var path = PlanPath(plan, successor);
        var prepared = await P0207Tests.PrepareAsync(other, path, Guid.NewGuid(), 1, trial);
        var results = await RaceAsync(fixture, $"catalogue:plan:{plan:D}",
            () => RetireAsync(manager, plan, version, Guid.NewGuid()), () => P0207Tests.ConfirmAsync(other, path, prepared));
        using var retired = results.First;
        using var published = results.Second;
        Assert.Contains(retired.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(3, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{version}'"));
        Assert.Equal(retired.IsSuccessStatusCode ? 1L : 0L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_retirements"));
        Assert.Equal(successor, Assert.Single(await OffersAsync(client, signing, fixture, Guid.NewGuid()))
            .GetProperty("versionId").GetGuid());
    }

    private static async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RaceAsync(P0206Fixture fixture,
        string resource, Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        await using var connection = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await connection.OpenAsync();
        await using var gate = await connection.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, gate);
        hold.Parameters.AddWithValue(resource);
        await hold.ExecuteNonQueryAsync();
        var a = first();
        var b = second();
        await P0207Tests.WaitForAdvisoryWaitersAsync(fixture, 2);
        await gate.CommitAsync();
        return (await a, await b);
    }

    private static async Task AssertOneRetirementAsync(P0206Fixture fixture)
    {
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM catalogue_retirements"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM withdrawn_catalogue_offers"));
    }
}
