using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0208Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Reason = "Fixture offer withdrawn";

    [Fact]
    public async Task RetiredPlan_LeavesNewSelectionAndKeepsItsFacts()
    {
        using var signing = new P0201Fixture(database, "retirement_fixture");
        await using var fixture = await StartAsync(signing);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        using var client = fixture.Api.CreateClient();
        var (plan, version, trial, _) = await PublishedPlanAsync(manager);
        var successor = await PublishSuccessorAsync(manager, plan, version, 2, trial);
        var retained = await FactsAsync(fixture, successor);
        var actor = Guid.NewGuid();
        Assert.Equal(successor, Assert.Single(await OffersAsync(client, signing, fixture, actor))
            .GetProperty("versionId").GetGuid());

        using var retired = await RetireAsync(manager, plan, successor, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        Assert.Equal(fixture.Clock.GetUtcNow(), (await P0206Fixture.BodyAsync(retired)).GetProperty("retiredAt").GetDateTimeOffset());
        Assert.Equal(3, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{successor}'"));
        Assert.Equal(3, await fixture.ScalarAsync($"SELECT revision FROM plan_versions WHERE id = '{successor}'"));
        Assert.Equal(retained, await FactsAsync(fixture, successor));
        Assert.Empty(await OffersAsync(client, signing, fixture, actor));

        var republished = await PublishSuccessorAsync(manager, plan, successor, 3, trial);
        Assert.Equal(republished, Assert.Single(await OffersAsync(client, signing, fixture, actor))
            .GetProperty("versionId").GetGuid());
    }

    private Task<P0206Fixture> StartAsync(P0201Fixture signing)
    {
        var settings = P0207Tests.LocalSettings();
        foreach (var (key, value) in signing.LicensingSettings()) settings[key] = value;
        settings["LicensingDatabase:MaxPoolSize"] = "4";
        return P0206Fixture.StartAsync(database, settings);
    }

    private static async Task<(Guid Plan, Guid Version, Guid Trial, JsonElement Prepared)> PublishedPlanAsync(
        P0110CookieBrowser manager, Dictionary<string, object?>? facts = null)
    {
        var trial = await P0207Tests.PublishTrialAsync(manager);
        var plan = Guid.NewGuid();
        var draft = await P0207Tests.SavePlanAsync(manager, plan, facts ?? P0207Tests.PlanFacts(0), HttpStatusCode.Created);
        var version = draft.GetProperty("versionId").GetGuid();
        return (plan, version, trial, await PublishAsync(manager, plan, version, trial));
    }

    private static async Task<Guid> PublishSuccessorAsync(P0110CookieBrowser manager, Guid plan, Guid version,
        int revision, Guid trial)
    {
        var facts = P0207Tests.PlanFacts(0, 245000);
        facts["sourceVersionId"] = version;
        facts["sourceExpectedVersion"] = revision;
        var draft = await P0207Tests.SavePlanAsync(manager, plan, facts, HttpStatusCode.Created);
        var successor = draft.GetProperty("versionId").GetGuid();
        await PublishAsync(manager, plan, successor, trial);
        return successor;
    }

    private static async Task<JsonElement> PublishAsync(P0110CookieBrowser manager, Guid plan, Guid version, Guid trial)
    {
        var path = PlanPath(plan, version);
        var prepared = await P0207Tests.PrepareAsync(manager, path, Guid.NewGuid(), 1, trial);
        using var published = await P0207Tests.ConfirmAsync(manager, path, prepared);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return prepared;
    }

    private static string PlanPath(Guid plan, Guid version) => $"/api/admin/plans/{plan}/versions/{version}";

    private static async Task<HttpResponseMessage> RetireAsync(P0110CookieBrowser manager, Guid plan, Guid version,
        Guid key, int expectedVersion = 2, string reason = Reason) =>
        await manager.SendAsync(HttpMethod.Post, PlanPath(plan, version) + "/retire",
            new { operationKey = key, expectedVersion, reason, confirmed = true }, await P0206Fixture.ProofAsync(manager));

    private static async Task<JsonElement[]> OffersAsync(HttpClient client, P0201Fixture signing, P0206Fixture fixture, Guid actor)
    {
        using var response = await ReadPublicAsync(client, signing, fixture, actor, PublishedCatalogueV1.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await P0206Fixture.BodyAsync(response)).GetProperty("items").EnumerateArray().ToArray();
    }

    private static Task<HttpResponseMessage> ReadPublicAsync(HttpClient client, P0201Fixture signing, P0206Fixture fixture,
        Guid actor, string path, string? signedPath = null)
    {
        signing.Clock.Now = fixture.Clock.GetUtcNow();
        var signed = P0202Fixture.Sign(signing, actor, PublishedCatalogueV1.Read, [], claims => claims["path"] = signedPath ?? path);
        return P0202Fixture.SendAsync(client, signed, path: path);
    }

    private static Task<object?> FactsAsync(P0206Fixture fixture, Guid version) => fixture.ScalarAsync($"""
        SELECT jsonb_build_array(
            (SELECT to_jsonb(v) - ARRAY['status', 'revision', 'updated_at', 'updated_by_admin_id'] FROM plan_versions v WHERE id = '{version}'),
            (SELECT to_jsonb(p) FROM catalogue_preparations p JOIN catalogue_publications c USING (admin_id, operation_key)
                WHERE c.version_id = '{version}'),
            (SELECT to_jsonb(c) FROM catalogue_publications c WHERE version_id = '{version}'),
            (SELECT to_jsonb(o) FROM published_catalogue_offers o WHERE version_id = '{version}'),
            (SELECT jsonb_agg(t) FROM subscription_coverage_terms t),
            (SELECT jsonb_agg(a) FROM shop_affiliations a))::text
        """);
}
