using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0208Tests
{
    [Fact]
    public async Task AcceptedAgreement_RemainsRecoverableAfterRetirement()
    {
        using var signing = new P0201Fixture(database, "retirement_history_fixture");
        await using var fixture = await StartAsync(signing);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        using var client = fixture.Api.CreateClient();
        var (plan, version, trial, prepared) = await PublishedPlanAsync(manager);
        await P0207Tests.SeedCoverageAsync(fixture.Superuser);
        var persisted = await FactsAsync(fixture, version);
        var actor = Guid.NewGuid();
        var path = VersionPath(version);

        // Model the accepted reference using the immutable publication hash and the previously selected public terms.
        var before = await ReadVersionAsync(client, signing, fixture, actor, path);
        Assert.True(before.GetProperty("availableForNewPurchase").GetBoolean());
        Assert.Equal(prepared.GetProperty("snapshot").GetProperty("plan").GetRawText(), before.GetProperty("terms").GetRawText());
        using var retired = await RetireAsync(manager, plan, version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var after = await ReadVersionAsync(client, signing, fixture, actor, path);
        Assert.False(after.GetProperty("availableForNewPurchase").GetBoolean());
        Assert.Equal(before.GetProperty("terms").GetRawText(), after.GetProperty("terms").GetRawText());
        Assert.Equal(persisted, await FactsAsync(fixture, version));
        await AssertHistoryAsync(manager, version, prepared, fixture.Clock.GetUtcNow());

        using var misbound = await ReadPublicAsync(client, signing, fixture, actor, path, PublishedCatalogueV1.Path);
        Assert.Equal(HttpStatusCode.Unauthorized, misbound.StatusCode);
        await AssertVersionReplayRefusedAsync(client, signing, actor, path);
        await AssertPrivateVersionsHiddenAsync(client, signing, fixture, manager, trial, actor);
    }

    private static string VersionPath(Guid version) => $"{PublishedCatalogueV1.Path}/versions/{version:D}";

    private static async Task<JsonElement> ReadVersionAsync(HttpClient client, P0201Fixture signing, P0206Fixture fixture,
        Guid actor, string path)
    {
        using var response = await ReadPublicAsync(client, signing, fixture, actor, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await P0206Fixture.BodyAsync(response);
    }

    private static async Task AssertHistoryAsync(P0110CookieBrowser manager, Guid version, JsonElement prepared,
        DateTimeOffset now)
    {
        using var response = await manager.SendAsync(HttpMethod.Get, "/api/admin/catalogue/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await P0206Fixture.BodyAsync(response)).GetProperty("items").EnumerateArray();
        var row = Assert.Single(rows, row => row.GetProperty("versionId").GetGuid() == version);
        Assert.Equal("retired", row.GetProperty("status").GetString());
        Assert.Equal(prepared.GetProperty("snapshot").GetRawText(), row.GetProperty("snapshot").GetRawText());
        Assert.Equal(prepared.GetProperty("snapshotHash").GetString(), row.GetProperty("snapshotHash").GetString());
        var retirement = row.GetProperty("retirement");
        Assert.Equal(Reason, retirement.GetProperty("reason").GetString());
        Assert.Equal(now, retirement.GetProperty("retiredAt").GetDateTimeOffset());
    }

    private static async Task AssertVersionReplayRefusedAsync(HttpClient client, P0201Fixture signing, Guid actor, string path)
    {
        var assertion = P0202Fixture.Sign(signing, actor, PublishedCatalogueV1.Read, [], claims => claims["path"] = path);
        using var first = await P0202Fixture.SendAsync(client, assertion, path: path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var replay = await P0202Fixture.SendAsync(client, assertion, path: path);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("Billing.DelegationReplayed", await P0206Fixture.CodeAsync(replay));
    }

    private static async Task AssertPrivateVersionsHiddenAsync(HttpClient client, P0201Fixture signing, P0206Fixture fixture,
        P0110CookieBrowser manager, Guid trial, Guid actor)
    {
        var draft = await P0207Tests.SavePlanAsync(manager, Guid.NewGuid(), P0207Tests.PlanFacts(0), HttpStatusCode.Created);
        var custom = P0207Tests.PlanFacts(0);
        custom["kind"] = "custom";
        custom["name"] = "Private Custom canary";
        custom["reason"] = "Private negotiation canary";
        custom["extraShopsOffered"] = false;
        custom["extraShopMonthlyPricePaise"] = null;
        custom["extraShopAnnualPricePaise"] = null;
        var plan = Guid.NewGuid();
        var customDraft = await P0207Tests.SavePlanAsync(manager, plan, custom, HttpStatusCode.Created);
        var version = customDraft.GetProperty("versionId").GetGuid();
        await PublishAsync(manager, plan, version, trial);
        using var retired = await RetireAsync(manager, plan, version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        Assert.Equal(0L, await fixture.ScalarAsync($"SELECT count(*) FROM withdrawn_catalogue_offers WHERE version_id = '{version}'"));

        foreach (var id in new[] { draft.GetProperty("versionId").GetGuid(), version, Guid.NewGuid() })
        {
            using var missing = await ReadPublicAsync(client, signing, fixture, actor, VersionPath(id));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("Billing.PlanVersionNotFound", await P0206Fixture.CodeAsync(missing));
            Assert.DoesNotContain("canary", await missing.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
