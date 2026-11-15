using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0207Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task TrialPublication_RequiresCompleteApprovedConfiguration()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        using var draft = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial,
            new { expectedVersion = 0, activeMemberCap = 5 }, await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var id = (await P0206Fixture.BodyAsync(draft)).GetProperty("draft").GetProperty("versionId").GetGuid();
        var path = $"/api/admin/trial-policy/versions/{id}";
        var operationKey = Guid.NewGuid();
        var proof = await P0206Fixture.ProofAsync(manager);
        using var refused = await manager.SendAsync(HttpMethod.Post, path + "/publish-preview",
            new { expectedVersion = 1, operationKey, reason = "Local verification" }, proof);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Catalogue.Incomplete", await P0206Fixture.CodeAsync(refused));
        using var complete = await manager.SendAsync(HttpMethod.Put, P0206Fixture.Trial,
            new { expectedVersion = 1, activeMemberCap = 5, maxConcurrentTrialShops = 2, trialTermsVersion = "trial-local-1" }, proof);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        using var preview = await manager.SendAsync(HttpMethod.Post, path + "/publish-preview",
            new { expectedVersion = 2, operationKey, reason = "Local verification" }, proof);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var prepared = await P0206Fixture.BodyAsync(preview);
        Assert.True(prepared.GetProperty("snapshot").GetProperty("developmentFixture").GetBoolean());
        Assert.Equal(1, await fixture.ScalarAsync($"SELECT status FROM trial_policy_versions WHERE id = '{id}'"));
        using var published = await manager.SendAsync(HttpMethod.Post, path + "/publish",
            new { expectedVersion = 2, operationKey, snapshotHash = prepared.GetProperty("snapshotHash").GetString(), confirmed = true }, proof);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(2, await fixture.ScalarAsync($"SELECT status FROM trial_policy_versions WHERE id = '{id}'"));
    }

    [Fact]
    public async Task PlanPublication_ExposesOnlyTheConfirmedCompleteSnapshot()
    {
        await using var fixture = await P0206Fixture.StartAsync(database, LocalSettings());
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var trial = await PublishTrialAsync(manager);
        var planId = Guid.NewGuid();
        var draft = await SavePlanAsync(manager, planId, PlanFacts(0), HttpStatusCode.Created);
        var versionId = draft.GetProperty("versionId").GetGuid();
        var path = $"/api/admin/plans/{planId}/versions/{versionId}";
        var key = Guid.NewGuid();
        fixture.Clock.Advance(TimeSpan.FromSeconds(299));
        var prepared = await PrepareAsync(manager, path, key, 1, trial);
        var terms = prepared.GetProperty("snapshot").GetProperty("plan");
        Assert.Equal(0, terms.GetProperty("monthlyPricePaise").GetInt64());
        Assert.Equal("TaxInclusiveFinal", terms.GetProperty("priceBasis").GetString());
        Assert.Equal(trial, terms.GetProperty("trialPolicy").GetProperty("versionId").GetGuid());
        Assert.Equal(1, await fixture.ScalarAsync($"SELECT status FROM plan_versions WHERE id = '{versionId}'"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        using var expired = await ConfirmAsync(manager, path, prepared);
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        Assert.Equal("Catalogue.RecentMfaRequired", await P0206Fixture.CodeAsync(expired));
    }

    internal static Dictionary<string, string?> LocalSettings() => new()
    {
        ["CataloguePublication:Mode"] = "LocalFixture",
        ["CataloguePublication:Revision"] = "local-1",
        ["CataloguePublication:TrialTermsVersion"] = "trial-local-1",
        ["CataloguePublication:CustomerTermsVersion"] = "customer-local-1",
        ["CataloguePublication:RefundPolicyVersion"] = "refund-local-1",
        ["CataloguePublication:PriceBasis"] = "TaxInclusiveFinal",
        ["CataloguePublication:AccountingTreatmentReference"] = "fixture-accounting",
    };

    private static Dictionary<string, object?> PlanFacts(int version, long annualPrice = 120000) => new()
    {
        ["expectedVersion"] = version, ["kind"] = "multi-shop", ["name"] = "Local growth",
        ["monthlyPricePaise"] = 0L, ["annualPricePaise"] = annualPrice, ["includedShops"] = 3,
        ["membersPerShop"] = 5, ["extraShopsOffered"] = true, ["extraShopMonthlyPricePaise"] = 15000L,
        ["extraShopAnnualPricePaise"] = 150000L, ["customerTermsVersion"] = "customer-local-1",
        ["refundPolicyVersion"] = "refund-local-1",
    };

    private static async Task<Guid> PublishTrialAsync(P0110CookieBrowser manager)
    {
        var id = await CreateTrialDraftAsync(manager);
        var path = $"/api/admin/trial-policy/versions/{id}";
        var prepared = await PrepareAsync(manager, path, Guid.NewGuid(), 1);
        using var published = await ConfirmAsync(manager, path, prepared);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return id;
    }

    private static async Task<JsonElement> SavePlanAsync(P0110CookieBrowser manager, Guid planId, object body,
        HttpStatusCode status)
    {
        using var response = await P0206Fixture.PutPlanAsync(manager, planId, body);
        Assert.Equal(status, response.StatusCode);
        return status is HttpStatusCode.Created or HttpStatusCode.OK
            ? (await P0206Fixture.BodyAsync(response)).GetProperty("draft") : default;
    }

    private static async Task<JsonElement> PrepareAsync(P0110CookieBrowser manager, string path, Guid key, int expected,
        Guid? trial = null)
    {
        using var response = await manager.SendAsync(HttpMethod.Post, path + "/publish-preview",
            new { operationKey = key, expectedVersion = expected, trialPolicyVersionId = trial, reason = "Local verification" },
            await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await P0206Fixture.BodyAsync(response);
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(P0110CookieBrowser manager, string path, JsonElement prepared) =>
        await manager.SendAsync(HttpMethod.Post, path + "/publish", new
        {
            operationKey = prepared.GetProperty("operationKey").GetGuid(),
            expectedVersion = prepared.GetProperty("expectedVersion").GetInt32(),
            snapshotHash = prepared.GetProperty("snapshotHash").GetString(), confirmed = true,
        }, await P0206Fixture.ProofAsync(manager));
}
